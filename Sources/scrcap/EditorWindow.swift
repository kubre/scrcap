// EditorWindow — borderless window that appears already-loaded: screenshot at
// 1:1, toolbar docked on top. Draw-stays-armed: shapes commit on mouse-up, the
// tool stays armed, Esc means exactly one thing — done (copy + close).

import AppKit
import ScrcapCore

enum EditorTool: CaseIterable {
    case arrow, rectangle, counter, text
}

final class EditorWindowController: NSObject, NSWindowDelegate {
    private let window: EditorWindow
    private let canvas: CanvasView
    private let toolbar: EditorToolbar
    private let settingsStore: SettingsStore
    private let bitmap: CGImage
    private let scale: CGFloat
    private var stack = AnnotationStack()
    private var onClose: (() -> Void)?

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
            escCopies: settingsStore.settings.escBehavior == .copyAndClose
        )
        window = EditorWindow(canvas: canvas, toolbar: toolbar, imagePointSize: pointSize)

        super.init()

        window.delegate = self
        window.keyHandler = { [weak self] event in self?.handleKey(event) ?? false }
        canvas.dataSource = self
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
            case "w":
                close()
                return true
            default:
                return false
            }
        }

        if event.keyCode == 53 { // Esc
            // While typing, Esc only cancels the text entry — never the window.
            // (Belt and braces: the text view normally swallows it first.)
            if canvas.isEditingText {
                canvas.cancelTextEditing()
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

    /// The Esc / "Done" exit: copy per settings, then close.
    private func finish() {
        canvas.commitTextEditing()
        if settingsStore.settings.escBehavior == .copyAndClose {
            copyToClipboard()
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
            scale: scale
        )
    }

    private func copyToClipboard() {
        Exporter.copyToClipboard(flattened())
    }

    private func saveAs() {
        let settings = settingsStore.settings
        let panel = NSSavePanel()
        panel.directoryURL = Exporter.defaultSaveFolder(settings: settings)
        panel.nameFieldStringValue = Exporter.filename(pattern: settings.filenamePattern)
        panel.beginSheetModal(for: window) { [weak self] response in
            guard let self, response == .OK, let url = panel.url else { return }
            Exporter.writePNG(self.flattened(), to: url)
        }
    }

    private func close() {
        window.orderOut(nil)
        windowWillClose(Notification(name: NSWindow.willCloseNotification))
    }

    func windowWillClose(_ notification: Notification) {
        let callback = onClose
        onClose = nil
        EditorWindowController.open.remove(self)
        callback?()
    }

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
}

// MARK: - CanvasDataSource

protocol CanvasDataSource: AnyObject {
    var currentTool: EditorTool { get }
    var currentColorIndex: Int { get }
    var visibleShapes: [Shape] { get }
    var palette: [String] { get }
    var strokeWidth: CGFloat { get }
    var textSize: CGFloat { get }
    func commit(shape: Shape)
    func nextCounterNumber() -> Int
    func dragOutImage() -> (CGImage, String)
}

extension EditorWindowController: CanvasDataSource {
    var currentTool: EditorTool { tool }
    var currentColorIndex: Int { colorIndex }
    var visibleShapes: [Shape] { Array(stack.visible) }
    var palette: [String] { settingsStore.settings.paletteHex }
    var strokeWidth: CGFloat { settingsStore.settings.strokeWidth }
    var textSize: CGFloat { settingsStore.settings.textSize }

    func commit(shape: Shape) {
        stack.append(shape)
        canvas.needsDisplay = true
    }

    func nextCounterNumber() -> Int { stack.nextCounterNumber }

    func dragOutImage() -> (CGImage, String) {
        (flattened(), settingsStore.settings.filenamePattern)
    }
}

// MARK: - Window

/// Editor chrome that re-resolves its dynamic colors when the system theme
/// flips (CGColor assignments don't auto-adapt the way semantic NSColors do).
final class EditorContainerView: NSView {
    override init(frame: NSRect) {
        super.init(frame: frame)
        wantsLayer = true
        layer?.cornerRadius = 10
        layer?.masksToBounds = true
        layer?.borderWidth = 1
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
            layer?.borderColor = EditorStyle.border.cgColor
        }
    }
}

