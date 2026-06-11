// EditorToolbar — dense strip docked at the top of the editor. Every control
// wears its shortcut. Tools left, palette center, undo right. Red is the
// primary accent; chrome follows the system light/dark appearance.

import AppKit

enum EditorStyle {
    static let height: CGFloat = 38
    static let accent = NSColor(srgbRed: 1.0, green: 0.23, blue: 0.19, alpha: 1) // #FF3B30
    static let accentFill = NSColor(srgbRed: 1.0, green: 0.23, blue: 0.19, alpha: 0.14)

    // Dynamic colors resolve against the appearance current at .cgColor time;
    // layer-backed views reapply them in viewDidChangeEffectiveAppearance.
    static let background = NSColor(name: nil) { appearance in
        appearance.isDark
            ? NSColor(srgbRed: 0.085, green: 0.09, blue: 0.10, alpha: 1)
            : NSColor(srgbRed: 0.965, green: 0.96, blue: 0.955, alpha: 1)
    }
    static let border = NSColor(name: nil) { appearance in
        appearance.isDark
            ? NSColor(srgbRed: 0.18, green: 0.19, blue: 0.21, alpha: 1)
            : NSColor(srgbRed: 0.80, green: 0.80, blue: 0.79, alpha: 1)
    }
    static let swatchRing = NSColor(name: nil) { appearance in
        appearance.isDark ? .white : NSColor(srgbRed: 0.15, green: 0.15, blue: 0.15, alpha: 1)
    }
}

extension NSAppearance {
    var isDark: Bool { bestMatch(from: [.darkAqua, .aqua]) == .darkAqua }
}

final class EditorToolbar: NSView {
    var onTool: ((EditorTool) -> Void)?
    var onColor: ((Int) -> Void)?
    var onUndo: (() -> Void)?
    var onSave: (() -> Void)?
    var onDone: (() -> Void)?

    private var toolButtons: [EditorTool: ToolbarButton] = [:]
    private var swatches: [SwatchButton] = []
    private var hairlines: [NSView] = []

    // The empty toolbar background is the window's drag handle.
    override var mouseDownCanMoveWindow: Bool { true }

    init(palette: [String], escCopies: Bool) {
        super.init(frame: .zero)
        wantsLayer = true

        let bottomBorder = NSView()
        bottomBorder.wantsLayer = true
        bottomBorder.translatesAutoresizingMaskIntoConstraints = false
        hairlines.append(bottomBorder)
        addSubview(bottomBorder)

        let stack = NSStackView()
        stack.orientation = .horizontal
        stack.spacing = 2
        stack.alignment = .centerY
        stack.translatesAutoresizingMaskIntoConstraints = false
        addSubview(stack)
        NSLayoutConstraint.activate([
            stack.centerXAnchor.constraint(equalTo: centerXAnchor),
            stack.centerYAnchor.constraint(equalTo: centerYAnchor),
            bottomBorder.leadingAnchor.constraint(equalTo: leadingAnchor),
            bottomBorder.trailingAnchor.constraint(equalTo: trailingAnchor),
            bottomBorder.bottomAnchor.constraint(equalTo: bottomAnchor),
            bottomBorder.heightAnchor.constraint(equalToConstant: 1),
        ])

        // Tool keys sit on the home-reach row: Q W E R.
        let tools: [(EditorTool, String, String, String)] = [
            (.arrow, "arrow.up.left", "Q", "Arrow"),
            (.rectangle, "rectangle", "W", "Rectangle"),
            (.counter, "1.circle", "E", "Counter"),
            (.text, "textformat", "R", "Text — ⏎ confirms, ⇧⏎ new line"),
        ]
        for (tool, symbol, hint, tip) in tools {
            let button = ToolbarButton(symbolName: symbol, hint: hint) { [weak self] in
                self?.onTool?(tool)
            }
            button.toolTip = tip
            toolButtons[tool] = button
            stack.addArrangedSubview(button)
        }

        stack.addArrangedSubview(separator())

        for (index, hex) in palette.prefix(7).enumerated() {
            let swatch = SwatchButton(color: NSColor(hex: hex) ?? .systemRed, hint: "\(index + 1)") { [weak self] in
                self?.onColor?(index)
            }
            swatches.append(swatch)
            stack.addArrangedSubview(swatch)
        }

        stack.addArrangedSubview(separator())

        let undo = ToolbarButton(symbolName: "arrow.uturn.backward", hint: "⌘Z") { [weak self] in
            self?.onUndo?()
        }
        undo.toolTip = "Undo"
        stack.addArrangedSubview(undo)

        let save = ToolbarButton(symbolName: "square.and.arrow.down", hint: "⌘S") { [weak self] in
            self?.onSave?()
        }
        save.toolTip = "Save as PNG…"
        stack.addArrangedSubview(save)

        // Esc's behavior is visible, not just documented.
        let done = ToolbarButton(
            symbolName: escCopies ? "doc.on.clipboard" : "xmark.circle",
            title: escCopies ? "Copy & Close" : "Close",
            hint: "esc"
        ) { [weak self] in
            self?.onDone?()
        }
        done.toolTip = escCopies
            ? "Esc copies the image to the clipboard and closes the editor"
            : "Esc closes the editor without copying"
        stack.addArrangedSubview(done)

        applyColors()
    }

