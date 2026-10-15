import AppKit
import Combine
import KrillCore
import SwiftUI

@MainActor
final class MenuBarController: NSObject, NSPopoverDelegate {
    private let store: DashboardStore
    private let statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
    private let popover = NSPopover()
    private var subscriptions = Set<AnyCancellable>()

    init(store: DashboardStore) {
        self.store = store
        super.init()
        popover.delegate = self; popover.animates = true
        popover.contentViewController = NSHostingController(rootView: DashboardView(store: store))
        if let button = statusItem.button {
            button.target = self; button.action = #selector(statusClicked(_:))
            button.sendAction(on: [.leftMouseUp, .rightMouseUp]); button.imagePosition = .imageLeading
            button.setAccessibilityLabel("Krill 剩余额度")
        }
        store.objectWillChange.sink { [weak self] _ in
            // ObservableObject sends before mutation; redraw once values have been applied.
            Task { @MainActor [weak self] in self?.updateStatus() }
        }.store(in: &subscriptions)
        updateStatus()
    }
    private func updateStatus() {
        guard let button = statusItem.button else { return }
        button.image = progressIcon(); button.title = store.preferences.showPercentage ? " " + store.menuText : ""
        button.toolTip = store.menuTooltip
        button.setAccessibilityValue(store.menuTooltip)
        popover.behavior = store.preferences.panelPinned ? .applicationDefined : .transient
    }
    private func progressIcon() -> NSImage {
        let size = NSSize(width: 22, height: 22)
        let percent = store.percent
        let needsLogin = store.needsLogin
        let stale = store.isStale
        let balanceOnly = store.hasData && store.selectedPlan == nil
        let image = NSImage(size: size, flipped: false) { rect in
            let center = NSPoint(x: rect.midX, y: rect.midY)
            let track = NSBezierPath(ovalIn: NSRect(x: 3, y: 3, width: 16, height: 16))
            track.lineWidth = 2; NSColor.labelColor.withAlphaComponent(0.25).setStroke(); track.stroke()
            NSColor.labelColor.setStroke()
            if let percent, !needsLogin {
                let arc = NSBezierPath(); arc.lineWidth = 2.4; arc.lineCapStyle = .round
                if percent >= 100 { arc.appendOval(in: NSRect(x: 3, y: 3, width: 16, height: 16)) }
                else if percent > 0 { arc.appendArc(withCenter: center, radius: 8, startAngle: 90, endAngle: 90 - CGFloat(percent) * 3.6, clockwise: true) }
                arc.stroke()
            }
            let symbol = needsLogin ? "person.crop.circle.badge.questionmark" : percent == nil ? (balanceOnly ? "dollarsign" : "minus") : ""
            if !symbol.isEmpty, let icon = NSImage(systemSymbolName: symbol, accessibilityDescription: nil) {
                icon.draw(in: NSRect(x: 6, y: 6, width: 10, height: 10))
            }
            if stale {
                NSColor.labelColor.setFill(); NSBezierPath(ovalIn: NSRect(x: 16, y: 1, width: 4, height: 4)).fill()
            }
            return true
        }
        // Template rendering keeps the icon legible in light/dark menu bars and fullscreen spaces.
        image.isTemplate = true
        return image
    }
    @objc private func statusClicked(_ sender: Any?) {
        if NSApp.currentEvent?.type == .rightMouseUp { showMenu(); return }
        if popover.isShown { hidePanel() } else { showPanel() }
    }
    func showPanel() {
        guard let button = statusItem.button else { return }
        let available = button.window?.screen?.visibleFrame.height ?? 900
        let size = NSSize(width: 500, height: min(780, max(400, available - 36)))
        popover.contentSize = size
        popover.contentViewController?.preferredContentSize = size
        popover.contentViewController?.view.setFrameSize(size)
        popover.behavior = store.preferences.panelPinned ? .applicationDefined : .transient
        popover.show(relativeTo: button.bounds, of: button, preferredEdge: .minY)
        popover.contentViewController?.view.window?.makeKey()
        NSApp.activate(ignoringOtherApps: true)
        store.isPanelVisible = true; store.synchronizeLaunchStatus(); store.requestRefresh()
    }
    func hidePanel() { popover.performClose(nil); store.isPanelVisible = false }
    func popoverDidClose(_ notification: Notification) { store.isPanelVisible = false }
    private func showMenu() {
        let menu = NSMenu()
        let entries: [(String, Selector)] = [("打开详情", #selector(openFromMenu)), ("立即刷新", #selector(refreshFromMenu)),
                                             ("打开官网", #selector(websiteFromMenu)), ("退出 Krill Desktop", #selector(quitFromMenu))]
        for (title, action) in entries { let item = menu.addItem(withTitle: title, action: action, keyEquivalent: ""); item.target = self }
        statusItem.menu = menu; statusItem.button?.performClick(nil); statusItem.menu = nil
    }
    @objc private func openFromMenu() { showPanel() }
    @objc private func refreshFromMenu() { store.requestRefresh() }
    @objc private func websiteFromMenu() { store.openWebsite() }
    @objc private func quitFromMenu() { NSApp.terminate(nil) }
}