final class EditorWindow: NSWindow {
    var keyHandler: ((NSEvent) -> Bool)?

    init(canvas: CanvasView, toolbar: EditorToolbar, imagePointSize: NSSize) {
        let toolbarHeight = EditorStyle.height
        // Small screenshots must not clip the toolbar: the window is at least
        // as wide as the toolbar's content, and the canvas centers in the gap.
        let minWidth = toolbar.fittingSize.width + 24
        let maxCanvas = (NSScreen.main?.visibleFrame.size ?? NSSize(width: 1440, height: 900))
        let visibleCanvas = NSSize(
            width: min(imagePointSize.width, maxCanvas.width * 0.92),
            height: min(imagePointSize.height, maxCanvas.height * 0.88 - toolbarHeight)
        )
        let contentSize = NSSize(
            width: max(visibleCanvas.width, minWidth),
            height: visibleCanvas.height + toolbarHeight
        )

        super.init(
            contentRect: NSRect(origin: .zero, size: contentSize),
            styleMask: [.borderless, .fullSizeContentView],
            backing: .buffered,
            defer: false
        )
        isOpaque = false
        backgroundColor = .clear
        hasShadow = true
        // Drag anywhere that isn't a control or the canvas (i.e. the toolbar
        // background) to move the window.
        isMovableByWindowBackground = true
        level = .floating
        collectionBehavior = [.moveToActiveSpace, .fullScreenAuxiliary]

        let container = EditorContainerView(frame: NSRect(origin: .zero, size: contentSize))

        canvas.frame = NSRect(origin: .zero, size: imagePointSize)
        // Toolbar docked at the top (Cocoa coords: top = maxY).
        let scroll = NSScrollView(frame: NSRect(
            x: 0, y: 0,
            width: contentSize.width, height: visibleCanvas.height
        ))
        if imagePointSize.width < contentSize.width {
            // Center a narrow image inside the toolbar-width window.
            let doc = FlippedView(frame: NSRect(
                x: 0, y: 0, width: contentSize.width, height: imagePointSize.height
            ))
            canvas.setFrameOrigin(NSPoint(
                x: ((contentSize.width - imagePointSize.width) / 2).rounded(), y: 0
            ))
            doc.addSubview(canvas)
            scroll.documentView = doc
        } else {
            scroll.documentView = canvas
        }
        scroll.hasVerticalScroller = imagePointSize.height > visibleCanvas.height
        scroll.hasHorizontalScroller = imagePointSize.width > visibleCanvas.width
        scroll.drawsBackground = false
        scroll.autoresizingMask = [.width, .height]
        // Canvas is flipped (top-left origin), so tall scrolling captures
        // already start at the top of the document.

        toolbar.frame = NSRect(
            x: 0, y: contentSize.height - toolbarHeight,
            width: contentSize.width, height: toolbarHeight
        )
        toolbar.autoresizingMask = [.width, .minYMargin]

        container.addSubview(scroll)
        container.addSubview(toolbar)
        contentView = container
    }

    override var canBecomeKey: Bool { true }
    override var canBecomeMain: Bool { true }

