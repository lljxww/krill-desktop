import AppKit
import SwiftUI

@main
struct KrillApp: App {
    @NSApplicationDelegateAdaptor(AppDelegate.self) private var delegate
    var body: some Scene { Settings { EmptyView() } }
}

@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate {
    private var store: DashboardStore?
    private var menuBar: MenuBarController?
    private var login: LoginWindowController?
    func applicationDidFinishLaunching(_ notification: Notification) {
        NSApp.setActivationPolicy(.accessory)
        // The application bundle launches without a Dock icon.
        let demo = ProcessInfo.processInfo.arguments.contains("--demo")
        do {
            let storage = try LocalStorage(demo: demo)
            let store = DashboardStore(storage: storage, demo: demo)
            let login = LoginWindowController(store: store)
            let menuBar = MenuBarController(store: store)
            store.openLogin = { [weak login, weak menuBar] in menuBar?.hidePanel(); login?.show() }
            store.closePanel = { [weak menuBar] in menuBar?.hidePanel() }
            login.didClose = { [weak menuBar] in menuBar?.showPanel() }
            self.store = store; self.menuBar = menuBar; self.login = login
            store.start()
            if !ProcessInfo.processInfo.arguments.contains("--background") { menuBar.showPanel() }
        } catch {
            let alert = NSAlert(); alert.messageText = "无法启动 Krill Desktop"
            alert.informativeText = "请检查当前用户的应用支持目录是否可读写。"; alert.runModal(); NSApp.terminate(nil)
        }
    }
    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows flag: Bool) -> Bool { menuBar?.showPanel(); return true }
    func applicationWillTerminate(_ notification: Notification) { store?.stop(); login?.didClose = nil; login?.close() }
}