    required init?(coder: NSCoder) { fatalError() }

    override func viewDidChangeEffectiveAppearance() {
        super.viewDidChangeEffectiveAppearance()
        applyColors()
    }

    private func applyColors() {
        effectiveAppearance.performAsCurrentDrawingAppearance {
            layer?.backgroundColor = EditorStyle.background.cgColor
            for hairline in hairlines {
                hairline.layer?.backgroundColor = EditorStyle.border.cgColor
            }
        }
    }

    func select(tool: EditorTool) {
        for (t, button) in toolButtons { button.isActive = (t == tool) }
    }

    func select(color index: Int) {
        for (i, swatch) in swatches.enumerated() { swatch.isActive = (i == index) }
    }

    private func separator() -> NSView {
        let view = NSView()
        view.wantsLayer = true
        view.translatesAutoresizingMaskIntoConstraints = false
        hairlines.append(view)
        NSLayoutConstraint.activate([
            view.widthAnchor.constraint(equalToConstant: 1),
            view.heightAnchor.constraint(equalToConstant: 20),
        ])
        // Breathing room around the hairline.
        let wrapper = NSView()
        wrapper.translatesAutoresizingMaskIntoConstraints = false
        wrapper.addSubview(view)
        NSLayoutConstraint.activate([
            wrapper.widthAnchor.constraint(equalToConstant: 13),
            wrapper.heightAnchor.constraint(equalToConstant: EditorStyle.height),
            view.centerXAnchor.constraint(equalTo: wrapper.centerXAnchor),
            view.centerYAnchor.constraint(equalTo: wrapper.centerYAnchor),
        ])
        return wrapper
    }
}

// MARK: - Buttons

final class ToolbarButton: NSControl {
    var isActive = false {
        didSet { updateAppearance() }
    }
    private let iconView = NSImageView()
    private var titleLabel: NSTextField?
    private let hintLabel = NSTextField(labelWithString: "")
    private let onClick: () -> Void

    override var mouseDownCanMoveWindow: Bool { false }

    init(symbolName: String, title: String? = nil, hint: String, onClick: @escaping () -> Void) {
        self.onClick = onClick
        super.init(frame: .zero)
        wantsLayer = true
        layer?.cornerRadius = 5
        translatesAutoresizingMaskIntoConstraints = false

        iconView.image = NSImage(systemSymbolName: symbolName, accessibilityDescription: title ?? hint)?
            .withSymbolConfiguration(.init(pointSize: 13, weight: .semibold))
        iconView.contentTintColor = .labelColor
        iconView.translatesAutoresizingMaskIntoConstraints = false

        hintLabel.stringValue = hint
        hintLabel.font = NSFont.monospacedSystemFont(ofSize: 10, weight: .semibold)
        hintLabel.textColor = .secondaryLabelColor
        hintLabel.translatesAutoresizingMaskIntoConstraints = false

        addSubview(iconView)
        addSubview(hintLabel)

        var hintLeading = iconView.trailingAnchor
        if let title {
            let label = NSTextField(labelWithString: title)
            label.font = NSFont.systemFont(ofSize: 11.5, weight: .semibold)
            label.textColor = .secondaryLabelColor
            label.translatesAutoresizingMaskIntoConstraints = false
            addSubview(label)
            titleLabel = label
            NSLayoutConstraint.activate([
                label.leadingAnchor.constraint(equalTo: iconView.trailingAnchor, constant: 5),
                label.centerYAnchor.constraint(equalTo: centerYAnchor),
            ])
            hintLeading = label.trailingAnchor
        }

        NSLayoutConstraint.activate([
            heightAnchor.constraint(equalToConstant: 28),
            iconView.leadingAnchor.constraint(equalTo: leadingAnchor, constant: 8),
            iconView.centerYAnchor.constraint(equalTo: centerYAnchor),
            hintLabel.leadingAnchor.constraint(equalTo: hintLeading, constant: 5),
            hintLabel.trailingAnchor.constraint(equalTo: trailingAnchor, constant: -8),
            hintLabel.centerYAnchor.constraint(equalTo: centerYAnchor),
        ])
        updateAppearance()
    }