    override func keyDown(with event: NSEvent) {
        if keyHandler?(event) != true {
            super.keyDown(with: event)
        }
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

/// Top-left-origin container used to center a narrow canvas; flipped so the
/// canvas keeps its coordinate convention inside the scroll view.
final class FlippedView: NSView {
    override var isFlipped: Bool { true }
    override var mouseDownCanMoveWindow: Bool { false }
}

final class CanvasView: NSView, NSDraggingSource {
    weak var dataSource: CanvasDataSource?

    private let bitmap: CGImage
    private var liveShape: Shape?
    private var dragStart: NSPoint?
    private var isDragOut = false
    private var textEditor: AnnotationTextView?

    private let bitmapImage: NSImage

    init(bitmap: CGImage, pointSize: NSSize) {
        self.bitmap = bitmap
        // NSImage.draw respects the flipped context, unlike raw CGContext.draw.
        self.bitmapImage = NSImage(cgImage: bitmap, size: pointSize)
        super.init(frame: NSRect(origin: .zero, size: pointSize))
        wantsLayer = true
    }

    required init?(coder: NSCoder) { fatalError() }

    override var isFlipped: Bool { true }
    // Dragging on the image must draw, never move the window.
    override var mouseDownCanMoveWindow: Bool { false }

    override func draw(_ dirtyRect: NSRect) {
        bitmapImage.draw(in: bounds)
        guard let source = dataSource else { return }
        ShapeRenderer.draw(source.visibleShapes, palette: source.palette, strokeWidth: source.strokeWidth)
        if let liveShape {
            ShapeRenderer.draw(liveShape, palette: source.palette, strokeWidth: source.strokeWidth)
        }
    }

    private func corePoint(_ event: NSEvent) -> CorePoint {
        let p = convert(event.locationInWindow, from: nil)
        return CorePoint(x: p.x, y: p.y)
    }

    override func mouseDown(with event: NSEvent) {
        guard let source = dataSource else { return }
        let point = corePoint(event)
        dragStart = NSPoint(x: point.x, y: point.y)
        isDragOut = event.modifierFlags.contains(.option)
        guard !isDragOut else { return }

        switch source.currentTool {
        case .arrow:
            liveShape = Shape(kind: .arrow, colorIndex: source.currentColorIndex, start: point, end: point)
        case .rectangle:
            liveShape = Shape(kind: .rectangle, colorIndex: source.currentColorIndex, start: point, end: point)
        case .counter:
            liveShape = nil // counters stamp on mouse-up (click)
        case .text:
            // Clicking elsewhere while typing commits the current text first.
            let wasEditing = isEditingText
            commitTextEditing()
            if !wasEditing {
                beginTextEditing(at: NSPoint(x: point.x, y: point.y))
            }
        }
    }

    override func mouseDragged(with event: NSEvent) {
        if isDragOut {
            startDragOutSession(with: event)
            return
        }
        guard var shape = liveShape else { return }
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
        liveShape = shape
        needsDisplay = true
    }

    override func mouseUp(with event: NSEvent) {
        defer {
            liveShape = nil
            dragStart = nil
            isDragOut = false
        }
        guard let source = dataSource else { return }

        if source.currentTool == .counter, !isDragOut {
            let point = corePoint(event)
            source.commit(shape: Shape(
                kind: .counter(number: source.nextCounterNumber()),
                colorIndex: source.currentColorIndex,
                start: point,
                end: point
            ))
            return
        }

        guard let shape = liveShape else { return }
        let dx = abs(shape.end.x - shape.start.x)
        let dy = abs(shape.end.y - shape.start.y)
        guard dx > 3 || dy > 3 else { return } // ignore accidental clicks
        source.commit(shape: shape)
    }

    // MARK: Text tool — type in place; ⏎ commits, ⇧⏎ newline, Esc cancels

    var isEditingText: Bool { textEditor != nil }
    var debugTextEditor: AnnotationTextView? { textEditor }

    func debugBeginTextEditing(at point: NSPoint) {
        beginTextEditing(at: point)
    }

    private func beginTextEditing(at point: NSPoint) {
        guard let source = dataSource else { return }
        let font = ShapeRenderer.textFont(size: source.textSize)
        let color = ShapeRenderer.color(at: source.currentColorIndex, palette: source.palette)

        let editor = AnnotationTextView(origin: point, font: font, color: color, maxWidth: bounds.width - point.x)
        editor.onCommit = { [weak self] in self?.commitTextEditing() }
        editor.onCancel = { [weak self] in self?.cancelTextEditing() }
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

        let (image, pattern) = source.dragOutImage()
        guard let fileURL = Exporter.tempFileForDrag(image, pattern: pattern) else { return }

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

    private let storage: NSTextStorage
    private let manualLayoutManager: NSLayoutManager

    init(origin: NSPoint, font: NSFont, color: NSColor, maxWidth: CGFloat) {
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

        super.init(
            frame: NSRect(x: origin.x, y: origin.y, width: width, height: ceil(font.pointSize * 1.5)),
            textContainer: container
        )
        self.font = font
        textColor = color
        insertionPointColor = color
        typingAttributes = [.font: font, .foregroundColor: color]
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
        case 36 where !event.modifierFlags.contains(.shift): // ⏎ confirms
            onCommit?()
        case 53: // Esc cancels only the text entry
            onCancel?()
        default: // ⇧⏎ falls through to insert a newline
            super.keyDown(with: event)
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
