// OverlayController — borderless windows at .screenSaver level: dim mask,
// crosshair + live coordinates, drag select with Space-to-move, hovered-window
// highlight with Tab cycling. Windows are pre-created at launch (P1: showing
// a window is fast, creating one is not).

import AppKit

struct PickableWindow {
    let windowID: CGWindowID
    /// Cocoa global coordinates.
    let frame: NSRect
    let title: String
}

final class OverlayController {
    enum Mode {
        case region
        case window
    }

    private var windows: [OverlayWindow] = []
    private var mode: Mode = .region
    private var onRegion: ((NSRect, NSScreen) -> Void)?
    private var onWindow: ((PickableWindow) -> Void)?
    private var pickableWindows: [PickableWindow] = []

    var isActive: Bool { windows.contains { $0.isVisible } }

    init() {
        rebuildWindows()
        NotificationCenter.default.addObserver(
            forName: NSApplication.didChangeScreenParametersNotification,
            object: nil, queue: .main
        ) { [weak self] _ in
            self?.rebuildWindows()
        }
    }

    private func rebuildWindows() {
        let wasVisible = isActive
        windows.forEach { $0.orderOut(nil) }
        windows = NSScreen.screens.map { screen in
            let w = OverlayWindow(screen: screen)
            w.overlayDelegate = self
            return w
        }
        if wasVisible { cancel() }
    }

    func beginRegionSelection(completion: @escaping (NSRect, NSScreen) -> Void) {
        guard !isActive else { return }
        mode = .region
        onRegion = completion
        present()
    }

    func beginWindowPick(completion: @escaping (PickableWindow) -> Void) {
        guard !isActive else { return }
        mode = .window
        onWindow = completion
        pickableWindows = Self.listWindows()
        present()
    }

    private func present() {
        NSApp.activate(ignoringOtherApps: true)
        for w in windows {
            w.prepare(mode: mode)
            w.orderFrontRegardless()
        }
        keyWindow(under: NSEvent.mouseLocation)?.makeKey()
        NSCursor.crosshair.set()
    }

    func cancel() {
        finish()
    }

    private func finish() {
        for w in windows { w.orderOut(nil) }
        onRegion = nil
        onWindow = nil
        NSCursor.arrow.set()
    }

    private func keyWindow(under point: NSPoint) -> OverlayWindow? {
        windows.first { NSMouseInRect(point, $0.frame, false) }
    }

    /// On-screen, layer-0 windows of other apps, front-to-back, in Cocoa coords.
    static func listWindows() -> [PickableWindow] {
        let options: CGWindowListOption = [.optionOnScreenOnly, .excludeDesktopElements]
        guard let info = CGWindowListCopyWindowInfo(options, kCGNullWindowID) as? [[String: Any]] else {
            return []
        }
        let ourPID = ProcessInfo.processInfo.processIdentifier
        return info.compactMap { entry in
            guard let layer = entry[kCGWindowLayer as String] as? Int, layer == 0,
                  let pid = entry[kCGWindowOwnerPID as String] as? Int, pid != ourPID,
                  let id = entry[kCGWindowNumber as String] as? CGWindowID,
                  let boundsDict = entry[kCGWindowBounds as String] as? [String: Any],
                  let x = cgFloat(boundsDict["X"]),
                  let y = cgFloat(boundsDict["Y"]),
                  let width = cgFloat(boundsDict["Width"]),
                  let height = cgFloat(boundsDict["Height"])
            else { return nil }
            let cgRect = CGRect(x: x, y: y, width: width, height: height)
            guard cgRect.isUsableWindowRect else { return nil }
            let owner = entry[kCGWindowOwnerName as String] as? String ?? ""
            let name = entry[kCGWindowName as String] as? String ?? ""
            let windowScreen = GeometryMapper.screen(containing: NSPoint(x: cgRect.midX, y: cgRect.midY))
            let frame = GeometryMapper.cocoaRect(fromCG: cgRect, on: windowScreen)
            guard frame.isUsableWindowRect else { return nil }
            return PickableWindow(
                windowID: id,
                frame: frame,
                title: name.isEmpty ? owner : "\(owner) — \(name)"
            )
        }
    }

