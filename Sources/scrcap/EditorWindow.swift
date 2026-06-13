// EditorWindow — borderless window that appears already-loaded: screenshot at
// 1:1, toolbar docked on top. Draw-stays-armed: shapes commit on mouse-up, the
// tool stays armed, Esc means exactly one thing — done (copy + close).

import AppKit
import ScrcapCore

enum EditorTool: CaseIterable {
    case arrow, rectangle, counter, text, crop
}

final class EditorWindowController: NSObject, NSWindowDelegate {
    private let window: EditorWindow
    private let canvas: CanvasView
    private let toolbar: EditorToolbar
    private let settingsStore: SettingsStore
    private var bitmap: CGImage
    private let scale: CGFloat
    private var stack = AnnotationStack()
    private var onClose: (() -> Void)?
    private var pendingZoomWindowPoint: NSPoint?
    private var zoomScale: CGFloat = 1 {
        didSet {
            zoomScale = min(max(zoomScale, 0.25), 4)
            guard zoomScale != oldValue else { return }
            window.updateZoomScale(zoomScale, around: pendingZoomWindowPoint)
            pendingZoomWindowPoint = nil
        }
    }

    private var tool: EditorTool = .arrow {
        didSet {
            if oldValue == .text { canvas.commitTextEditing() }
            toolbar.select(tool: tool)
        }
    }
    private var colorIndex = 0 {
        didSet { toolbar.select(color: colorIndex) }
    }

    /// Strong self-retain while the window is open (no window controller chain).
    private static var open: Set<EditorWindowController> = []

    init(capture: CaptureResult, settingsStore: SettingsStore, onClose: (() -> Void)? = nil) {
        self.bitmap = capture.image
        self.scale = capture.scale
        self.settingsStore = settingsStore
        self.onClose = onClose

        let pointSize = NSSize(
            width: CGFloat(bitmap.width) / scale,
            height: CGFloat(bitmap.height) / scale
        )
        canvas = CanvasView(bitmap: bitmap, pointSize: pointSize)
        toolbar = EditorToolbar(
            palette: settingsStore.settings.paletteHex,
            escCopies: settingsStore.settings.escBehavior == .copyAndClose,
            textEnterBehavior: settingsStore.settings.textEnterBehavior
        )
        window = EditorWindow(canvas: canvas, toolbar: toolbar, imagePointSize: pointSize)

        super.init()

        window.delegate = self
        window.keyHandler = { [weak self] event in self?.handleKey(event) ?? false }
        canvas.dataSource = self
        window.onZoomIn = { [weak self] in self?.zoomIn() }
        window.onZoomOut = { [weak self] in self?.zoomOut() }
        window.onZoomReset = { [weak self] in self?.resetZoom() }
        window.gestureHandler = { [weak self] factor, point in
            self?.zoomCanvas(by: factor, around: point)
        }
        toolbar.onTool = { [weak self] in self?.tool = $0 }
        toolbar.onColor = { [weak self] in self?.colorIndex = $0 }
        toolbar.onUndo = { [weak self] in self?.undo() }
        toolbar.onSave = { [weak self] in self?.saveAs() }
        toolbar.onDone = { [weak self] in self?.finish() }
        toolbar.select(tool: tool)
        toolbar.select(color: colorIndex)
    }

    func show() {
        EditorWindowController.open.insert(self)
        NSApp.setActivationPolicy(.regular)
        NSApp.activate(ignoringOtherApps: true)
        window.center()
        window.makeKeyAndOrderFront(nil)
    }

    // MARK: Key handling

    private func handleKey(_ event: NSEvent) -> Bool {
        let mods = event.modifierFlags.intersection(.deviceIndependentFlagsMask)
        let chars = event.charactersIgnoringModifiers?.lowercased() ?? ""

        if mods.contains(.command) {
            switch chars {
            case "z":
                if mods.contains(.shift) { redo() } else { undo() }
                return true
            case "c":
                copyToClipboard()
                return true
            case "s":
                saveAs()
                return true
            case "+", "=":
                zoomIn()
                return true
            case "-":
                zoomOut()
                return true
            case "0":
                resetZoom()
                return true
            case "w":
                close()
                return true
            default:
                return false
            }
        }

        if event.keyCode == 53 { // Esc
            // While typing, Esc exits text entry — never the window.
            // (Belt and braces: the text view normally swallows it first.)
            if canvas.isEditingText {
                canvas.commitTextEditing()
                return true
            }
            finish()
            return true
        }

        switch chars {
        case "q": tool = .arrow; return true
        case "w": tool = .rectangle; return true
        case "e": tool = .counter; return true
        case "r": tool = .text; return true
        case "t": tool = .crop; return true
        case "1", "2", "3", "4", "5", "6", "7":
            colorIndex = Int(chars)! - 1
            return true
        default:
            return false
        }
    }

    private func undo() {
        canvas.cancelTextEditing()
        stack.undo()
        canvas.needsDisplay = true
    }

    private func redo() {
        stack.redo()
        canvas.needsDisplay = true
    }

    private func zoomIn() {
        zoomScale = nextZoomScale(direction: 1)
    }

    private func zoomOut() {
        zoomScale = nextZoomScale(direction: -1)
    }

    private func resetZoom() {
        // zoomScale.didSet skips window.updateZoomScale when the value is already
        // 1, so crop or canvas-extend followed by "100%" would silently no-op.
        // Always push the update so the viewport re-centers on the current canvas.
        zoomScale = 1
        window.resetZoom()
    }

    private func nextZoomScale(direction: Int) -> CGFloat {
        let steps: [CGFloat] = [0.25, 0.33, 0.5, 0.67, 0.75, 1, 1.25, 1.5, 2, 3, 4]
        if direction > 0 {
            return steps.first { $0 > zoomScale + 0.001 } ?? steps.last!
        }
        return steps.reversed().first { $0 < zoomScale - 0.001 } ?? steps.first!
    }

    private func zoom(by factor: CGFloat, around windowPoint: NSPoint) {
        let next = min(max(zoomScale * factor, 0.25), 4)
        guard abs(next - zoomScale) > 0.001 else { return }
        pendingZoomWindowPoint = windowPoint
        zoomScale = next
    }

    /// The Esc / "Done" exit: copy per settings, then close.
    private func finish() {
        canvas.commitTextEditing()
        if settingsStore.settings.escBehavior == .copyAndClose {
            guard copyToClipboard() else { return }
        }
        close()
    }

    // MARK: Output

    private func flattened() -> CGImage {
        canvas.commitTextEditing()
        return Exporter.flatten(
            bitmap: bitmap,
            shapes: Array(stack.visible),
            palette: settingsStore.settings.paletteHex,
            strokeWidth: settingsStore.settings.strokeWidth,
            scale: scale,
            exportScale: settingsStore.settings.resolvedExportScale
        )
    }

    /// Pixels-per-point of flattened output, for DPI metadata.
    private var exportPointScale: CGFloat {
        min(CGFloat(settingsStore.settings.resolvedExportScale), max(scale, 1))
    }

    @discardableResult
    private func copyToClipboard() -> Bool {
        let copied = Exporter.copyToClipboard(flattened(), pointScale: exportPointScale)
        if !copied {
            presentError(ExportError.clipboardWriteFailed)
        }
        return copied
    }

    private func saveAs() {
        let settings = settingsStore.settings
        let panel = NSSavePanel()
        panel.allowedContentTypes = [.png]
        panel.canCreateDirectories = true
        panel.directoryURL = Exporter.defaultSaveFolder(settings: settings)
        panel.nameFieldStringValue = Exporter.filename(pattern: settings.filenamePattern)
        panel.beginSheetModal(for: window) { [weak self] response in
            guard let self, response == .OK, let url = panel.url else { return }
            if !Exporter.writePNG(self.flattened(), pointScale: self.exportPointScale, to: url) {
                self.presentError(ExportError.pngWriteFailed(url))
            }
        }
    }

