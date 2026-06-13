// OnboardingWindow — first-run welcome panel, implemented in AppKit to keep
// release code size down while matching the rest of scrcap's native chrome.

import AppKit

final class OnboardingWindowController: NSObject, NSWindowDelegate {
    private var window: NSWindow?
    private let regionShortcut: String
    private let onEnablePermission: () -> Void
    private let onOpenPreferences: () -> Void

    init(regionShortcut: String, onEnablePermission: @escaping () -> Void, onOpenPreferences: @escaping () -> Void) {
        self.regionShortcut = regionShortcut
        self.onEnablePermission = onEnablePermission
        self.onOpenPreferences = onOpenPreferences
    }

    func show() {
        let content = OnboardingContentView(
            regionShortcut: regionShortcut,
            target: self
        )
        let win = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 460, height: 452),
                           styleMask: [.titled, .closable, .fullSizeContentView],
                           backing: .buffered,
                           defer: false)
        win.contentView = content
        win.titleVisibility = .hidden
        win.titlebarAppearsTransparent = true
        win.isMovableByWindowBackground = true
        win.standardWindowButton(.miniaturizeButton)?.isHidden = true
        win.standardWindowButton(.zoomButton)?.isHidden = true
        win.backgroundColor = Theme.chrome
        win.isReleasedWhenClosed = false
        win.delegate = self
        window = win

        NSApp.setActivationPolicy(.regular)
        NSApp.activate(ignoringOtherApps: true)
        win.center()
        win.makeKeyAndOrderFront(nil)
    }

    @objc fileprivate func enablePermission() {
        close()
        onEnablePermission()
    }

    @objc fileprivate func openPreferences() {
        close()
        onOpenPreferences()
    }

    @objc fileprivate func done() {
        close()
    }

    private func close() {
        window?.close()
    }

    func windowWillClose(_ notification: Notification) {
        window = nil
        DispatchQueue.main.async {
            let hasUserWindow = NSApp.windows.contains {
                $0.isVisible && $0.level == .normal && $0.canBecomeKey
            }
            if !hasUserWindow { NSApp.setActivationPolicy(.accessory) }
        }
    }
}

private final class OnboardingContentView: NSView {
    private weak var target: OnboardingWindowController?

    init(regionShortcut: String, target: OnboardingWindowController) {
        self.target = target
        super.init(frame: NSRect(x: 0, y: 0, width: 460, height: 452))
        wantsLayer = true
        layer?.backgroundColor = Theme.chrome.cgColor
        build(regionShortcut: regionShortcut)
    }

    required init?(coder: NSCoder) {
        nil
    }

    private func build(regionShortcut: String) {
        let root = NSStackView()
        root.orientation = .vertical
        root.spacing = 0
        root.translatesAutoresizingMaskIntoConstraints = false
        addSubview(root)

        let hero = NSStackView()
        hero.orientation = .vertical
        hero.alignment = .centerX
        hero.spacing = 9
        hero.edgeInsets = NSEdgeInsets(top: 34, left: 0, bottom: 24, right: 0)

        if let logo = Theme.logoImage(size: 46, template: true) {
            let image = NSImageView(image: logo)
            image.contentTintColor = Theme.accent
            hero.addArrangedSubview(image)
        }
        hero.addArrangedSubview(label("Welcome to scrcap", size: 20, weight: .bold, color: Theme.ink, centered: true))
        hero.addArrangedSubview(label("Screenshot, marked up, done.", size: 12.5, color: Theme.inkDim, centered: true))

        let features = NSStackView()
        features.orientation = .vertical
        features.alignment = .leading // left-align every row so the icon column lines up
        features.spacing = 12
        features.edgeInsets = NSEdgeInsets(top: 0, left: 26, bottom: 0, right: 26)
        features.addArrangedSubview(feature(
            symbol: "menubar.rectangle",
            title: "It lives in your menu bar",
            subtitle: "Click the camera icon for every capture, settings, and quit."
        ))
        features.addArrangedSubview(feature(
            symbol: "rectangle.dashed",
            title: "Grab a region in one press",
            subtitle: "Try it anywhere - the rest of the shortcuts are in the menu.",
            chip: regionShortcut
        ))
        features.addArrangedSubview(feature(
            symbol: "pencil.and.outline",
            title: "Mark up, then copy or save",
            subtitle: "Arrows, text, counters, pixelate - then Cmd-C or Cmd-S."
        ))

        let spacer = NSView()
        spacer.setContentHuggingPriority(.defaultLow, for: .vertical)

        let footerView = footer()
        root.addArrangedSubview(hero)
        root.addArrangedSubview(features)
        root.addArrangedSubview(spacer)
        root.addArrangedSubview(footerView)

        NSLayoutConstraint.activate([
            root.leadingAnchor.constraint(equalTo: leadingAnchor),
            root.trailingAnchor.constraint(equalTo: trailingAnchor),
            root.topAnchor.constraint(equalTo: topAnchor),
            root.bottomAnchor.constraint(equalTo: bottomAnchor),
            // Span full width so the left margin is consistent and the footer
            // background reaches both edges.
            hero.leadingAnchor.constraint(equalTo: root.leadingAnchor),
            hero.trailingAnchor.constraint(equalTo: root.trailingAnchor),
            features.leadingAnchor.constraint(equalTo: root.leadingAnchor),
            features.trailingAnchor.constraint(equalTo: root.trailingAnchor),
            footerView.leadingAnchor.constraint(equalTo: root.leadingAnchor),
            footerView.trailingAnchor.constraint(equalTo: root.trailingAnchor),
        ])
    }