    private static func cgFloat(_ value: Any?) -> CGFloat? {
        switch value {
        case let number as NSNumber:
            return CGFloat(truncating: number)
        case let value as CGFloat:
            return value
        case let value as Double:
            return CGFloat(value)
        case let value as Int:
            return CGFloat(value)
        default:
            return nil
        }
    }
}

// MARK: - Delegate surface for OverlayWindow

extension OverlayController: OverlayWindowDelegate {
    func overlayWindowsForHitTest() -> [PickableWindow] { pickableWindows }

    func overlayDidSelectRegion(_ rect: NSRect, on screen: NSScreen) {
        let callback = onRegion
        finish()
        if rect.width >= 2, rect.height >= 2 {
            callback?(rect, screen)
        }
    }

    func overlayDidPickWindow(_ window: PickableWindow) {
        let callback = onWindow
        finish()
        callback?(window)
    }

    func overlayDidCancel() {
        finish()
    }

    func overlayWantsKey(_ window: OverlayWindow) {
        if window.isVisible { window.makeKey() }
    }
}

// MARK: - Window

protocol OverlayWindowDelegate: AnyObject {
    func overlayWindowsForHitTest() -> [PickableWindow]
    func overlayDidSelectRegion(_ rect: NSRect, on screen: NSScreen)
    func overlayDidPickWindow(_ window: PickableWindow)
    func overlayDidCancel()
    func overlayWantsKey(_ window: OverlayWindow)
}

final class OverlayWindow: NSWindow {
    weak var overlayDelegate: OverlayWindowDelegate? {
        didSet { overlayView.controllerDelegate = overlayDelegate }
    }
    private let overlayView: OverlayView
    let targetScreen: NSScreen

    init(screen: NSScreen) {
        targetScreen = screen
        overlayView = OverlayView(frame: NSRect(origin: .zero, size: screen.frame.size), screen: screen)
        super.init(contentRect: screen.frame, styleMask: .borderless, backing: .buffered, defer: false)
        isOpaque = false
        backgroundColor = .clear
        hasShadow = false
        level = .screenSaver
        collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]
        acceptsMouseMovedEvents = true
        contentView = overlayView
        overlayView.hostWindow = self
    }

    override var canBecomeKey: Bool { true }

    func prepare(mode: OverlayController.Mode) {
        setFrame(targetScreen.frame, display: true)
        overlayView.reset(mode: mode)
    }

    override func keyDown(with event: NSEvent) {
        overlayView.handleKeyDown(event)
    }

    override func keyUp(with event: NSEvent) {
        overlayView.handleKeyUp(event)
    }

    override func cancelOperation(_ sender: Any?) {
        overlayDelegate?.overlayDidCancel()
    }
}

// MARK: - View

final class OverlayView: NSView {
    weak var controllerDelegate: OverlayWindowDelegate?
    weak var hostWindow: OverlayWindow?
    private let screen: NSScreen

    private var mode: OverlayController.Mode = .region

    // Region state (window-local coordinates).
    private var dragAnchor: NSPoint?
    private var dragCurrent: NSPoint?
    private var spaceHeld = false
    private var lastDragPoint: NSPoint?

    // Window-pick state.
    private var hoverCandidates: [PickableWindow] = []
    private var hoverIndex = 0
    private var antsPhase: CGFloat = 0
    private var antsTimer: Timer?

    init(frame: NSRect, screen: NSScreen) {
        self.screen = screen
        super.init(frame: frame)
        let tracking = NSTrackingArea(
            rect: .zero,
            options: [.mouseMoved, .mouseEnteredAndExited, .activeAlways, .inVisibleRect],
            owner: self
        )
        addTrackingArea(tracking)
    }

    required init?(coder: NSCoder) { fatalError() }