    private func presentError(_ error: Error) {
        NSLog("scrcap: editor export failed: \(error.localizedDescription)")
        let alert = NSAlert()
        alert.messageText = "Export failed"
        alert.informativeText = error.localizedDescription
        alert.alertStyle = .warning
        alert.beginSheetModal(for: window)
    }

    private func close() {
        window.close()
    }

    func windowWillClose(_ notification: Notification) {
        let callback = onClose
        onClose = nil
        EditorWindowController.open.remove(self)
        restoreAccessoryActivationIfNoUserWindowsRemain()
        callback?()
    }

    private func restoreAccessoryActivationIfNoUserWindowsRemain() {
        DispatchQueue.main.async {
            let hasVisibleUserWindow = NSApp.windows.contains { window in
                window.isVisible && window.level == .normal && window.canBecomeKey
            }
            if !hasVisibleUserWindow {
                NSApp.setActivationPolicy(.accessory)
            }
        }
    }

    #if DEBUG
    var debugContentView: NSView? { window.contentView }

    /// Headless smoke test for the inline text editor (SCRCAP_SMOKE=text):
    /// exercises begin → type → newline → commit without a real keyboard.
    /// Returns the committed string, or nil if the pipeline broke.
    func debugExerciseTextTool() -> String? {
        tool = .text
        canvas.debugBeginTextEditing(at: NSPoint(x: 30, y: 30))
        guard let editor = canvas.debugTextEditor else { return nil }
        editor.insertText("hello", replacementRange: NSRange(location: 0, length: 0))
        editor.insertText("\n", replacementRange: NSRange(location: 5, length: 0)) // ⇧⏎ path
        editor.insertText("world", replacementRange: NSRange(location: 6, length: 0))
        canvas.commitTextEditing() // ⏎ path
        guard case .text(let string, _) = stack.visible.last?.kind else { return nil }
        _ = flattened() // render path, including the text shape
        return string
    }
    #endif
}

// MARK: - CanvasDataSource

protocol CanvasDataSource: AnyObject {
    var currentTool: EditorTool { get }
    var currentColorIndex: Int { get }
    var visibleShapes: [Shape] { get }
    var palette: [String] { get }
    var strokeWidth: CGFloat { get }
    var textSize: CGFloat { get }
    var textEnterBehavior: TextEnterBehavior { get }
    var autoExpandCanvas: Bool { get }
    func commit(shape: Shape)
    func commitCrop(_ rect: NSRect)
    func expandCanvasIfNeeded(toFit rect: NSRect) -> NSPoint
    func nextCounterNumber() -> Int
    func dragOutImage() -> (image: CGImage, pattern: String, pointScale: CGFloat)
}

extension EditorWindowController: CanvasDataSource {
    var currentTool: EditorTool { tool }
    var currentColorIndex: Int { colorIndex }
    var visibleShapes: [Shape] { Array(stack.visible) }
    var palette: [String] { settingsStore.settings.paletteHex }
    var strokeWidth: CGFloat { settingsStore.settings.strokeWidth }
    var textSize: CGFloat { settingsStore.settings.textSize }
    var textEnterBehavior: TextEnterBehavior { settingsStore.settings.textEnterBehavior }
    var autoExpandCanvas: Bool { settingsStore.settings.autoExpandCanvas }

    func commit(shape: Shape) {
        stack.append(shape)
        canvas.needsDisplay = true
    }

    func commitCrop(_ rect: NSRect) {
        canvas.commitTextEditing()

        let crop = pixelAlignedCrop(from: rect)
        guard crop.points.width >= 2, crop.points.height >= 2,
              let cropped = bitmap.cropping(to: crop.pixels)
        else { return }

        bitmap = cropped
        stack = shiftedStack(afterCroppingTo: crop.points)

        let pointSize = NSSize(
            width: CGFloat(cropped.width) / scale,
            height: CGFloat(cropped.height) / scale
        )
        canvas.replaceBitmap(cropped, pointSize: pointSize)
        window.updateImagePointSize(pointSize)
    }

    func expandCanvasIfNeeded(toFit rect: NSRect) -> NSPoint {
        guard settingsStore.settings.autoExpandCanvas else { return .zero }
        let expansion = CanvasExpansion(
            fitting: rect.standardized,
            in: NSRect(origin: .zero, size: canvas.bounds.size)
        )
        guard expansion.hasWork else { return .zero }

        guard let expanded = expandedBitmap(
            by: expansion,
            fill: NSColor(hex: settingsStore.settings.canvasExtensionBackgroundHex) ?? .white
        ) else { return .zero }

        bitmap = expanded.image
        if expanded.offset != .zero {
            stack = shiftedStack(by: expanded.offset)
        }
        canvas.replaceBitmap(
            expanded.image,
            pointSize: expanded.pointSize,
            clearTransientState: false,
            leadingExpansionOffset: expanded.offset
        )
        window.updateImagePointSize(expanded.pointSize, layoutCanvas: false)
        return expanded.offset
    }

    private struct CanvasExpansion {
        var left: CGFloat = 0
        var top: CGFloat = 0
        var right: CGFloat = 0
        var bottom: CGFloat = 0

        init(fitting rect: NSRect, in bounds: NSRect) {
            left = Self.growth(for: bounds.minX - rect.minX)
            top = Self.growth(for: bounds.minY - rect.minY)
            right = Self.growth(for: rect.maxX - bounds.maxX)
            bottom = Self.growth(for: rect.maxY - bounds.maxY)
        }

        private static func growth(for missing: CGFloat) -> CGFloat {
            missing > 0 ? ceil(missing) : 0
        }

        var hasWork: Bool {
            left > 0 || top > 0 || right > 0 || bottom > 0
        }
    }

    private func expandedBitmap(
        by expansion: CanvasExpansion,
        fill: NSColor
    ) -> (image: CGImage, pointSize: NSSize, offset: NSPoint)? {
        let leftPixels = Int((expansion.left * scale).rounded(.toNearestOrAwayFromZero))
        let topPixels = Int((expansion.top * scale).rounded(.toNearestOrAwayFromZero))
        let rightPixels = Int((expansion.right * scale).rounded(.toNearestOrAwayFromZero))
        let bottomPixels = Int((expansion.bottom * scale).rounded(.toNearestOrAwayFromZero))

        let newWidth = bitmap.width + leftPixels + rightPixels
        let newHeight = bitmap.height + topPixels + bottomPixels
        guard newWidth > bitmap.width || newHeight > bitmap.height,
              let ctx = CGContext(
                  data: nil,
                  width: newWidth,
                  height: newHeight,
                  bitsPerComponent: 8,
                  bytesPerRow: 0,
                  space: bitmap.colorSpace ?? CGColorSpace(name: CGColorSpace.sRGB)!,
                  bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue
              )
        else { return nil }

        ctx.setFillColor(fill.usingColorSpace(.sRGB)?.cgColor ?? NSColor.white.cgColor)
        ctx.fill(CGRect(x: 0, y: 0, width: newWidth, height: newHeight))
        ctx.draw(bitmap, in: CGRect(
            x: leftPixels,
            y: bottomPixels,
            width: bitmap.width,
            height: bitmap.height
        ))
        guard let image = ctx.makeImage() else { return nil }

        let pointSize = NSSize(width: CGFloat(newWidth) / scale, height: CGFloat(newHeight) / scale)
        let offset = NSPoint(x: CGFloat(leftPixels) / scale, y: CGFloat(topPixels) / scale)
        return (image, pointSize, offset)
    }

