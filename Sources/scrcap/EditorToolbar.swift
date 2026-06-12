// EditorToolbar — flat full-width cell bar docked under the canvas, styled
// like kubre.in's nav: hairline-separated cells, mono labels, the active tool
// is a solid yellow block with black content, and the primary exit is an
// inverted carbon block flush right. No pills, no nesting, no shadows.

import AppKit
import ScrcapCore

final class EditorToolbar: NSView {
    var onTool: ((EditorTool) -> Void)?
    var onColor: ((Int) -> Void)?
    var onUndo: (() -> Void)?
    var onSave: (() -> Void)?
    var onDone: (() -> Void)?

    private var toolCells: [EditorTool: ToolbarCell] = [:]
    private var swatchCells: [SwatchCell] = []
    private var hairlines: [NSView] = []
    private let topRule = NSView()
    private let stack = NSStackView()

    // Gaps between cells are the window's drag handle.
    override var mouseDownCanMoveWindow: Bool { true }

    /// The window must be at least this wide or cells clip.
    var minimumWidth: CGFloat {
        stack.fittingSize.width + 2
    }

    init(palette: [String], escCopies: Bool, textEnterBehavior: TextEnterBehavior) {
        super.init(frame: .zero)
        wantsLayer = true

        topRule.wantsLayer = true
        topRule.translatesAutoresizingMaskIntoConstraints = false
        addSubview(topRule)

        stack.orientation = .horizontal
        stack.spacing = 0
        stack.alignment = .centerY
        stack.translatesAutoresizingMaskIntoConstraints = false
        addSubview(stack)
        NSLayoutConstraint.activate([
            topRule.leadingAnchor.constraint(equalTo: leadingAnchor),
            topRule.trailingAnchor.constraint(equalTo: trailingAnchor),
            topRule.topAnchor.constraint(equalTo: topAnchor),
            topRule.heightAnchor.constraint(equalToConstant: 1),
            stack.leadingAnchor.constraint(equalTo: leadingAnchor),
            stack.trailingAnchor.constraint(equalTo: trailingAnchor),
            stack.topAnchor.constraint(equalTo: topAnchor, constant: 1),
            stack.bottomAnchor.constraint(equalTo: bottomAnchor),
        ])

        // Tool keys sit on the home-reach row: Q W E R T.
        let tools: [(EditorTool, String, String, String)] = [
            (.arrow, "arrow.up.left", "Q", "Arrow"),
            (.rectangle, "rectangle", "W", "Rectangle"),
            (.counter, "1.circle", "E", "Counter"),
            (.text, "textformat", "R", Self.textToolTip(for: textEnterBehavior)),
            (.crop, "crop", "T", Self.cropToolTip()),
        ]
        for (tool, symbol, key, tip) in tools {
            let cell = ToolbarCell(symbolName: symbol, key: key) { [weak self] in
                self?.onTool?(tool)
            }
            cell.toolTip = tip
            toolCells[tool] = cell
            stack.addArrangedSubview(cell)
            stack.addArrangedSubview(hairline())
        }

        stack.addArrangedSubview(gap(4))

        for (index, hex) in palette.prefix(7).enumerated() {
            let cell = SwatchCell(color: NSColor(hex: hex) ?? .systemRed, key: "\(index + 1)") { [weak self] in
                self?.onColor?(index)
            }
            swatchCells.append(cell)
            stack.addArrangedSubview(cell)
        }

        // Flexible gap: tools/palette left, actions flush right.
        let spacer = NSView()
        spacer.translatesAutoresizingMaskIntoConstraints = false
        spacer.setContentHuggingPriority(.init(1), for: .horizontal)
        spacer.widthAnchor.constraint(greaterThanOrEqualToConstant: 8).isActive = true
        stack.addArrangedSubview(spacer)

        let undo = ToolbarCell(symbolName: "arrow.uturn.backward", key: "⌘Z") { [weak self] in
            self?.onUndo?()
        }
        undo.toolTip = "Undo"
        stack.addArrangedSubview(undo)
        stack.addArrangedSubview(hairline())

        let save = ToolbarCell(symbolName: "square.and.arrow.down", key: "⌘S") { [weak self] in
            self?.onSave?()
        }
        save.toolTip = "Save as PNG…"
        stack.addArrangedSubview(save)
        stack.addArrangedSubview(hairline())

        // Esc's behavior is visible, not just documented — the one inverted
        // block in the strip, flush right like the site's "Resume →" link.
        let done = PrimaryCell(
            title: escCopies ? "COPY & CLOSE" : "CLOSE",
            key: "ESC"
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

    private static func textToolTip(for behavior: TextEnterBehavior) -> String {
        switch behavior {
        case .newline:
            return "Text - Return new line, Shift-Return confirms"
        case .commit:
            return "Text - Return confirms, Shift-Return new line"
        }
    }

    private static func cropToolTip() -> String { "Crop" }

    override func viewDidChangeEffectiveAppearance() {
        super.viewDidChangeEffectiveAppearance()
        applyColors()
    }

    private func applyColors() {
        effectiveAppearance.performAsCurrentDrawingAppearance {
            layer?.backgroundColor = Theme.chrome.cgColor
            topRule.layer?.backgroundColor = Theme.rule.cgColor
            for hairline in hairlines {
                hairline.layer?.backgroundColor = Theme.hairline.cgColor
            }
        }
    }

    func select(tool: EditorTool) {
        for (t, cell) in toolCells { cell.isActive = (t == tool) }
    }

    func select(color index: Int) {
        for (i, cell) in swatchCells.enumerated() { cell.isActive = (i == index) }
    }

    private func hairline() -> NSView {
        let view = NSView()
        view.wantsLayer = true
        view.translatesAutoresizingMaskIntoConstraints = false
        hairlines.append(view)
        NSLayoutConstraint.activate([
            view.widthAnchor.constraint(equalToConstant: 1),
            view.heightAnchor.constraint(equalToConstant: Theme.toolbarHeight - 1),
        ])
        return view
    }

    private func gap(_ width: CGFloat) -> NSView {
        let view = NSView()
        view.translatesAutoresizingMaskIntoConstraints = false
        view.widthAnchor.constraint(equalToConstant: width).isActive = true
        return view
    }
}

// MARK: - Cells

/// Flat full-height cell: SF icon + mono key, inline. Active = yellow block
/// with black content (the site's selected-nav treatment).
final class ToolbarCell: NSControl {
    var isActive = false {
        didSet { updateAppearance() }
    }
    private var isHovered = false {
        didSet { updateAppearance() }
    }
    private let iconView = NSImageView()
    private let keyLabel: NSTextField

    private let onClick: () -> Void

    override var mouseDownCanMoveWindow: Bool { false }

    init(symbolName: String, key: String, onClick: @escaping () -> Void) {
        self.onClick = onClick
        keyLabel = NSTextField(labelWithString: key)
        super.init(frame: .zero)
        wantsLayer = true
        translatesAutoresizingMaskIntoConstraints = false

        iconView.image = NSImage(systemSymbolName: symbolName, accessibilityDescription: key)?
            .withSymbolConfiguration(Theme.iconConfiguration)
        iconView.imageScaling = .scaleProportionallyDown
        iconView.translatesAutoresizingMaskIntoConstraints = false

        keyLabel.font = Theme.cellKeyFont
        keyLabel.translatesAutoresizingMaskIntoConstraints = false

        addSubview(iconView)
        addSubview(keyLabel)
        NSLayoutConstraint.activate([
            heightAnchor.constraint(equalToConstant: Theme.toolbarHeight - 1),
            iconView.widthAnchor.constraint(equalToConstant: 16),
            iconView.heightAnchor.constraint(equalToConstant: 16),
            iconView.leadingAnchor.constraint(equalTo: leadingAnchor, constant: 8),
            iconView.centerYAnchor.constraint(equalTo: centerYAnchor),
            keyLabel.leadingAnchor.constraint(equalTo: iconView.trailingAnchor, constant: 4),
            keyLabel.centerYAnchor.constraint(equalTo: centerYAnchor),
            keyLabel.trailingAnchor.constraint(equalTo: trailingAnchor, constant: -8),
        ])
        updateAppearance()
    }

    required init?(coder: NSCoder) { fatalError() }

    override func updateTrackingAreas() {
        super.updateTrackingAreas()
        trackingAreas.forEach(removeTrackingArea)
        addTrackingArea(NSTrackingArea(
            rect: .zero,
            options: [.mouseEnteredAndExited, .activeInActiveApp, .inVisibleRect],
            owner: self
        ))
    }

    override func mouseEntered(with event: NSEvent) { isHovered = true }
    override func mouseExited(with event: NSEvent) { isHovered = false }

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
                layer?.backgroundColor = Theme.yellow.cgColor
                iconView.contentTintColor = Theme.onYellow
                keyLabel.textColor = Theme.onYellow
            } else {
                layer?.backgroundColor = isHovered ? Theme.hoverWash.cgColor : NSColor.clear.cgColor
                iconView.contentTintColor = Theme.ink
                keyLabel.textColor = Theme.inkDim
            }
        }
    }
}