    func reset(mode: OverlayController.Mode) {
        self.mode = mode
        dragAnchor = nil
        dragCurrent = nil
        spaceHeld = false
        hoverCandidates = []
        hoverIndex = 0
        antsTimer?.invalidate()
        antsTimer = Timer.scheduledTimer(withTimeInterval: 1.0 / 20, repeats: true) { [weak self] _ in
            guard let self, self.window?.isVisible == true else { return }
            self.antsPhase += 1.4
            self.needsDisplay = true
        }
        if mode == .window { refreshHover() }
        needsDisplay = true
    }

    private var selectionRect: NSRect? {
        guard let a = dragAnchor, let c = dragCurrent else { return nil }
        return NSRect(
            x: min(a.x, c.x), y: min(a.y, c.y),
            width: abs(a.x - c.x), height: abs(a.y - c.y)
        )
    }

    // MARK: Mouse

    override func mouseEntered(with event: NSEvent) {
        if let hostWindow { controllerDelegate?.overlayWantsKey(hostWindow) }
        NSCursor.crosshair.set()
    }

    override func mouseMoved(with event: NSEvent) {
        if mode == .window { refreshHover() }
        needsDisplay = true
    }

    override func mouseDown(with event: NSEvent) {
        switch mode {
        case .region:
            dragAnchor = convert(event.locationInWindow, from: nil)
            dragCurrent = dragAnchor
            lastDragPoint = dragAnchor
        case .window:
            if hoverCandidates.indices.contains(hoverIndex) {
                controllerDelegate?.overlayDidPickWindow(hoverCandidates[hoverIndex])
            }
        }
    }

    override func mouseDragged(with event: NSEvent) {
        guard mode == .region else { return }
        let point = convert(event.locationInWindow, from: nil)
        if spaceHeld, let last = lastDragPoint, let anchor = dragAnchor {
            // Space mid-drag moves the whole selection.
            dragAnchor = NSPoint(x: anchor.x + point.x - last.x, y: anchor.y + point.y - last.y)
            if let current = dragCurrent {
                dragCurrent = NSPoint(x: current.x + point.x - last.x, y: current.y + point.y - last.y)
            }
        } else {
            dragCurrent = point
        }
        lastDragPoint = point
        needsDisplay = true
    }

    override func mouseUp(with event: NSEvent) {
        guard mode == .region, let rect = selectionRect else { return }
        // Window-local → Cocoa global.
        let global = NSRect(
            x: rect.minX + screen.frame.minX,
            y: rect.minY + screen.frame.minY,
            width: rect.width,
            height: rect.height
        )
        controllerDelegate?.overlayDidSelectRegion(global, on: screen)
    }

    // MARK: Keys

    func handleKeyDown(_ event: NSEvent) {
        switch event.keyCode {
        case 53: // esc
            controllerDelegate?.overlayDidCancel()
        case 49: // space
            spaceHeld = true
        case 48: // tab — cycle overlapping windows
            if mode == .window, hoverCandidates.count > 1 {
                hoverIndex = (hoverIndex + 1) % hoverCandidates.count
                needsDisplay = true
            }
        default:
            break
        }
    }

    func handleKeyUp(_ event: NSEvent) {
        if event.keyCode == 49 { spaceHeld = false }
    }

    private func refreshHover() {
        let mouse = NSEvent.mouseLocation
        let previous = hoverCandidates.indices.contains(hoverIndex) ? hoverCandidates[hoverIndex].windowID : nil
        hoverCandidates = (controllerDelegate?.overlayWindowsForHitTest() ?? [])
            .filter { NSMouseInRect(mouse, $0.frame, false) }
        if let previous, let kept = hoverCandidates.firstIndex(where: { $0.windowID == previous }) {
            hoverIndex = kept
        } else {
            hoverIndex = 0
        }
    }

    // MARK: Drawing

    override func draw(_ dirtyRect: NSRect) {
        guard let ctx = NSGraphicsContext.current?.cgContext else { return }
        let dim = NSColor.black.withAlphaComponent(0.35)

        switch mode {
        case .region:
            drawRegionMode(ctx: ctx, dim: dim)
        case .window:
            drawWindowMode(ctx: ctx, dim: dim)
        }
    }