    private func shiftedStack(by offset: NSPoint) -> AnnotationStack {
        var shifted = AnnotationStack()
        for shape in stack.visible {
            shifted.append(Shape(
                kind: shape.kind,
                colorIndex: shape.colorIndex,
                start: CorePoint(x: shape.start.x + offset.x, y: shape.start.y + offset.y),
                end: CorePoint(x: shape.end.x + offset.x, y: shape.end.y + offset.y)
            ))
        }
        return shifted
    }

    private func pixelAlignedCrop(from rect: NSRect) -> (points: NSRect, pixels: CGRect) {
        let bounds = NSRect(origin: .zero, size: canvas.bounds.size)
        let clamped = rect.standardized.intersection(bounds)
        var pixels = CGRect(
            x: clamped.minX * scale,
            y: clamped.minY * scale,
            width: clamped.width * scale,
            height: clamped.height * scale
        ).integral
        pixels = pixels.intersection(CGRect(x: 0, y: 0, width: bitmap.width, height: bitmap.height))
        let points = NSRect(
            x: pixels.minX / scale,
            y: pixels.minY / scale,
            width: pixels.width / scale,
            height: pixels.height / scale
        )
        return (points, pixels)
    }

    private func shiftedStack(afterCroppingTo crop: NSRect) -> AnnotationStack {
        var shifted = AnnotationStack()
        for shape in stack.visible {
            guard shape.shouldSurviveCrop(crop, strokeWidth: settingsStore.settings.strokeWidth) else {
                continue
            }
            shifted.append(Shape(
                kind: shape.kind,
                colorIndex: shape.colorIndex,
                start: CorePoint(x: shape.start.x - crop.minX, y: shape.start.y - crop.minY),
                end: CorePoint(x: shape.end.x - crop.minX, y: shape.end.y - crop.minY)
            ))
        }
        return shifted
    }

    func nextCounterNumber() -> Int { stack.nextCounterNumber }

    func dragOutImage() -> (image: CGImage, pattern: String, pointScale: CGFloat) {
        (flattened(), settingsStore.settings.filenamePattern, exportPointScale)
    }

    func zoomCanvas(by factor: CGFloat, around windowPoint: NSPoint) {
        zoom(by: factor, around: windowPoint)
    }
}

private extension Shape {
    func offsetBy(x: CGFloat, y: CGFloat) -> Shape {
        Shape(
            kind: kind,
            colorIndex: colorIndex,
            start: CorePoint(x: start.x + x, y: start.y + y),
            end: CorePoint(x: end.x + x, y: end.y + y)
        )
    }

    func shouldSurviveCrop(_ crop: NSRect, strokeWidth: CGFloat) -> Bool {
        switch kind {
        case .counter:
            return crop.contains(NSPoint(x: end.x, y: end.y))
        case .text:
            return crop.contains(NSPoint(x: start.x, y: start.y))
        case .arrow, .rectangle:
            let minX = min(start.x, end.x)
            let minY = min(start.y, end.y)
            let maxX = max(start.x, end.x)
            let maxY = max(start.y, end.y)
            let pad = max(strokeWidth, 1)
            let bounds = NSRect(
                x: minX - pad,
                y: minY - pad,
                width: maxX - minX + pad * 2,
                height: maxY - minY + pad * 2
            )
            return bounds.intersects(crop)
        }
    }
}

// MARK: - Window

/// Editor chrome that re-resolves its dynamic colors when the system theme
/// flips (CGColor assignments don't auto-adapt the way semantic NSColors do).
/// Rounded compact chrome with a single balanced outline.
///
/// layer.borderWidth is NOT used: with cornerRadius+masksToBounds the outer
/// half of the stroke is clipped away, leaving nothing visible at corners.
/// Instead a CAShapeLayer sublayer draws a precisely-inset rounded-rect path.
final class EditorContainerView: NSView {
    private let borderLayer = CAShapeLayer()

    override init(frame: NSRect) {
        super.init(frame: frame)
        wantsLayer = true
        layer?.cornerRadius = 10
        layer?.masksToBounds = true
        borderLayer.fillColor = nil
        borderLayer.lineWidth = 1
        layer?.addSublayer(borderLayer)
        applyColors()
    }

    required init?(coder: NSCoder) { fatalError() }

    override func layout() {
        super.layout()
        // Rebuild the border path on every resize. Inset by 0.5pt so the full
        // stroke width sits inside the rounded clip region and stays visible.
        // Disable implicit animation so there's no lag on live-resize.
        CATransaction.begin()
        CATransaction.setDisableActions(true)
        let r = (layer?.cornerRadius ?? 10) - 0.5
        borderLayer.path = CGPath(
            roundedRect: bounds.insetBy(dx: 0.5, dy: 0.5),
            cornerWidth: r, cornerHeight: r,
            transform: nil
        )
        borderLayer.frame = bounds
        CATransaction.commit()
    }

    override func viewDidChangeEffectiveAppearance() {
        super.viewDidChangeEffectiveAppearance()
        applyColors()
    }

    private func applyColors() {
        effectiveAppearance.performAsCurrentDrawingAppearance {
            layer?.backgroundColor = Theme.chrome.cgColor
            borderLayer.strokeColor = Theme.rule.cgColor
        }
    }
}

/// Top strip: the brand mark left, image dimensions right, 1px rule below.
/// Doubles as the window's drag handle.
final class EditorHeaderView: NSView {
    let dimensionLabel = NSTextField(labelWithString: "")
    var onZoomIn: (() -> Void)?
    var onZoomOut: (() -> Void)?
    var onZoomReset: (() -> Void)?

    private let brandIcon = NSImageView(image: Theme.logoImage(size: 16, template: true) ?? NSImage())
    private let brandLabel = NSTextField(labelWithString: Theme.brandName)
    private let zoomOutButton = HeaderIconButton(symbolName: "minus.magnifyingglass")
    private let zoomResetButton = HeaderTextButton(title: "100%")
    private let zoomInButton = HeaderIconButton(symbolName: "plus.magnifyingglass")
    private let rule = NSView()

    override var mouseDownCanMoveWindow: Bool { true }

    override init(frame: NSRect) {
        super.init(frame: frame)
        wantsLayer = true

        brandIcon.imageScaling = .scaleProportionallyDown
        brandIcon.translatesAutoresizingMaskIntoConstraints = false
        addSubview(brandIcon)

        brandLabel.font = Theme.brandFont
        brandLabel.translatesAutoresizingMaskIntoConstraints = false
        addSubview(brandLabel)

        dimensionLabel.font = Theme.headerFont
        dimensionLabel.alignment = .right
        dimensionLabel.translatesAutoresizingMaskIntoConstraints = false
        addSubview(dimensionLabel)

        zoomOutButton.toolTip = "Zoom out"
        zoomOutButton.onClick = { [weak self] in self?.onZoomOut?() }
        zoomResetButton.toolTip = "Reset zoom"
        zoomResetButton.onClick = { [weak self] in self?.onZoomReset?() }
        zoomInButton.toolTip = "Zoom in"
        zoomInButton.onClick = { [weak self] in self?.onZoomIn?() }
        addSubview(zoomOutButton)
        addSubview(zoomResetButton)
        addSubview(zoomInButton)

        rule.wantsLayer = true
        rule.translatesAutoresizingMaskIntoConstraints = false
        addSubview(rule)

        NSLayoutConstraint.activate([
            brandIcon.leadingAnchor.constraint(equalTo: leadingAnchor, constant: 92),
            brandIcon.centerYAnchor.constraint(equalTo: centerYAnchor),
            brandIcon.widthAnchor.constraint(equalToConstant: 16),
            brandIcon.heightAnchor.constraint(equalToConstant: 16),
            brandLabel.leadingAnchor.constraint(equalTo: brandIcon.trailingAnchor, constant: 7),
            brandLabel.centerYAnchor.constraint(equalTo: centerYAnchor),
            dimensionLabel.trailingAnchor.constraint(equalTo: trailingAnchor, constant: -12),
            dimensionLabel.centerYAnchor.constraint(equalTo: centerYAnchor),
            zoomInButton.trailingAnchor.constraint(equalTo: dimensionLabel.leadingAnchor, constant: -12),
            zoomInButton.centerYAnchor.constraint(equalTo: centerYAnchor),
            zoomResetButton.trailingAnchor.constraint(equalTo: zoomInButton.leadingAnchor, constant: -1),
            zoomResetButton.centerYAnchor.constraint(equalTo: centerYAnchor),
            zoomOutButton.trailingAnchor.constraint(equalTo: zoomResetButton.leadingAnchor, constant: -1),
            zoomOutButton.centerYAnchor.constraint(equalTo: centerYAnchor),
            brandLabel.trailingAnchor.constraint(lessThanOrEqualTo: zoomOutButton.leadingAnchor, constant: -12),
            rule.leadingAnchor.constraint(equalTo: leadingAnchor),
            rule.trailingAnchor.constraint(equalTo: trailingAnchor),
            rule.bottomAnchor.constraint(equalTo: bottomAnchor),
            rule.heightAnchor.constraint(equalToConstant: 1),
        ])
        applyColors()
    }

