// Notifier — best-effort in-app feedback toast (copied / saved).

import AppKit

enum Notifier {
    private static var window: NSWindow?
    private static var timer: Timer?

    /// "Copied to clipboard" toast.
    static func copiedToClipboard() {
        DispatchQueue.main.async { showToast("Copied to clipboard") }
    }

    /// "Saved to <path>" toast, with the home directory abbreviated to ~.
    static func saved(to url: URL) {
        let path = (url.path as NSString).abbreviatingWithTildeInPath
        DispatchQueue.main.async { showToast("Saved to \(path)") }
    }

    private static func showToast(_ message: String) {
        timer?.invalidate()
        window?.orderOut(nil)

        let label = NSTextField(labelWithString: message)
        label.font = .systemFont(ofSize: 12, weight: .semibold)
        label.textColor = Theme.tagText
        label.alignment = .center
        label.lineBreakMode = .byTruncatingMiddle
        label.translatesAutoresizingMaskIntoConstraints = false

        // Size to the message, clamped so long paths don't run off-screen.
        let textWidth = label.intrinsicContentSize.width
        let width = min(max(textWidth + 28, 180), 460)
        let height: CGFloat = 40

        let content = NSView(frame: NSRect(x: 0, y: 0, width: width, height: height))
        content.wantsLayer = true
        content.layer?.backgroundColor = Theme.tagBackground.cgColor
        content.layer?.borderColor = Theme.tagRule.cgColor
        content.layer?.borderWidth = 1
        content.layer?.cornerRadius = 8
        content.addSubview(label)
        NSLayoutConstraint.activate([
            label.leadingAnchor.constraint(equalTo: content.leadingAnchor, constant: 14),
            label.trailingAnchor.constraint(equalTo: content.trailingAnchor, constant: -14),
            label.centerYAnchor.constraint(equalTo: content.centerYAnchor),
        ])

        let screen = NSScreen.main?.visibleFrame ?? NSRect(x: 0, y: 0, width: 800, height: 600)
        let frame = NSRect(
            x: screen.maxX - width - 16,
            y: screen.maxY - 56,
            width: width,
            height: height
        )
        let toast = NSWindow(contentRect: frame, styleMask: .borderless, backing: .buffered, defer: false)
        toast.isOpaque = false
        toast.backgroundColor = .clear
        toast.level = .floating
        toast.ignoresMouseEvents = true
        toast.collectionBehavior = [.canJoinAllSpaces, .transient]
        toast.contentView = content
        toast.orderFrontRegardless()
        window = toast

        timer = Timer.scheduledTimer(withTimeInterval: 1.6, repeats: false) { _ in
            window?.orderOut(nil)
            window = nil
            timer = nil
        }
    }
}