/// Square color chip + number. Active = yellow block, black number.
final class SwatchCell: NSControl {
    var isActive = false {
        didSet { updateAppearance() }
    }
    private var isHovered = false {
        didSet { updateAppearance() }
    }
    private let chip = NSView()
    private let keyLabel: NSTextField
    private let onClick: () -> Void

    override var mouseDownCanMoveWindow: Bool { false }

    init(color: NSColor, key: String, onClick: @escaping () -> Void) {
        self.onClick = onClick
        keyLabel = NSTextField(labelWithString: key)
        super.init(frame: .zero)
        wantsLayer = true
        translatesAutoresizingMaskIntoConstraints = false

        chip.wantsLayer = true
        chip.layer?.backgroundColor = color.cgColor
        chip.layer?.borderWidth = 1
        chip.translatesAutoresizingMaskIntoConstraints = false

        keyLabel.font = Theme.cellKeyFont
        keyLabel.translatesAutoresizingMaskIntoConstraints = false

        addSubview(chip)
        addSubview(keyLabel)
        NSLayoutConstraint.activate([
            heightAnchor.constraint(equalToConstant: Theme.toolbarHeight - 1),
            chip.widthAnchor.constraint(equalToConstant: 12),
            chip.heightAnchor.constraint(equalToConstant: 12),
            chip.leadingAnchor.constraint(equalTo: leadingAnchor, constant: 7),
            chip.centerYAnchor.constraint(equalTo: centerYAnchor),
            keyLabel.leadingAnchor.constraint(equalTo: chip.trailingAnchor, constant: 4),
            keyLabel.centerYAnchor.constraint(equalTo: centerYAnchor),
            keyLabel.trailingAnchor.constraint(equalTo: trailingAnchor, constant: -7),
        ])
        updateAppearance()
    }