    required init?(coder: NSCoder) { fatalError() }

    override func viewDidChangeEffectiveAppearance() {
        super.viewDidChangeEffectiveAppearance()
        applyColors()
    }

    private func applyColors() {
        effectiveAppearance.performAsCurrentDrawingAppearance {
            layer?.backgroundColor = Theme.chrome.cgColor
            brandIcon.contentTintColor = Theme.ink
            brandLabel.textColor = Theme.ink
            dimensionLabel.textColor = Theme.inkDim
            rule.layer?.backgroundColor = Theme.rule.cgColor
        }
    }

    func updateZoomScale(_ scale: CGFloat) {
        zoomResetButton.title = "\(Int((scale * 100).rounded()))%"
    }
}

final class EditorWindow: NSWindow {
    var keyHandler: ((NSEvent) -> Bool)?
    var gestureHandler: ((CGFloat, NSPoint) -> Void)?
    var onZoomIn: (() -> Void)? {
        didSet { header?.onZoomIn = onZoomIn }
    }
    var onZoomOut: (() -> Void)? {
        didSet { header?.onZoomOut = onZoomOut }
    }
    var onZoomReset: (() -> Void)? {
        didSet { header?.onZoomReset = onZoomReset }
    }
    private weak var header: EditorHeaderView?
    private weak var canvasScrollView: CanvasScrollView?

    init(canvas: CanvasView, toolbar: EditorToolbar, imagePointSize: NSSize) {
        let headerHeight = Theme.headerHeight
        let toolbarHeight = Theme.toolbarHeight
        let canvasPadding = CanvasHostView.padding
        // Small screenshots must not clip the toolbar cells: the window is at
        // least as wide as the cell row, and the canvas centers in the gap.
        let minWidth = toolbar.minimumWidth
        let screenSize = NSScreen.main?.visibleFrame.size ?? NSSize(width: 1440, height: 900)
        let maxContent = NSSize(
            width: max(minWidth, screenSize.width - 32),
            height: max(headerHeight + toolbarHeight + 160, screenSize.height - 32)
        )
        let paddedImageSize = NSSize(
            width: imagePointSize.width + canvasPadding * 2,
            height: imagePointSize.height + canvasPadding * 2
        )
        let visibleCanvas = NSSize(
            width: min(paddedImageSize.width, maxContent.width),
            height: min(paddedImageSize.height, maxContent.height - headerHeight - toolbarHeight)
        )
        let contentSize = NSSize(
            width: max(visibleCanvas.width, minWidth),
            height: visibleCanvas.height + headerHeight + toolbarHeight
        )

        super.init(
            contentRect: NSRect(origin: .zero, size: contentSize),
            styleMask: [.titled, .closable, .miniaturizable, .resizable, .fullSizeContentView],
            backing: .buffered,
            defer: false
        )
        isReleasedWhenClosed = false
        title = "scrcap"
        titlebarAppearsTransparent = true
        titleVisibility = .hidden
        // isOpaque = true prevents macOS 26's Liquid Glass compositor from
        // tinting the window. backgroundColor matches EditorContainerView's
        // chrome so the window edge outside the content view's cornerRadius
        // arc blends seamlessly rather than showing a ghost background.
        isOpaque = true
        backgroundColor = Theme.chrome
        hasShadow = true
        // Drag anywhere that isn't a control or the canvas (header, toolbar
        // gaps) to move the window.
        isMovableByWindowBackground = true
        minSize = NSSize(width: minWidth, height: headerHeight + toolbarHeight + 160)
        level = .normal
        collectionBehavior = [.moveToActiveSpace, .fullScreenAuxiliary]

        let container = EditorContainerView(frame: NSRect(origin: .zero, size: contentSize))
        container.autoresizingMask = [.width, .height]

        let headerView = EditorHeaderView(frame: NSRect(
            x: 0, y: contentSize.height - headerHeight,
            width: contentSize.width, height: headerHeight
        ))
        headerView.autoresizingMask = [.width, .minYMargin]
        headerView.dimensionLabel.stringValue = Self.dimensionText(imagePointSize)
        headerView.onZoomIn = onZoomIn
        headerView.onZoomOut = onZoomOut
        headerView.onZoomReset = onZoomReset
        header = headerView

        canvas.frame = NSRect(origin: .zero, size: imagePointSize)
        let canvasHost = CanvasHostView(canvas: canvas, imagePointSize: imagePointSize)
        let scroll = CanvasScrollView(frame: NSRect(
            x: 0, y: toolbarHeight,
            width: contentSize.width,
            height: contentSize.height - headerHeight - toolbarHeight
        ))
        canvasScrollView = scroll
        scroll.onMagnify = { [weak self] factor, point in
            self?.gestureHandler?(factor, point)
        }
        canvas.isSpacePanning = { [weak scroll] in scroll?.isSpacePanning == true }
        canvas.forwardSpacePanMouseDown = { [weak scroll] event in scroll?.beginSpacePan(with: event) }
        canvas.forwardSpacePanMouseDragged = { [weak scroll] event in scroll?.continueSpacePan(with: event) }
        canvas.forwardSpacePanMouseUp = { [weak scroll] event in scroll?.endSpacePan(with: event) }
        canvasHost.fit(to: scroll.contentSize)
        scroll.documentView = canvasHost
        scroll.contentView.scroll(to: canvasHost.centeredViewportOrigin(for: scroll.contentSize))
        scroll.reflectScrolledClipView(scroll.contentView)
        scroll.hasVerticalScroller = false
        scroll.hasHorizontalScroller = false
        scroll.autohidesScrollers = true
        scroll.drawsBackground = true
        scroll.backgroundColor = Theme.well
        // Don't let AppKit add titlebar insets/edge effects (renders an
        // opaque glass sheet over the canvas on macOS 26).
        scroll.automaticallyAdjustsContentInsets = false
        scroll.autoresizingMask = [.width, .height]
        scroll.refreshScrollerVisibility()
        // Canvas host is flipped (top-left origin), so tall scrolling captures
        // start with a small amount of empty area above the image.

        toolbar.frame = NSRect(x: 0, y: 0, width: contentSize.width, height: toolbarHeight)
        toolbar.autoresizingMask = [.width, .maxYMargin]

        container.addSubview(scroll)
        container.addSubview(toolbar)
        container.addSubview(headerView)
        contentView = container
    }

    private static func dimensionText(_ size: NSSize) -> String {
        String(format: "%.0f × %.0f", size.width, size.height)
    }

