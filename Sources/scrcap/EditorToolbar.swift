// EditorToolbar — compact full-width cell bar docked under the canvas.
// Tools, swatches, and the exit action share one red active language; shortcut
// labels stay monospaced so the editor remains fast to scan.

import AppKit
import ScrcapCore

final class EditorToolbar: NSView {
    var onTool: ((EditorTool) -> Void)?
    var onColor: ((Int) -> Void)?
    var onSize: ((ShapeSize) -> Void)?

    private var toolCells: [EditorTool: ToolbarCell] = [:]
    private var sizeCells: [ShapeSize: ToolbarCell] = [:]
    private var swatchCells: [SwatchCell] = []
    private let topRule = NSView()
    private let stack = NSStackView()

    // Gaps between cells are the window's drag handle.
    override var mouseDownCanMoveWindow: Bool { true }

    /// The window must be at least this wide to show the spread groups without
    /// crowding; the floor also keeps the default window size from shrinking.
    var minimumWidth: CGFloat {
        max(stack.fittingSize.width, 600)
    }

    init(palette: [String], textEnterBehavior: TextEnterBehavior) {
        super.init(frame: .zero)
        wantsLayer = true

        topRule.wantsLayer = true
        topRule.translatesAutoresizingMaskIntoConstraints = false
        addSubview(topRule)

        // The three groups (tools · colors · sizes) spread across the full width.
        stack.orientation = .horizontal
        stack.distribution = .equalSpacing
        stack.spacing = 16
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

        // Tools — keys on the home-reach row: Q W E R T Y.
        let tools: [(EditorTool, String, String, String)] = [
            (.arrow, "arrow.up.left", "Q", "Arrow"),
            (.rectangle, "rectangle", "W", "Rectangle"),
            (.counter, "1.circle", "E", "Counter"),
            (.text, "textformat", "R", Self.textToolTip(for: textEnterBehavior)),
            (.pixelate, "square.grid.3x3.fill", "T", "Pixelate / redact a region"),
            (.crop, "crop", "Y", Self.cropToolTip()),
        ]
        let toolViews: [NSView] = tools.map { tool, symbol, key, tip in
            let cell = ToolbarCell(symbolName: symbol, key: key) { [weak self] in self?.onTool?(tool) }
            cell.toolTip = tip
            toolCells[tool] = cell
            return cell
        }

        let swatchViews: [NSView] = palette.prefix(AppSettings.paletteSlotCount).enumerated().map { index, hex in
            let cell = SwatchCell(color: NSColor(hex: hex) ?? .systemRed, key: "\(index + 1)") { [weak self] in
                self?.onColor?(index)
            }
            swatchCells.append(cell)
            return cell
        }

        // Sizes — a dot that grows S→M→L on the Z X C row.
        let sizes: [(ShapeSize, CGFloat, String, String)] = [
            (.small, 5, "Z", "Small"),
            (.medium, 9, "X", "Medium"),
            (.large, 13, "C", "Large"),
        ]
        let sizeViews: [NSView] = sizes.map { size, dotDiameter, key, tip in
            let cell = ToolbarCell(image: Self.dotImage(diameter: dotDiameter), key: key) { [weak self] in
                self?.onSize?(size)
            }
            cell.toolTip = "\(tip) size"
            sizeCells[size] = cell
            return cell
        }

        stack.addArrangedSubview(group(toolViews))
        stack.addArrangedSubview(group(swatchViews))
        stack.addArrangedSubview(group(sizeViews))

        applyColors()
    }

    /// A packed, borderless horizontal cluster of cells.
    private func group(_ cells: [NSView]) -> NSStackView {
        let g = NSStackView()
        g.orientation = .horizontal
        g.alignment = .centerY
        g.spacing = 0
        cells.forEach { g.addArrangedSubview($0) }
        return g
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
        }
    }

    func select(tool: EditorTool) {
        for (t, cell) in toolCells { cell.isActive = (t == tool) }
    }

    func select(color index: Int) {
        for (i, cell) in swatchCells.enumerated() { cell.isActive = (i == index) }
    }

    func select(size: ShapeSize) {
        for (s, cell) in sizeCells { cell.isActive = (s == size) }
    }

    /// A centered filled dot of the given diameter, as a template image so it
    /// tints with the cell's ink/accent like the SF symbols do.
    private static func dotImage(diameter: CGFloat) -> NSImage {
        let side: CGFloat = 15
        let image = NSImage(size: NSSize(width: side, height: side))
        image.lockFocus()
        NSColor.black.setFill()
        let rect = NSRect(x: (side - diameter) / 2, y: (side - diameter) / 2, width: diameter, height: diameter)
        NSBezierPath(ovalIn: rect).fill()
        image.unlockFocus()
        image.isTemplate = true
        return image
    }
}

// MARK: - Cells

