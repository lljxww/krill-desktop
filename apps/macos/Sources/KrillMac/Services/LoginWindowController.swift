import AppKit
import KrillCore
@preconcurrency import WebKit

@MainActor
final class LoginWindowController: NSObject, NSWindowDelegate, WKNavigationDelegate, WKUIDelegate {
    private let store: DashboardStore
    private var window: NSWindow?
    private var webView: WKWebView?
    private let status = NSTextField(wrappingLabelWithString: "请在官网页面中选择 GitHub 登录。此窗口与 Safari 登录状态独立。")
    private var pollingTask: Task<Void, Never>?
    private var validationTask: Task<Void, Never>?
    private var checking = false
    private var rejectedToken: String?
    var didClose: (() -> Void)?

    init(store: DashboardStore) { self.store = store }
    func show() {
        if let window { NSApp.activate(ignoringOtherApps: true); window.makeKeyAndOrderFront(nil); return }
        let config = WKWebViewConfiguration()
        config.websiteDataStore = .nonPersistent()
        config.preferences.javaScriptCanOpenWindowsAutomatically = false
        let browser = WKWebView(frame: .zero, configuration: config)
        browser.navigationDelegate = self; browser.uiDelegate = self
        browser.allowsBackForwardNavigationGestures = true
        browser.isInspectable = false
        let content = NSView(); content.translatesAutoresizingMaskIntoConstraints = false
        browser.translatesAutoresizingMaskIntoConstraints = false; status.translatesAutoresizingMaskIntoConstraints = false
        status.font = .systemFont(ofSize: 12); status.textColor = .secondaryLabelColor
        content.addSubview(status); content.addSubview(browser)
        NSLayoutConstraint.activate([
            status.topAnchor.constraint(equalTo: content.topAnchor, constant: 12),
            status.leadingAnchor.constraint(equalTo: content.leadingAnchor, constant: 16),
            status.trailingAnchor.constraint(equalTo: content.trailingAnchor, constant: -16),
            browser.topAnchor.constraint(equalTo: status.bottomAnchor, constant: 12),
            browser.leadingAnchor.constraint(equalTo: content.leadingAnchor), browser.trailingAnchor.constraint(equalTo: content.trailingAnchor),
            browser.bottomAnchor.constraint(equalTo: content.bottomAnchor)
        ])
        let window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 900, height: 720),
                              styleMask: [.titled, .closable, .miniaturizable, .resizable], backing: .buffered, defer: false)
        window.title = "连接 Krill · GitHub 登录"; window.minSize = NSSize(width: 600, height: 500)
        window.contentView = content; window.delegate = self; window.isReleasedWhenClosed = false; window.center()
        self.window = window; webView = browser; rejectedToken = nil
        window.makeKeyAndOrderFront(nil); NSApp.activate(ignoringOtherApps: true)
        browser.load(URLRequest(url: KrillOrigin.url.appendingPathComponent("login")))
        pollingTask = Task { [weak self] in
            while !Task.isCancelled {
                do { try await Task.sleep(for: .milliseconds(1500)) } catch { return }
                self?.checkSession()
            }
        }
    }
    func close() { window?.close() }
    func windowWillClose(_ notification: Notification) {
        pollingTask?.cancel(); validationTask?.cancel(); checking = false
        webView?.stopLoading(); webView?.navigationDelegate = nil; webView?.uiDelegate = nil
        webView = nil; window = nil; didClose?()
    }
    func webView(_ webView: WKWebView, decidePolicyFor navigationAction: WKNavigationAction,
                 decisionHandler: @escaping (WKNavigationActionPolicy) -> Void) {
        // Subresources retain the website's own policy; restrict every top-level or new-window navigation.
        if navigationAction.targetFrame?.isMainFrame == false { decisionHandler(.allow); return }
        guard KrillOrigin.allowsLoginNavigation(navigationAction.request.url) else {
            status.stringValue = "登录窗口仅用于 Krill 与 GitHub 的 HTTPS 登录页面。"
            decisionHandler(.cancel); return
        }
        decisionHandler(.allow)
    }
    func webView(_ webView: WKWebView, decidePolicyFor navigationResponse: WKNavigationResponse,
                 decisionHandler: @escaping (WKNavigationResponsePolicy) -> Void) {
        decisionHandler(navigationResponse.canShowMIMEType ? .allow : .cancel)
    }
    func webView(_ webView: WKWebView, createWebViewWith configuration: WKWebViewConfiguration,
                 for navigationAction: WKNavigationAction, windowFeatures: WKWindowFeatures) -> WKWebView? {
        if KrillOrigin.allowsLoginNavigation(navigationAction.request.url) { webView.load(navigationAction.request) }
        return nil
    }
    func webView(_ webView: WKWebView, didFinish navigation: WKNavigation!) { checkSession() }
    func webView(_ webView: WKWebView, didFailProvisionalNavigation navigation: WKNavigation!, withError error: Error) {
        if (error as NSError).code != NSURLErrorCancelled { status.stringValue = "页面加载失败，可关闭窗口后重新连接。" }
    }
    private func checkSession() {
        guard !checking, let browser = webView, window != nil, KrillOrigin.isTrusted(browser.url) else { return }
        checking = true
        let script = """
        (() => {
          if (location.origin !== 'https://www.krill-code.com') return null;
          return {origin: location.origin, token: localStorage.getItem('krill_jwt')};
        })()
        """
        browser.evaluateJavaScript(script) { [weak self] result, _ in
            // WebKit invokes this completion on the main thread. Keep Any inside that boundary;
            // only the validated String crosses into the asynchronous verification task.
            MainActor.assumeIsolated {
                guard let self else { return }
                guard self.window != nil, KrillOrigin.isTrusted(self.webView?.url),
                      let object = result as? [String: Any], object["origin"] as? String == "https://www.krill-code.com",
                      let token = object["token"] as? String, KrillOrigin.isPlausibleSession(token), token != self.rejectedToken else {
                    self.checking = false; return
                }
                self.status.stringValue = "登录完成，正在确认账号…"
                self.validationTask = Task { [weak self] in
                    guard let self else { return }
                    defer { self.checking = false }
                    do {
                        try await self.store.acceptSession(token)
                        try Task.checkCancellation()
                        self.close()
                    } catch is CancellationError { }
                    catch let error as APIFailure {
                        if error == .sessionExpired { self.rejectedToken = token }
                        self.status.stringValue = error.message
                    } catch { self.status.stringValue = "暂时无法确认登录或保存钥匙串，请稍后重试。" }
                }
            }
        }
    }
}