    func updateImagePointSize(_ pointSize: NSSize, layoutCanvas: Bool = true) {
        header?.dimensionLabel.stringValue = Self.dimensionText(pointSize)
        guard let container = contentView,
              let scroll = container.subviews.compactMap({ $0 as? CanvasScrollView }).first,
              let host = scroll.documentView as? CanvasHostView
        else { return }

        host.updateImagePointSize(pointSize, layoutCanvas: layoutCanvas)
        host.fit(to: scroll.contentView.bounds.size, layoutCanvas: layoutCanvas)
        scroll.refreshScrollerVisibility()
    }

    func updateZoomScale(_ scale: CGFloat) {
        updateZoomScale(scale, around: nil)
    }

    func updateZoomScale(_ scale: CGFloat, around windowPoint: NSPoint?) {
        header?.updateZoomScale(scale)
        guard let container = contentView,
              let scroll = container.subviews.compactMap({ $0 as? CanvasScrollView }).first,
              let host = scroll.documentView as? CanvasHostView
        else { return }

        host.updateZoomScale(scale, in: scroll, around: windowPoint)
    }

    /// Always sets zoom to 1 and re-centers the viewport on the current canvas,
    /// even when zoom was already 1 (e.g. after crop or canvas extension).
    func resetZoom() {
        header?.updateZoomScale(1)
        guard let container = contentView,
              let scroll = container.subviews.compactMap({ $0 as? CanvasScrollView }).first,
              let host = scroll.documentView as? CanvasHostView
        else { return }
        host.forceZoomToOne(in: scroll)
    }

    override var canBecomeKey: Bool { true }
    override var canBecomeMain: Bool { true }

    override func keyDown(with event: NSEvent) {
        if shouldToggleSpacePan(for: event) {
            canvasScrollView?.isSpacePanning = true
            return
        }
        if keyHandler?(event) != true {
            super.keyDown(with: event)
        }
    }

    override func keyUp(with event: NSEvent) {
        if event.keyCode == 49 {
            canvasScrollView?.isSpacePanning = false
            return
        }
        super.keyUp(with: event)
    }

    override func resignKey() {
        canvasScrollView?.isSpacePanning = false
        super.resignKey()
    }

    private func shouldToggleSpacePan(for event: NSEvent) -> Bool {
        guard event.keyCode == 49, !event.isARepeat else { return false }
        let mods = event.modifierFlags.intersection(.deviceIndependentFlagsMask)
        guard mods.isEmpty else { return false }
        return !(firstResponder is NSTextView)
    }

    override func cancelOperation(_ sender: Any?) {
        // Esc routes through keyDown via keyHandler; nothing to do here, but
        // overriding prevents the system beep when no responder handles it.
        _ = keyHandler?(NSEvent.keyEvent(
            with: .keyDown, location: .zero, modifierFlags: [], timestamp: 0,
            windowNumber: windowNumber, context: nil, characters: "\u{1b}",
            charactersIgnoringModifiers: "\u{1b}", isARepeat: false, keyCode: 53
        ) ?? NSEvent())
    }
}

// MARK: - Canvas

final class CanvasScrollView: NSScrollView {
    var onMagnify: ((CGFloat, NSPoint) -> Void)?
    var isSpacePanning = false {
        didSet {
            guard isSpacePanning != oldValue else { return }
            if !isSpacePanning {
                panStartWindowPoint = nil
            }
            window?.invalidateCursorRects(for: self)
            (isSpacePanning ? NSCursor.openHand : NSCursor.arrow).set()
        }
    }

    private var panStartWindowPoint: NSPoint?
    private var panStartBoundsOrigin: NSPoint = .zero
    private var isUpdatingScrollerVisibility = false

    override func layout() {
        super.layout()
        (documentView as? CanvasHostView)?.fit(to: contentView.bounds.size)
        refreshScrollerVisibility()
    }

    func refreshScrollerVisibility() {
        guard !isUpdatingScrollerVisibility else { return }
        guard documentView != nil else {
            hasHorizontalScroller = false
            hasVerticalScroller = false
            return
        }

        isUpdatingScrollerVisibility = true
        defer { isUpdatingScrollerVisibility = false }

        for _ in 0..<3 {
            (documentView as? CanvasHostView)?.fit(to: contentView.bounds.size)
            let needsHorizontal = documentView!.bounds.width > contentView.bounds.width + 0.5
            let needsVertical = documentView!.bounds.height > contentView.bounds.height + 0.5
            guard hasHorizontalScroller != needsHorizontal || hasVerticalScroller != needsVertical else { break }
            hasHorizontalScroller = needsHorizontal
            hasVerticalScroller = needsVertical
            tile()
        }
    }

    override func magnify(with event: NSEvent) {
        onMagnify?(1 + event.magnification, event.locationInWindow)
    }

    override func scrollWheel(with event: NSEvent) {
        let mods = event.modifierFlags.intersection(.deviceIndependentFlagsMask)
        guard mods.contains(.command) else {
            super.scrollWheel(with: event)
            return
        }

        let deltaY = event.scrollingDeltaY != 0 ? event.scrollingDeltaY : event.deltaY
        guard deltaY != 0 else { return }

        let factor: CGFloat
        if event.hasPreciseScrollingDeltas {
            factor = pow(1.0025, deltaY)
        } else {
            factor = deltaY > 0 ? 1.1 : 1 / 1.1
        }
        onMagnify?(factor, event.locationInWindow)
    }

    override func smartMagnify(with event: NSEvent) {
        onMagnify?(event.magnification >= 0 ? 2 : 0.5, event.locationInWindow)
    }

    override func resetCursorRects() {
        super.resetCursorRects()
        if isSpacePanning {
            addCursorRect(bounds, cursor: NSCursor.openHand)
        }
    }

    override func mouseDown(with event: NSEvent) {
        guard isSpacePanning else {
            super.mouseDown(with: event)
            return
        }
        beginSpacePan(with: event)
    }

    override func mouseDragged(with event: NSEvent) {
        guard panStartWindowPoint != nil else {
            super.mouseDragged(with: event)
            return
        }
        continueSpacePan(with: event)
    }

    override func mouseUp(with event: NSEvent) {
        guard panStartWindowPoint != nil else {
            super.mouseUp(with: event)
            return
        }
        endSpacePan(with: event)
    }

    func beginSpacePan(with event: NSEvent) {
        guard isSpacePanning else { return }
        panStartWindowPoint = event.locationInWindow
        panStartBoundsOrigin = contentView.bounds.origin
        NSCursor.closedHand.set()
    }

    func continueSpacePan(with event: NSEvent) {
        guard let start = panStartWindowPoint else { return }
        let delta = NSPoint(
            x: event.locationInWindow.x - start.x,
            y: event.locationInWindow.y - start.y
        )
        contentView.scroll(to: clampedScrollOrigin(NSPoint(
            x: panStartBoundsOrigin.x - delta.x,
            y: panStartBoundsOrigin.y + delta.y
        )))
        reflectScrolledClipView(contentView)
    }

    func endSpacePan(with event: NSEvent) {
        panStartWindowPoint = nil
        if isSpacePanning {
            NSCursor.openHand.set()
        }
    }

    private func clampedScrollOrigin(_ origin: NSPoint) -> NSPoint {
        guard let documentView else { return origin }
        return NSPoint(
            x: max(0, min(origin.x, documentView.bounds.width - contentView.bounds.width)),
            y: max(0, min(origin.y, documentView.bounds.height - contentView.bounds.height))
        )
    }
}

/// Top-left-origin container that keeps a little breathing room around the
/// screenshot and recenters it whenever the editor window is resized.
final class CanvasHostView: NSView {
    static let padding: CGFloat = 36

    private let canvas: CanvasView
    private var imagePointSize: NSSize
    private var zoomScale: CGFloat = 1

    init(canvas: CanvasView, imagePointSize: NSSize) {
        self.canvas = canvas
        self.imagePointSize = imagePointSize
        super.init(frame: .zero)
        addSubview(canvas)
    }