    private func feature(symbol: String, title: String, subtitle: String, chip: String? = nil) -> NSView {
        let row = NSStackView()
        row.orientation = .horizontal
        row.alignment = .centerY
        row.spacing = 13

        let iconBox = NSView()
        iconBox.wantsLayer = true
        iconBox.layer?.backgroundColor = Theme.accent.withAlphaComponent(0.12).cgColor
        iconBox.layer?.cornerRadius = 7
        let config = NSImage.SymbolConfiguration(pointSize: 15, weight: .medium)
        let symbolImage = NSImage(systemSymbolName: symbol, accessibilityDescription: nil)?
            .withSymbolConfiguration(config)
        let icon = NSImageView(image: symbolImage ?? NSImage())
        icon.contentTintColor = Theme.accent
        icon.imageScaling = .scaleNone
        icon.translatesAutoresizingMaskIntoConstraints = false
        iconBox.addSubview(icon)
        NSLayoutConstraint.activate([
            iconBox.widthAnchor.constraint(equalToConstant: 34),
            iconBox.heightAnchor.constraint(equalToConstant: 34),
            icon.centerXAnchor.constraint(equalTo: iconBox.centerXAnchor),
            icon.centerYAnchor.constraint(equalTo: iconBox.centerYAnchor),
        ])

        let text = NSStackView()
        text.orientation = .vertical
        text.alignment = .leading
        text.spacing = 2

        let titleRow = NSStackView()
        titleRow.orientation = .horizontal
        titleRow.alignment = .centerY
        titleRow.spacing = 8
        titleRow.addArrangedSubview(label(title, size: 13, weight: .semibold, color: Theme.ink))
        if let chip {
            titleRow.addArrangedSubview(chipLabel(chip))
        }
        text.addArrangedSubview(titleRow)
        text.addArrangedSubview(label(subtitle, size: 11.5, color: Theme.inkDim))

        row.addArrangedSubview(iconBox)
        row.addArrangedSubview(text)
        return row
    }

    private func footer() -> NSView {
        let footer = NSView()
        footer.wantsLayer = true
        footer.layer?.backgroundColor = Theme.well.withAlphaComponent(0.5).cgColor

        let stack = NSStackView()
        stack.orientation = .vertical
        stack.spacing = 14
        stack.translatesAutoresizingMaskIntoConstraints = false
        footer.addSubview(stack)

        let notice = NSStackView()
        notice.orientation = .horizontal
        notice.alignment = .centerY
        notice.spacing = 7
        let lock = NSImageView(image: NSImage(systemSymbolName: "lock.shield", accessibilityDescription: nil) ?? NSImage())
        lock.contentTintColor = Theme.inkDim
        notice.addArrangedSubview(lock)
        notice.addArrangedSubview(label(
            "scrcap needs Screen Recording permission to capture your screen.",
            size: 11,
            color: Theme.inkDim
        ))

        let buttons = NSStackView()
        buttons.orientation = .horizontal
        buttons.spacing = 10
        buttons.addArrangedSubview(button("Preferences…", action: #selector(OnboardingWindowController.openPreferences)))
        let flex = NSView()
        flex.setContentHuggingPriority(.defaultLow, for: .horizontal)
        flex.setContentCompressionResistancePriority(.defaultLow, for: .horizontal)
        buttons.addArrangedSubview(flex)
        buttons.addArrangedSubview(button("Later", action: #selector(OnboardingWindowController.done)))
        buttons.addArrangedSubview(button("Enable Screen Recording", primary: true, action: #selector(OnboardingWindowController.enablePermission)))

        stack.addArrangedSubview(notice)
        stack.addArrangedSubview(buttons)

        let rule = NSView()
        rule.wantsLayer = true
        rule.layer?.backgroundColor = Theme.rule.withAlphaComponent(0.5).cgColor
        rule.translatesAutoresizingMaskIntoConstraints = false
        footer.addSubview(rule)

        NSLayoutConstraint.activate([
            footer.heightAnchor.constraint(equalToConstant: 105),
            rule.leadingAnchor.constraint(equalTo: footer.leadingAnchor),
            rule.trailingAnchor.constraint(equalTo: footer.trailingAnchor),
            rule.topAnchor.constraint(equalTo: footer.topAnchor),
            rule.heightAnchor.constraint(equalToConstant: 1),
            stack.leadingAnchor.constraint(equalTo: footer.leadingAnchor, constant: 22),
            stack.trailingAnchor.constraint(equalTo: footer.trailingAnchor, constant: -22),
            stack.topAnchor.constraint(equalTo: footer.topAnchor, constant: 16),
        ])
        return footer
    }

    private func label(_ text: String, size: CGFloat, weight: NSFont.Weight = .regular, color: NSColor, centered: Bool = false) -> NSTextField {
        let label = NSTextField(labelWithString: text)
        label.font = .systemFont(ofSize: size, weight: weight)
        label.textColor = color
        label.maximumNumberOfLines = 2
        label.lineBreakMode = .byWordWrapping
        label.alignment = centered ? .center : .left
        return label
    }

    private func chipLabel(_ text: String) -> NSTextField {
        let label = self.label(text, size: 11, weight: .semibold, color: Theme.ink)
        label.font = .monospacedSystemFont(ofSize: 11, weight: .semibold)
        label.wantsLayer = true
        label.layer?.borderColor = Theme.rule.cgColor
        label.layer?.borderWidth = 1
        label.layer?.cornerRadius = 4
        return label
    }

    private func button(_ title: String, primary: Bool = false, action: Selector) -> NSButton {
        let button = NSButton(title: title, target: target, action: action)
        button.bezelStyle = .rounded
        button.controlSize = .regular
        // Native push buttons render crisply; the primary one becomes the
        // default (blue, Return-activated) button.
        if primary { button.keyEquivalent = "\r" }
        return button
    }
}