    required init?(coder: NSCoder) { fatalError() }

    override func mouseDown(with event: NSEvent) {
        onClick()
    }

    override func viewDidChangeEffectiveAppearance() {
        super.viewDidChangeEffectiveAppearance()
        updateAppearance()
    }

    private func updateAppearance() {
        effectiveAppearance.performAsCurrentDrawingAppearance {
            if isActive {
                layer?.backgroundColor = EditorStyle.accentFill.cgColor
                layer?.borderColor = EditorStyle.accent.withAlphaComponent(0.55).cgColor
                layer?.borderWidth = 1
                iconView.contentTintColor = EditorStyle.accent
                titleLabel?.textColor = EditorStyle.accent
                hintLabel.textColor = EditorStyle.accent.withAlphaComponent(0.8)
            } else {
                layer?.backgroundColor = NSColor.clear.cgColor
                layer?.borderWidth = 0
                iconView.contentTintColor = .labelColor
                titleLabel?.textColor = .labelColor
                hintLabel.textColor = .secondaryLabelColor
            }
        }
    }
}

final class SwatchButton: NSControl {
    var isActive = false {
        didSet { updateAppearance() }
    }
    private let dot = NSView()
    private let hintLabel: NSTextField
    private let onClick: () -> Void

    override var mouseDownCanMoveWindow: Bool { false }

    init(color: NSColor, hint: String, onClick: @escaping () -> Void) {
        self.onClick = onClick
        hintLabel = NSTextField(labelWithString: hint)
        super.init(frame: .zero)
        translatesAutoresizingMaskIntoConstraints = false

        dot.wantsLayer = true
        dot.layer?.backgroundColor = color.cgColor
        dot.layer?.cornerRadius = 7
        dot.translatesAutoresizingMaskIntoConstraints = false

        hintLabel.font = NSFont.monospacedSystemFont(ofSize: 10, weight: .semibold)
        hintLabel.textColor = .secondaryLabelColor
        hintLabel.translatesAutoresizingMaskIntoConstraints = false

        addSubview(dot)
        addSubview(hintLabel)
        NSLayoutConstraint.activate([
            heightAnchor.constraint(equalToConstant: 28),
            dot.widthAnchor.constraint(equalToConstant: 14),
            dot.heightAnchor.constraint(equalToConstant: 14),
            dot.leadingAnchor.constraint(equalTo: leadingAnchor, constant: 5),
            dot.centerYAnchor.constraint(equalTo: centerYAnchor),
            hintLabel.leadingAnchor.constraint(equalTo: dot.trailingAnchor, constant: 3),
            hintLabel.trailingAnchor.constraint(equalTo: trailingAnchor, constant: -5),
            hintLabel.centerYAnchor.constraint(equalTo: centerYAnchor),
        ])
        updateAppearance()
    }

    required init?(coder: NSCoder) { fatalError() }

    override func mouseDown(with event: NSEvent) {
        onClick()
    }

    override func viewDidChangeEffectiveAppearance() {
        super.viewDidChangeEffectiveAppearance()
        updateAppearance()
    }

    private func updateAppearance() {
        effectiveAppearance.performAsCurrentDrawingAppearance {
            dot.layer?.borderColor = EditorStyle.swatchRing.cgColor
            dot.layer?.borderWidth = isActive ? 2 : 0
            hintLabel.textColor = isActive ? .labelColor : .secondaryLabelColor
        }
    }
}