    required init?(coder: NSCoder) { fatalError() }

    override var isFlipped: Bool { true }
    override var mouseDownCanMoveWindow: Bool { false }

    func updateImagePointSize(_ pointSize: NSSize, layoutCanvas: Bool = true) {
        imagePointSize = pointSize
        canvas.setPointSize(pointSize, zoomScale: zoomScale)
        if layoutCanvas {
            needsLayout = true
        }
    }

    func updateZoomScale(_ scale: CGFloat, in scrollView: NSScrollView, around windowPoint: NSPoint?) {
        let oldViewportPoint = windowPoint.map { convert($0, from: nil) } ?? NSPoint(
            x: scrollView.contentView.bounds.midX,
            y: scrollView.contentView.bounds.midY
        )
        let oldOrigin = canvas.frame.origin
        let imageAnchor = NSPoint(
            x: (oldViewportPoint.x - oldOrigin.x) / max(zoomScale, 0.001),
            y: (oldViewportPoint.y - oldOrigin.y) / max(zoomScale, 0.001)
        )

        zoomScale = scale
        canvas.setPointSize(imagePointSize, zoomScale: scale)
        fit(to: scrollView.contentView.bounds.size)
        scrollView.layoutSubtreeIfNeeded()

        let newOrigin = canvas.frame.origin
        let newViewportPoint = NSPoint(
            x: newOrigin.x + imageAnchor.x * scale,
            y: newOrigin.y + imageAnchor.y * scale
        )
        let clipSize = scrollView.contentView.bounds.size
        let cursorInClip = windowPoint.map { scrollView.contentView.convert($0, from: nil) } ?? NSPoint(
            x: clipSize.width / 2,
            y: clipSize.height / 2
        )
        scrollView.contentView.scroll(to: NSPoint(
            x: max(0, min(newViewportPoint.x - cursorInClip.x, bounds.width - clipSize.width)),
            y: max(0, min(newViewportPoint.y - cursorInClip.y, bounds.height - clipSize.height))
        ))
        scrollView.reflectScrolledClipView(scrollView.contentView)
        (scrollView as? CanvasScrollView)?.refreshScrollerVisibility()
    }

    func fit(to viewportSize: NSSize, layoutCanvas: Bool = true) {
        let zoomedSize = NSSize(width: imagePointSize.width * zoomScale, height: imagePointSize.height * zoomScale)
        let size = NSSize(
            width: max(zoomedSize.width + Self.padding * 2, viewportSize.width),
            height: max(zoomedSize.height + Self.padding * 2, viewportSize.height)
        )
        guard frame.size != size else { return }
        setFrameSize(size)
        if layoutCanvas {
            needsLayout = true
        }
    }

    func centeredViewportOrigin(for viewportSize: NSSize) -> NSPoint {
        NSPoint(
            x: max(0, (bounds.width - viewportSize.width) / 2),
            y: max(0, (bounds.height - viewportSize.height) / 2)
        )
    }

    /// Unconditionally resets zoom to 1 and re-centers the viewport on the
    /// current canvas. Called by the "100%" button so that crop or canvas
    /// extension is reflected even when zoom was already 1.
    func forceZoomToOne(in scrollView: NSScrollView) {
        zoomScale = 1
        canvas.setPointSize(imagePointSize, zoomScale: 1)
        fit(to: scrollView.contentView.bounds.size)
        scrollView.layoutSubtreeIfNeeded()
        (scrollView as? CanvasScrollView)?.refreshScrollerVisibility()
        scrollView.layoutSubtreeIfNeeded()

        let viewportSize = scrollView.contentView.bounds.size
        let centeredOrigin = centeredViewportOrigin(for: viewportSize)
        let origin = NSPoint(
            x: max(0, min(centeredOrigin.x, bounds.width - viewportSize.width)),
            y: max(0, min(centeredOrigin.y, bounds.height - viewportSize.height))
        )
        scrollView.contentView.scroll(to: origin)
        scrollView.reflectScrolledClipView(scrollView.contentView)
    }

    override func layout() {
        super.layout()
        let zoomedSize = NSSize(width: imagePointSize.width * zoomScale, height: imagePointSize.height * zoomScale)
        canvas.setPointSize(imagePointSize, zoomScale: zoomScale)
        canvas.setFrameOrigin(NSPoint(
            x: max(Self.padding, ((bounds.width - zoomedSize.width) / 2).rounded()),
            y: max(Self.padding, ((bounds.height - zoomedSize.height) / 2).rounded())
        ))
    }
}

final class CanvasView: NSView, NSDraggingSource {
    weak var dataSource: CanvasDataSource?
    var isSpacePanning: (() -> Bool)?
    var forwardSpacePanMouseDown: ((NSEvent) -> Void)?
    var forwardSpacePanMouseDragged: ((NSEvent) -> Void)?
    var forwardSpacePanMouseUp: ((NSEvent) -> Void)?

    private var bitmap: CGImage
    private var bitmapImage: NSImage
    private var liveShape: Shape?
    private var liveCropRect: NSRect?
    private var dragStart: NSPoint?
    private var isDragOut = false
    private var textEditor: AnnotationTextView?

    init(bitmap: CGImage, pointSize: NSSize) {
        self.bitmap = bitmap
        // NSImage.draw respects the flipped context, unlike raw CGContext.draw.
        self.bitmapImage = NSImage(cgImage: bitmap, size: pointSize)
        super.init(frame: NSRect(origin: .zero, size: pointSize))
        setBoundsSize(pointSize)
        wantsLayer = true
    }

    required init?(coder: NSCoder) { fatalError() }

    override var isFlipped: Bool { true }
    // Dragging on the image must draw, never move the window.
    override var mouseDownCanMoveWindow: Bool { false }

    override func draw(_ dirtyRect: NSRect) {
        bitmapImage.draw(in: bounds)
        // Flat 1px frame so screenshots remain measurable and unornamented.
        Theme.rule.withAlphaComponent(0.70).setStroke()
        let frame = NSBezierPath(rect: bounds.insetBy(dx: 0.5, dy: 0.5))
        frame.lineWidth = 1
        frame.stroke()
        guard let source = dataSource else { return }
        NSGraphicsContext.saveGraphicsState()
        NSBezierPath(rect: bounds).addClip()
        ShapeRenderer.draw(source.visibleShapes, palette: source.palette, strokeWidth: source.strokeWidth)
        if let liveShape {
            ShapeRenderer.draw(liveShape, palette: source.palette, strokeWidth: source.strokeWidth)
        }
        NSGraphicsContext.restoreGraphicsState()
        if let liveCropRect {
            drawCropPreview(liveCropRect)
        }
    }

    func replaceBitmap(
        _ image: CGImage,
        pointSize: NSSize,
        clearTransientState: Bool = true,
        leadingExpansionOffset: NSPoint = .zero
    ) {
        let zoomScale = frame.width / max(bounds.width, 1)
        let oldOrigin = frame.origin
        bitmap = image
        bitmapImage = NSImage(cgImage: image, size: pointSize)
        if clearTransientState {
            liveShape = nil
            liveCropRect = nil
        }
        setPointSize(pointSize, zoomScale: zoomScale)
        if leadingExpansionOffset != .zero {
            setFrameOrigin(NSPoint(
                x: oldOrigin.x - leadingExpansionOffset.x * zoomScale,
                y: oldOrigin.y - leadingExpansionOffset.y * zoomScale
            ))
        }
        needsDisplay = true
    }

    func setPointSize(_ pointSize: NSSize, zoomScale: CGFloat) {
        setFrameSize(NSSize(width: pointSize.width * zoomScale, height: pointSize.height * zoomScale))
        setBoundsSize(pointSize)
    }

    private func canvasPoint(_ event: NSEvent) -> NSPoint {
        convert(event.locationInWindow, from: nil)
    }