    required init?(coder: NSCoder) { fatalError() }

    override func updateTrackingAreas() {
        super.updateTrackingAreas()
        trackingAreas.forEach(removeTrackingArea)
        addTrackingArea(NSTrackingArea(
            rect: .zero,
            options: [.mouseEnteredAndExited, .activeInActiveApp, .inVisibleRect],
            owner: self
        ))
    }

    override func mouseEntered(with event: NSEvent) { isHovered = true }
    override func mouseExited(with event: NSEvent) { isHovered = false }

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
                layer?.backgroundColor = Theme.yellow.cgColor
                chip.layer?.borderColor = Theme.onYellow.cgColor
                keyLabel.textColor = Theme.onYellow
                keyLabel.font = NSFont.monospacedSystemFont(ofSize: 10, weight: .bold)
            } else {
                layer?.backgroundColor = isHovered ? Theme.hoverWash.cgColor : NSColor.clear.cgColor
                chip.layer?.borderColor = Theme.ink.withAlphaComponent(0.4).cgColor
                keyLabel.textColor = Theme.inkDim
                keyLabel.font = Theme.cellKeyFont
            }
        }
    }
}

/// The inverted block: ink background, chrome-colored text. Hovering flips it
/// to yellow/black, exactly like the site's links.
final class PrimaryCell: NSControl {
    private var isHovered = false {
        didSet { updateAppearance() }
    }
    private let titleLabel: NSTextField
    private let keyLabel: NSTextField
    private let onClick: () -> Void

    override var mouseDownCanMoveWindow: Bool { false }

    init(title: String, key: String, onClick: @escaping () -> Void) {
        self.onClick = onClick
        titleLabel = NSTextField(labelWithString: title)
        keyLabel = NSTextField(labelWithString: key)
        super.init(frame: .zero)
        wantsLayer = true
        translatesAutoresizingMaskIntoConstraints = false

        titleLabel.font = NSFont.monospacedSystemFont(ofSize: 11.5, weight: .semibold)
        titleLabel.translatesAutoresizingMaskIntoConstraints = false
        keyLabel.font = Theme.cellKeyFont
        keyLabel.translatesAutoresizingMaskIntoConstraints = false

        addSubview(titleLabel)
        addSubview(keyLabel)
        NSLayoutConstraint.activate([
            heightAnchor.constraint(equalToConstant: Theme.toolbarHeight - 1),
            titleLabel.leadingAnchor.constraint(equalTo: leadingAnchor, constant: 12),
            titleLabel.centerYAnchor.constraint(equalTo: centerYAnchor),
            keyLabel.leadingAnchor.constraint(equalTo: titleLabel.trailingAnchor, constant: 6),
            keyLabel.centerYAnchor.constraint(equalTo: centerYAnchor),
            keyLabel.trailingAnchor.constraint(equalTo: trailingAnchor, constant: -12),
        ])
        updateAppearance()
    }

    required init?(coder: NSCoder) { fatalError() }

    override func updateTrackingAreas() {
        super.updateTrackingAreas()
        trackingAreas.forEach(removeTrackingArea)
        addTrackingArea(NSTrackingArea(
            rect: .zero,
            options: [.mouseEnteredAndExited, .activeInActiveApp, .inVisibleRect],
            owner: self
        ))
    }

    override func mouseEntered(with event: NSEvent) { isHovered = true }
    override func mouseExited(with event: NSEvent) { isHovered = false }

    override func mouseDown(with event: NSEvent) {
        onClick()
    }

    override func viewDidChangeEffectiveAppearance() {
        super.viewDidChangeEffectiveAppearance()
        updateAppearance()
    }

    private func updateAppearance() {
        effectiveAppearance.performAsCurrentDrawingAppearance {
            if isHovered {
                layer?.backgroundColor = Theme.yellow.cgColor
                titleLabel.textColor = Theme.onYellow
                keyLabel.textColor = Theme.onYellow.withAlphaComponent(0.65)
            } else {
                layer?.backgroundColor = Theme.inverse.cgColor
                titleLabel.textColor = Theme.onInverse
                keyLabel.textColor = Theme.onInverse.withAlphaComponent(0.65)
            }
        }
    }
}
