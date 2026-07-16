// OverlayController — borderless windows at .screenSaver level: dim mask,
// crosshair + live coordinates, drag select with Space-to-move, hovered-window
// highlight with Tab cycling. Windows are pre-created at launch (P1: showing
// a window is fast, creating one is not).

import AppKit

struct PickableWindow {
    let windowID: CGWindowID
    /// Cocoa global coordinates.
    let frame: NSRect
    let ownerName: String
    let ownerPID: Int?
    let name: String
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
    private var screenParametersObserver: NSObjectProtocol?

    var isActive: Bool { windows.contains { $0.isVisible } }

    init() {
        rebuildWindows()
        screenParametersObserver = NotificationCenter.default.addObserver(
            forName: NSApplication.didChangeScreenParametersNotification,
            object: nil, queue: .main
        ) { [weak self] _ in
            self?.rebuildWindows()
        }
    }

    deinit {
        if let screenParametersObserver {
            NotificationCenter.default.removeObserver(screenParametersObserver)
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
        for w in windows {
            w.stopAnimation()
            w.orderOut(nil)
        }
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
            let frame = GeometryMapper.cocoaGlobalRect(fromQuartz: cgRect)
            guard frame.isUsableWindowRect else { return nil }
            return PickableWindow(
                windowID: id,
                frame: frame,
                ownerName: owner,
                ownerPID: pid,
                name: name,
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

    func stopAnimation() {
        overlayView.stopAnimation()
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

    deinit {
        stopAnimation()
    }

    func reset(mode: OverlayController.Mode) {
        self.mode = mode
        dragAnchor = nil
        dragCurrent = nil
        spaceHeld = false
        hoverCandidates = []
        hoverIndex = 0
        stopAnimation()
        if mode == .window { refreshHover() }
        needsDisplay = true
    }

    /// Marching ants only animate while a selection drag is live — a 20 Hz
    /// full-screen redraw on every display is too expensive to run idle.
    private func startAnimationIfNeeded() {
        guard antsTimer == nil else { return }
        antsTimer = Timer.scheduledTimer(withTimeInterval: 1.0 / 20, repeats: true) { [weak self] _ in
            guard let self, self.window?.isVisible == true else { return }
            self.antsPhase += 1.4
            self.needsDisplay = true
        }
    }

    func stopAnimation() {
        antsTimer?.invalidate()
        antsTimer = nil
    }

    #if DEBUG
    /// Injects a fake drag selection for the UI snapshot smoke test.
    func debugSetSelection(from a: NSPoint, to b: NSPoint) {
        dragAnchor = a
        dragCurrent = b
    }
    #endif

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
            startAnimationIfNeeded()
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
        switch mode {
        case .region:
            drawRegionMode(ctx: ctx)
        case .window:
            drawWindowMode(ctx: ctx)
        }
    }

    private func drawRegionMode(ctx: CGContext) {
        Theme.overlayDim.setFill()
        if let rect = selectionRect {
            // Dim everything except the selection.
            let path = NSBezierPath(rect: bounds)
            path.append(NSBezierPath(rect: rect).reversed)
            path.fill()

            // Capture marching ants: red dashes over a contrasting base.
            // Square corner handles.
            Theme.strokeHazardAnts(rect.insetBy(dx: -1, dy: -1), phase: antsPhase, in: ctx)
            Theme.drawHazardHandles(rect)

            ThemeTag.draw(
                [.text(String(format: "%.0f × %.0f", rect.width, rect.height))],
                near: NSPoint(x: rect.midX, y: rect.maxY + 10),
                in: bounds
            )
            drawHintBelowSelection(rect, [("⎵", "move"), ("esc", "cancel")])
        } else {
            bounds.fill()
            // Footer last so the crosshair never slices through it.
            defer { drawHintFooter([("drag", "select"), ("⎵", "move"), ("esc", "cancel")]) }
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
            ThemeTag.draw(
                [.text(String(format: "%.0f, %.0f", mouse.x, bounds.height - mouse.y))],
                near: NSPoint(x: mouse.x + 14, y: mouse.y + 14),
                in: bounds,
                centered: false
            )
        }
    }

    private func drawWindowMode(ctx: CGContext) {
        Theme.overlayDim.setFill()
        // Footer last so the dim fills never wash it out.
        defer { drawHintFooter([("click", "capture"), ("⇥", "cycle overlapped"), ("esc", "cancel")]) }
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

        Theme.accent.withAlphaComponent(0.16).setFill()
        local.fill()
        Theme.strokeHazardAnts(local.insetBy(dx: 1, dy: 1), phase: antsPhase, in: ctx)

        var segments: [ThemeTag.Segment] = [.text(target.title)]
        if hoverCandidates.count > 1 {
            segments.append(.dim("  ⇥ \(hoverIndex + 1)/\(hoverCandidates.count)"))
        }
        ThemeTag.draw(segments, near: NSPoint(x: local.midX, y: local.maxY - 40), in: bounds)
    }

    /// Bottom-center key legend — the overlay teaches its own shortcuts.
    private func drawHintFooter(_ hints: [(key: String, label: String)]) {
        ThemeTag.draw(
            ThemeTag.legend(hints),
            near: NSPoint(x: bounds.midX, y: 28),
            in: bounds
        )
    }

    private func drawHintBelowSelection(_ rect: NSRect, _ hints: [(key: String, label: String)]) {
        ThemeTag.draw(
            ThemeTag.legend(hints),
            near: NSPoint(x: rect.midX, y: rect.minY - 38),
            in: bounds
        )
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