    private func corePoint(_ event: NSEvent) -> CorePoint {
        let p = canvasPoint(event)
        return CorePoint(x: p.x, y: p.y)
    }

    private func cropRect(from start: NSPoint, to end: NSPoint) -> NSRect {
        NSRect(
            x: min(start.x, end.x),
            y: min(start.y, end.y),
            width: abs(start.x - end.x),
            height: abs(start.y - end.y)
        )
    }

    override func mouseDown(with event: NSEvent) {
        if isSpacePanning?() == true {
            forwardSpacePanMouseDown?(event)
            return
        }
        guard let source = dataSource else { return }
        let point = canvasPoint(event)
        let core = CorePoint(x: point.x, y: point.y)
        dragStart = point
        isDragOut = event.modifierFlags.contains(.option)
        guard !isDragOut else { return }

        switch source.currentTool {
        case .arrow:
            liveShape = Shape(kind: .arrow, colorIndex: source.currentColorIndex, start: core, end: core)
        case .rectangle:
            liveShape = Shape(kind: .rectangle, colorIndex: source.currentColorIndex, start: core, end: core)
        case .counter:
            liveShape = nil // counters stamp on mouse-up (click)
        case .text:
            // Clicking elsewhere while typing commits the current text first.
            let wasEditing = isEditingText
            commitTextEditing()
            if !wasEditing {
                beginTextEditing(at: point)
            }
        case .crop:
            commitTextEditing()
            liveCropRect = NSRect(origin: point, size: .zero)
        }
    }

    override func mouseDragged(with event: NSEvent) {
        if isSpacePanning?() == true {
            forwardSpacePanMouseDragged?(event)
            return
        }
        if isDragOut {
            startDragOutSession(with: event)
            return
        }
        if let start = dragStart, dataSource?.currentTool == .crop {
            liveCropRect = cropRect(from: start, to: canvasPoint(event))
            needsDisplay = true
            return
        }
        guard let source = dataSource, var shape = liveShape else { return }
        var end = corePoint(event)
        if case .rectangle = shape.kind, event.modifierFlags.contains(.shift) {
            // ⇧-drag constrains to a square.
            let side = max(abs(end.x - shape.start.x), abs(end.y - shape.start.y))
            end = CorePoint(
                x: shape.start.x + side * (end.x >= shape.start.x ? 1 : -1),
                y: shape.start.y + side * (end.y >= shape.start.y ? 1 : -1)
            )
        }
        shape.end = end
        if source.autoExpandCanvas {
            let offset = source.expandCanvasIfNeeded(toFit: expansionRect(for: shape, strokeWidth: source.strokeWidth))
            if offset != .zero {
                shape = shape.offsetBy(x: offset.x, y: offset.y)
                dragStart = dragStart.map { NSPoint(x: $0.x + offset.x, y: $0.y + offset.y) }
            }
        }
        liveShape = shape
        needsDisplay = true
    }

    private func expansionRect(for shape: Shape, strokeWidth: CGFloat) -> NSRect {
        let minX = min(shape.start.x, shape.end.x)
        let minY = min(shape.start.y, shape.end.y)
        let maxX = max(shape.start.x, shape.end.x)
        let maxY = max(shape.start.y, shape.end.y)
        let drawingPad: CGFloat
        switch shape.kind {
        case .rectangle:
            drawingPad = ceil(max(strokeWidth, 1) / 2) + 1
        case .arrow:
            drawingPad = max(28, strokeWidth * 8)
        case .counter:
            drawingPad = ShapeRenderer.counterRadius
        case .text:
            drawingPad = 0
        }
        return NSRect(
            x: minX - drawingPad,
            y: minY - drawingPad,
            width: maxX - minX + drawingPad * 2,
            height: maxY - minY + drawingPad * 2
        )
    }

    override func mouseUp(with event: NSEvent) {
        if isSpacePanning?() == true {
            forwardSpacePanMouseUp?(event)
            return
        }
        defer {
            liveShape = nil
            dragStart = nil
            isDragOut = false
        }
        guard let source = dataSource else { return }

        if source.currentTool == .crop, !isDragOut {
            guard let rect = liveCropRect else { return }
            guard rect.width > 3, rect.height > 3 else { return }
            source.commitCrop(rect)
            return
        }

        if source.currentTool == .counter, !isDragOut {
            let point = corePoint(event)
            var shape = Shape(
                kind: .counter(number: source.nextCounterNumber()),
                colorIndex: source.currentColorIndex,
                start: point,
                end: point
            )
            let offset = source.expandCanvasIfNeeded(toFit: expansionRect(for: shape, strokeWidth: source.strokeWidth))
            if offset != .zero {
                shape = shape.offsetBy(x: offset.x, y: offset.y)
            }
            source.commit(shape: shape)
            return
        }

        guard let shape = liveShape else { return }
        let dx = abs(shape.end.x - shape.start.x)
        let dy = abs(shape.end.y - shape.start.y)
        guard dx > 3 || dy > 3 else { return } // ignore accidental clicks
        source.commit(shape: shape)
    }

    private func drawCropPreview(_ rect: NSRect) {
        let crop = rect.standardized.intersection(bounds)
        guard crop.width > 0, crop.height > 0,
              let ctx = NSGraphicsContext.current?.cgContext
        else { return }

        NSColor.black.withAlphaComponent(0.34).setFill()
        let path = NSBezierPath(rect: bounds)
        path.append(NSBezierPath(rect: crop).reversed)
        path.fill()

        // Same hazard-tape stroke as the capture overlay: "this region will
        // be kept" looks identical everywhere.
        Theme.strokeHazardAnts(crop, phase: 0, in: ctx)
        Theme.drawHazardHandles(crop)

        // Live size readout — same tag block as the capture overlay. The
        // canvas is flipped, so "above the crop" is minY-side.
        ThemeTag.draw(
            [.text(String(format: "%.0f × %.0f", crop.width, crop.height))],
            near: NSPoint(x: crop.midX, y: crop.minY - 38),
            in: NSRect(origin: .zero, size: bounds.size)
        )
    }

    // MARK: Text tool — type in place; Return behavior is configurable

    var isEditingText: Bool { textEditor != nil }
    #if DEBUG
    var debugTextEditor: AnnotationTextView? { textEditor }

    func debugBeginTextEditing(at point: NSPoint) {
        beginTextEditing(at: point)
    }
    #endif

    private func beginTextEditing(at point: NSPoint) {
        guard let source = dataSource else { return }
        let font = ShapeRenderer.textFont(size: source.textSize)
        let color = ShapeRenderer.color(at: source.currentColorIndex, palette: source.palette)

        let editor = AnnotationTextView(
            origin: point,
            font: font,
            color: color,
            maxWidth: bounds.width - point.x,
            enterBehavior: source.textEnterBehavior
        )
        editor.onCommit = { [weak self] in self?.commitTextEditing() }
        editor.onCancel = { [weak self] in self?.commitTextEditing() }
        addSubview(editor)
        textEditor = editor
        window?.makeFirstResponder(editor)
    }

    func commitTextEditing() {
        guard let editor = textEditor else { return }
        let string = editor.string.trimmingCharacters(in: .whitespacesAndNewlines)
        let origin = editor.frame.origin
        removeTextEditor()
        guard let source = dataSource, !string.isEmpty else { return }
        let anchor = CorePoint(x: origin.x, y: origin.y)
        source.commit(shape: Shape(
            kind: .text(string: string, size: source.textSize),
            colorIndex: source.currentColorIndex,
            start: anchor,
            end: anchor
        ))
    }

    func cancelTextEditing() {
        removeTextEditor()
    }

    private func removeTextEditor() {
        guard let editor = textEditor else { return }
        textEditor = nil
        window?.makeFirstResponder(self)
        editor.removeFromSuperview()
        needsDisplay = true
    }