    private func drawRegionMode(ctx: CGContext, dim: NSColor) {
        dim.setFill()
        if let rect = selectionRect {
            // Dim everything except the selection.
            let path = NSBezierPath(rect: bounds)
            path.append(NSBezierPath(rect: rect).reversed)
            path.fill()

            // Marching ants.
            ctx.saveGState()
            ctx.setStrokeColor(NSColor.systemRed.cgColor)
            ctx.setLineWidth(1.5)
            ctx.setLineDash(phase: antsPhase, lengths: [7, 5])
            ctx.stroke(rect.insetBy(dx: -0.75, dy: -0.75))
            ctx.restoreGState()

            drawLabel(
                String(format: "%.0f × %.0f", rect.width, rect.height),
                near: NSPoint(x: rect.midX, y: rect.maxY + 8)
            )
        } else {
            bounds.fill()
            // Crosshair with live coordinates.
            let mouse = convert(window?.mouseLocationOutsideOfEventStream ?? .zero, from: nil)
            guard bounds.contains(mouse) else { return }
            ctx.setStrokeColor(NSColor.white.withAlphaComponent(0.6).cgColor)
            ctx.setLineWidth(1)
            ctx.strokeLineSegments(between: [
                CGPoint(x: 0, y: mouse.y), CGPoint(x: bounds.width, y: mouse.y),
                CGPoint(x: mouse.x, y: 0), CGPoint(x: mouse.x, y: bounds.height),
            ])
            // Coordinates in screen-local, top-left-origin points.
            let coordText = String(format: "%.0f, %.0f", mouse.x, bounds.height - mouse.y)
            drawLabel(coordText, near: NSPoint(x: mouse.x + 14, y: mouse.y + 14), centered: false)
        }
    }

    private func drawWindowMode(ctx: CGContext, dim: NSColor) {
        dim.setFill()
        guard hoverCandidates.indices.contains(hoverIndex) else {
            bounds.fill()
            return
        }
        let target = hoverCandidates[hoverIndex]
        let local = NSRect(
            x: target.frame.minX - screen.frame.minX,
            y: target.frame.minY - screen.frame.minY,
            width: target.frame.width,
            height: target.frame.height
        ).intersection(bounds)
        guard local.isDrawableRect else {
            bounds.fill()
            return
        }

        let path = NSBezierPath(rect: bounds)
        path.append(NSBezierPath(rect: local).reversed)
        path.fill()

        NSColor.systemBlue.withAlphaComponent(0.18).setFill()
        local.fill()
        ctx.setStrokeColor(NSColor.systemBlue.cgColor)
        ctx.setLineWidth(2)
        ctx.stroke(local.insetBy(dx: 1, dy: 1))

        var label = target.title
        if hoverCandidates.count > 1 {
            label += "  (⇥ \(hoverIndex + 1)/\(hoverCandidates.count))"
        }
        drawLabel(label, near: NSPoint(x: local.midX, y: local.maxY - 28))
    }

    private func drawLabel(_ text: String, near point: NSPoint, centered: Bool = true) {
        let attrs: [NSAttributedString.Key: Any] = [
            .font: NSFont.monospacedSystemFont(ofSize: 11.5, weight: .medium),
            .foregroundColor: NSColor.white,
        ]
        let str = NSAttributedString(string: text, attributes: attrs)
        var size = str.size()
        size.width += 14
        size.height += 6
        var origin = centered
            ? NSPoint(x: point.x - size.width / 2, y: point.y)
            : point
        origin.x = max(4, min(origin.x, bounds.width - size.width - 4))
        origin.y = max(4, min(origin.y, bounds.height - size.height - 4))
        let box = NSRect(origin: origin, size: size)
        NSColor.black.withAlphaComponent(0.75).setFill()
        NSBezierPath(roundedRect: box, xRadius: 5, yRadius: 5).fill()
        str.draw(at: NSPoint(x: origin.x + 7, y: origin.y + 3))
    }
}

private extension CGRect {
    var isDrawableRect: Bool {
        [minX, minY, width, height].allSatisfy(\.isFinite) && width > 0 && height > 0
    }

    var isUsableWindowRect: Bool {
        [minX, minY, width, height].allSatisfy(\.isFinite) && width >= 40 && height >= 40
    }
}