/// Canonical custom-button press tracking: highlight while the mouse is held
/// inside, fire on mouse-up inside. Gives a distinct pressed state vs. hover.
private func trackButtonPress(in view: NSView, setPressed: @escaping (Bool) -> Void, fire: () -> Void) {
    setPressed(true)
    while let event = view.window?.nextEvent(matching: [.leftMouseUp, .leftMouseDragged]) {
        let inside = view.bounds.contains(view.convert(event.locationInWindow, from: nil))
        if event.type == .leftMouseUp {
            setPressed(false)
            if inside { fire() }
            return
        }
        setPressed(inside)
    }
    setPressed(false)
}

/// Flat full-height cell: SF icon + mono key, inline. Active = red block.
final class ToolbarCell: NSControl {
    var isActive = false {
        didSet { updateAppearance() }
    }
    private var isHovered = false {
        didSet { updateAppearance() }
    }
    private var isPressed = false {
        didSet { updateAppearance() }
    }
    private let iconView = NSImageView()
    private let keyLabel: NSTextField

    private let onClick: () -> Void

    override var mouseDownCanMoveWindow: Bool { false }

    convenience init(symbolName: String, key: String, onClick: @escaping () -> Void) {
        let image = NSImage(systemSymbolName: symbolName, accessibilityDescription: key)?
            .withSymbolConfiguration(Theme.iconConfiguration)
        self.init(image: image, key: key, onClick: onClick)
    }

    init(image: NSImage?, key: String, onClick: @escaping () -> Void) {
        self.onClick = onClick
        keyLabel = NSTextField(labelWithString: key)
        super.init(frame: .zero)
        wantsLayer = true
        translatesAutoresizingMaskIntoConstraints = false

        iconView.image = image
        iconView.imageScaling = .scaleProportionallyDown
        iconView.translatesAutoresizingMaskIntoConstraints = false

        keyLabel.font = Theme.cellKeyFont
        keyLabel.translatesAutoresizingMaskIntoConstraints = false

        addSubview(iconView)
        addSubview(keyLabel)
        NSLayoutConstraint.activate([
            heightAnchor.constraint(equalToConstant: Theme.toolbarHeight - 1),
            iconView.widthAnchor.constraint(equalToConstant: 15),
            iconView.heightAnchor.constraint(equalToConstant: 15),
            iconView.leadingAnchor.constraint(equalTo: leadingAnchor, constant: 7),
            iconView.centerYAnchor.constraint(equalTo: centerYAnchor),
            keyLabel.leadingAnchor.constraint(equalTo: iconView.trailingAnchor, constant: 4),
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
        trackButtonPress(in: self, setPressed: { [weak self] in self?.isPressed = $0 }, fire: onClick)
    }

    override func viewDidChangeEffectiveAppearance() {
        super.viewDidChangeEffectiveAppearance()
        updateAppearance()
    }

    private func updateAppearance() {
        effectiveAppearance.performAsCurrentDrawingAppearance {
            if isActive {
                // Translucent red, no border — uniform with the color swatches.
                layer?.backgroundColor = (isPressed ? Theme.pressedWash : Theme.activeWash).cgColor
                iconView.contentTintColor = Theme.accent
                keyLabel.textColor = Theme.accent
            } else {
                let wash = isPressed ? Theme.pressedWash : (isHovered ? Theme.hoverWash : NSColor.clear)
                layer?.backgroundColor = wash.cgColor
                iconView.contentTintColor = Theme.ink
                keyLabel.textColor = Theme.inkDim
            }
        }
    }
}

/// Square color chip + number. Active = soft red outline so the chip stays visible.
final class SwatchCell: NSControl {
    var isActive = false {
        didSet { updateAppearance() }
    }
    private var isHovered = false {
        didSet { updateAppearance() }
    }
    private var isPressed = false {
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
            chip.widthAnchor.constraint(equalToConstant: 11),
            chip.heightAnchor.constraint(equalToConstant: 11),
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
        trackButtonPress(in: self, setPressed: { [weak self] in self?.isPressed = $0 }, fire: onClick)
    }

    override func viewDidChangeEffectiveAppearance() {
        super.viewDidChangeEffectiveAppearance()
        updateAppearance()
    }

    private func updateAppearance() {
        effectiveAppearance.performAsCurrentDrawingAppearance {
            if isActive {
                // Translucent red, no border — uniform with tool/size cells.
                layer?.backgroundColor = (isPressed ? Theme.pressedWash : Theme.activeWash).cgColor
                chip.layer?.borderColor = Theme.accent.cgColor
                keyLabel.textColor = Theme.accent
                keyLabel.font = NSFont.monospacedSystemFont(ofSize: 10, weight: .bold)
            } else {
                let wash = isPressed ? Theme.pressedWash : (isHovered ? Theme.hoverWash : NSColor.clear)
                layer?.backgroundColor = wash.cgColor
                chip.layer?.borderColor = Theme.ink.withAlphaComponent(0.4).cgColor
                keyLabel.textColor = Theme.inkDim
                keyLabel.font = Theme.cellKeyFont
            }
        }
    }
}