    // MARK: Drag-out (⌥-drag the flattened PNG into Slack, Finder, mail…)

    private func startDragOutSession(with event: NSEvent) {
        guard isDragOut, let source = dataSource, let start = dragStart else { return }
        let current = convert(event.locationInWindow, from: nil)
        guard hypot(current.x - start.x, current.y - start.y) > 6 else { return }
        isDragOut = false

        let (image, pattern, pointScale) = source.dragOutImage()
        guard let fileURL = Exporter.tempFileForDrag(image, pattern: pattern, pointScale: pointScale) else { return }

        let item = NSDraggingItem(pasteboardWriter: fileURL as NSURL)
        let dragSize = NSSize(width: 160, height: 160 * CGFloat(image.height) / CGFloat(image.width))
        let preview = NSImage(cgImage: image, size: dragSize)
        item.setDraggingFrame(
            NSRect(origin: NSPoint(x: current.x - dragSize.width / 2, y: current.y - dragSize.height / 2),
                   size: dragSize),
            contents: preview
        )
        beginDraggingSession(with: [item], event: event, source: self)
    }

    func draggingSession(_ session: NSDraggingSession, sourceOperationMaskFor context: NSDraggingContext) -> NSDragOperation {
        .copy
    }

}

private final class HeaderIconButton: NSControl {
    var onClick: (() -> Void)?
    private var isHovered = false {
        didSet { updateAppearance() }
    }
    private let iconView = NSImageView()

    override var mouseDownCanMoveWindow: Bool { false }

    init(symbolName: String) {
        super.init(frame: .zero)
        wantsLayer = true
        layer?.cornerRadius = 6
        layer?.borderWidth = 1
        translatesAutoresizingMaskIntoConstraints = false
        iconView.image = NSImage(systemSymbolName: symbolName, accessibilityDescription: nil)?
            .withSymbolConfiguration(Theme.iconConfiguration)
        iconView.imageScaling = .scaleProportionallyDown
        iconView.translatesAutoresizingMaskIntoConstraints = false
        addSubview(iconView)
        NSLayoutConstraint.activate([
            widthAnchor.constraint(equalToConstant: 26),
            heightAnchor.constraint(equalToConstant: 22),
            iconView.widthAnchor.constraint(equalToConstant: 14),
            iconView.heightAnchor.constraint(equalToConstant: 14),
            iconView.centerXAnchor.constraint(equalTo: centerXAnchor),
            iconView.centerYAnchor.constraint(equalTo: centerYAnchor),
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
    override func mouseDown(with event: NSEvent) { onClick?() }
    override func viewDidChangeEffectiveAppearance() {
        super.viewDidChangeEffectiveAppearance()
        updateAppearance()
    }

    private func updateAppearance() {
        effectiveAppearance.performAsCurrentDrawingAppearance {
            layer?.backgroundColor = isHovered ? Theme.hoverWash.cgColor : NSColor.clear.cgColor
            layer?.borderColor = (isHovered ? Theme.accent.withAlphaComponent(0.45) : Theme.hairline).cgColor
            iconView.contentTintColor = Theme.ink
        }
    }
}

private final class HeaderTextButton: NSControl {
    var onClick: (() -> Void)?
    var title: String {
        get { label.stringValue }
        set { label.stringValue = newValue }
    }
    private var isHovered = false {
        didSet { updateAppearance() }
    }
    private let label: NSTextField

    override var mouseDownCanMoveWindow: Bool { false }

    init(title: String) {
        label = NSTextField(labelWithString: title)
        super.init(frame: .zero)
        wantsLayer = true
        layer?.cornerRadius = 6
        layer?.borderWidth = 1
        translatesAutoresizingMaskIntoConstraints = false
        label.font = Theme.headerFont
        label.alignment = .center
        label.translatesAutoresizingMaskIntoConstraints = false
        addSubview(label)
        NSLayoutConstraint.activate([
            widthAnchor.constraint(equalToConstant: 42),
            heightAnchor.constraint(equalToConstant: 22),
            label.leadingAnchor.constraint(equalTo: leadingAnchor, constant: 4),
            label.trailingAnchor.constraint(equalTo: trailingAnchor, constant: -4),
            label.centerYAnchor.constraint(equalTo: centerYAnchor),
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
    override func mouseDown(with event: NSEvent) { onClick?() }
    override func viewDidChangeEffectiveAppearance() {
        super.viewDidChangeEffectiveAppearance()
        updateAppearance()
    }

    private func updateAppearance() {
        effectiveAppearance.performAsCurrentDrawingAppearance {
            layer?.backgroundColor = isHovered ? Theme.hoverWash.cgColor : NSColor.clear.cgColor
            layer?.borderColor = (isHovered ? Theme.accent.withAlphaComponent(0.45) : Theme.hairline).cgColor
            label.textColor = Theme.inkDim
        }
    }
}

// MARK: - Inline text editor

/// Borderless in-place text view, metrics-matched to ShapeRenderer's text
/// drawing so the committed annotation lands exactly where it was typed.
///
/// Built on an explicit TextKit 1 stack: the default NSTextView(frame:) is
/// TextKit 2 on modern macOS, and touching `layoutManager` on one forces a
/// mid-flight downgrade — crashy. With a manual stack the view does NOT
/// retain its NSTextStorage, so we hold it ourselves.
final class AnnotationTextView: NSTextView {
    var onCommit: (() -> Void)?
    var onCancel: (() -> Void)?

    private let enterBehavior: TextEnterBehavior
    private let storage: NSTextStorage
    private let manualLayoutManager: NSLayoutManager

    init(origin: NSPoint, font: NSFont, color: NSColor, maxWidth: CGFloat, enterBehavior: TextEnterBehavior) {
        let width = max(maxWidth, 60)
        let storage = NSTextStorage()
        let layoutManager = NSLayoutManager()
        storage.addLayoutManager(layoutManager)
        let container = NSTextContainer(size: NSSize(width: width, height: .greatestFiniteMagnitude))
        container.widthTracksTextView = true
        container.lineFragmentPadding = 0
        layoutManager.addTextContainer(container)
        self.storage = storage
        self.manualLayoutManager = layoutManager
        self.enterBehavior = enterBehavior

        super.init(
            frame: NSRect(x: origin.x, y: origin.y, width: width, height: ceil(font.pointSize * 1.5)),
            textContainer: container
        )
        self.font = font
        textColor = color
        insertionPointColor = color
        typingAttributes = ShapeRenderer.textAttributes(size: font.pointSize, color: color)
        isRichText = false
        drawsBackground = false
        allowsUndo = true
        isVerticallyResizable = false
        isHorizontallyResizable = false
        textContainerInset = .zero
        sizeToFitContent()
    }

    @available(*, unavailable)
    required init?(coder: NSCoder) { fatalError() }

    override func keyDown(with event: NSEvent) {
        switch event.keyCode {
        case 36 where shouldCommitOnReturn(event): // Return commits in the selected mode
            onCommit?()
        case 53: // Esc exits text entry without discarding typed text
            onCancel?()
        default:
            super.keyDown(with: event)
        }
    }

    private func shouldCommitOnReturn(_ event: NSEvent) -> Bool {
        let shift = event.modifierFlags.contains(.shift)
        switch enterBehavior {
        case .newline:
            return shift
        case .commit:
            return !shift
        }
    }

    override func didChangeText() {
        super.didChangeText()
        sizeToFitContent()
    }

    private func sizeToFitContent() {
        guard let container = textContainer else { return }
        manualLayoutManager.ensureLayout(for: container)
        let used = manualLayoutManager.usedRect(for: container).size
        let lineHeight = (font ?? .systemFont(ofSize: 16)).boundingRectForFont.height
        setFrameSize(NSSize(
            width: frame.width,
            height: ceil(max(used.height, lineHeight)) + 2
        ))
    }
}
