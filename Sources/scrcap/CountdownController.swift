// CountdownController — the on-screen countdown used by delayed capture. After
// a region is chosen, this shows a ticking numeral so the user can summon
// transient UI (a menu, a tooltip) before the shot fires. The window is
// click-through and tears down before the capture so it never appears in it.

import AppKit

final class CountdownController {
    private var window: NSWindow?
    private var timer: Timer?
    private var escMonitor: Any?
    private var remaining = 0
    private var onFire: (() -> Void)?

    /// Counts down `seconds` over `screen`, then fires `completion`. Esc cancels.
    func start(seconds: Int, on screen: NSScreen, completion: @escaping () -> Void) {
        cancel()
        remaining = max(1, seconds)
        onFire = completion

        let view = CountdownView(frame: NSRect(origin: .zero, size: screen.frame.size))
        view.value = remaining

        let win = NSWindow(
            contentRect: screen.frame,
            styleMask: .borderless,
            backing: .buffered,
            defer: false
        )
        win.isOpaque = false
        win.backgroundColor = .clear
        win.level = .screenSaver
        win.ignoresMouseEvents = true
        win.collectionBehavior = [.canJoinAllSpaces, .stationary, .fullScreenAuxiliary]
        win.contentView = view
        win.orderFrontRegardless()
        window = win

        escMonitor = NSEvent.addLocalMonitorForEvents(matching: .keyDown) { [weak self] event in
            if event.keyCode == 53 { self?.cancel(); return nil } // Esc
            return event
        }

        timer = Timer.scheduledTimer(withTimeInterval: 1, repeats: true) { [weak self] _ in
            self?.tick()
        }
    }

    private func tick() {
        remaining -= 1
        if remaining <= 0 {
            let fire = onFire
            teardown()
            // Let the window fully disappear before the capture reads the screen.
            DispatchQueue.main.async { fire?() }
            return
        }
        (window?.contentView as? CountdownView)?.value = remaining
    }

    func cancel() {
        teardown()
    }

    private func teardown() {
        timer?.invalidate()
        timer = nil
        if let escMonitor { NSEvent.removeMonitor(escMonitor) }
        escMonitor = nil
        window?.orderOut(nil)
        window = nil
        onFire = nil
    }
}

private final class CountdownView: NSView {
    var value: Int = 0 { didSet { needsDisplay = true } }

    override var isFlipped: Bool { false }

    override func draw(_ dirtyRect: NSRect) {
        let diameter: CGFloat = 132
        let circle = NSRect(
            x: (bounds.width - diameter) / 2,
            y: (bounds.height - diameter) / 2,
            width: diameter,
            height: diameter
        )

        let shadow = NSShadow()
        shadow.shadowColor = NSColor.black.withAlphaComponent(0.5)
        shadow.shadowBlurRadius = 24
        shadow.shadowOffset = .zero

        NSGraphicsContext.saveGraphicsState()
        shadow.set()
        Theme.tagBackground.setFill()
        NSBezierPath(ovalIn: circle).fill()
        NSGraphicsContext.restoreGraphicsState()

        Theme.tagRule.setStroke()
        let ring = NSBezierPath(ovalIn: circle.insetBy(dx: 0.5, dy: 0.5))
        ring.lineWidth = 1
        ring.stroke()

        let text = "\(value)"
        let attrs: [NSAttributedString.Key: Any] = [
            .font: NSFont.systemFont(ofSize: 72, weight: .semibold),
            .foregroundColor: Theme.tagText,
        ]
        let str = NSAttributedString(string: text, attributes: attrs)
        let size = str.size()
        str.draw(at: NSPoint(x: circle.midX - size.width / 2, y: circle.midY - size.height / 2))
    }
}
