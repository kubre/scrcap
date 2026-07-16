// ScrollCaptureController — scroll injection + settle detection + the
// StitchEngine row-hash aligner. Accessibility permission is requested lazily
// here, the first time scrolling capture is invoked.

import AppKit
import ApplicationServices
import ScrcapCore

enum ScrollingCaptureError: LocalizedError {
    case accessibilityPermissionRequired
    case noMovement
    case couldNotAlign

    var errorDescription: String? {
        switch self {
        case .accessibilityPermissionRequired:
            return "Accessibility permission was requested. Enable scrcap in System Settings, then try Scrolling Capture again."
        case .noMovement:
            return "The selected area did not scroll. Select the scrollable content area and make sure scrcap has Accessibility permission."
        case .couldNotAlign:
            return "The page scrolled, but its frames could not be aligned. Try a tighter selection around the scrolling content."
        }
    }
}

final class ScrollCaptureController {
    private static let maxAccumulatedPixelBytes = 256 * 1024 * 1024
    private static let scrollPulseCount = 2
    private static let scrollLinesPerPulse: Int32 = -4

    private let capture: CaptureProviding
    private let settingsStore: SettingsStore

    private var stopped = false
    private var progressPanel: NSPanel?
    private var progressLabel: NSTextField?
    private var escMonitor: Any?
    private var localEscMonitor: Any?

    init(capture: CaptureProviding, settingsStore: SettingsStore) {
        self.capture = capture
        self.settingsStore = settingsStore
    }

    static func ensureAccessibility() -> Bool {
        if AXIsProcessTrusted() { return true }
        let options = [kAXTrustedCheckOptionPrompt.takeUnretainedValue() as String: true] as CFDictionary
        return AXIsProcessTrustedWithOptions(options)
    }

    /// Runs the scroll-and-stitch loop over `rect` and returns the composite,
    /// or a plain capture of the region when the target turns out not to
    /// scroll. Calls `completion` on the main thread.
    func run(rect: NSRect, screen: NSScreen, completion: @escaping (Result<CaptureResult, Error>) -> Void) {
        stopped = false
        if let engine = capture as? CaptureEngine {
            engine.includeCursor = false
        }
        activateTargetApplication(under: NSPoint(x: rect.midX, y: rect.midY))

        let quartzRect = GeometryMapper.quartzGlobalRect(fromCocoa: rect)
        let scrollLocation = CGPoint(x: quartzRect.midX, y: quartzRect.midY)
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.15) { [weak self] in
            self?.start(rect: rect, screen: screen, scrollLocation: scrollLocation, completion: completion)
        }
    }

    private func start(
        rect: NSRect,
        screen: NSScreen,
        scrollLocation: CGPoint,
        completion: @escaping (Result<CaptureResult, Error>) -> Void
    ) {
        showProgress(on: screen)

        CGWarpMouseCursorPosition(scrollLocation)
        if let move = CGEvent(
            mouseEventSource: nil,
            mouseType: .mouseMoved,
            mouseCursorPosition: scrollLocation,
            mouseButton: .left
        ) {
            move.post(tap: .cghidEventTap)
        }

        escMonitor = NSEvent.addGlobalMonitorForEvents(matching: .keyDown) { [weak self] event in
            if event.keyCode == 53 { self?.stopped = true }
        }
        localEscMonitor = NSEvent.addLocalMonitorForEvents(matching: .keyDown) { [weak self] event in
            guard event.keyCode == 53 else { return event }
            self?.stopped = true
            return nil
        }

        Task { @MainActor in
            defer { self.teardown() }
            do {
                let result = try await self.stitchLoop(rect: rect, screen: screen, scrollLocation: scrollLocation)
                completion(.success(result))
            } catch {
                completion(.failure(error))
            }
        }
    }

    @MainActor
    private func stitchLoop(rect: NSRect, screen: NSScreen, scrollLocation: CGPoint) async throws -> CaptureResult {
        let scale = screen.backingScaleFactor

        let firstCapture = try await capture.captureRegion(rect, on: screen)
        let first = await Self.extractRowsOffMain(from: firstCapture.image)
        let maxRows = Self.effectiveMaxRows(
            configuredMaxRows: settingsStore.settings.scrollingMaxHeight,
            initialRows: first.signatures.count,
            bytesPerRow: first.bytesPerRow,
            frameBytes: first.bytes.count
        )
        var accumulatedBytes = first.bytes
        var accumulatedSignatures = first.signatures
        var previousFullHashes = first.hashes
        var previousFullSignatures = first.signatures
        var fixedTopBytes: [UInt8] = []
        var fixedBottomBytes: [UInt8] = []
        var fixedTop = 0
        var fixedBottom = 0
        var fixedEdgesResolved = false
        let width = first.width
        var consecutiveNoNewRows = 0
        var appendedAnyRows = false

        while !stopped {
            let totalRows = fixedTop + accumulatedSignatures.count + fixedBottom
            let remainingRows = maxRows - totalRows
            guard remainingRows > 0 else { break }

            updateProgress(rows: totalRows)
            await injectScroll(at: scrollLocation)
            try await settle(rect: rect, screen: screen)
            guard !stopped else { break }

            var next = try await frame(rect: rect, screen: screen)
            guard next.width == width else { break }

            let frameUnchanged = next.hashes == previousFullHashes
            let edges = await Self.fixedEdgesOffMain(previousFullSignatures, next.signatures)
            previousFullHashes = next.hashes
            previousFullSignatures = next.signatures
            if frameUnchanged {
                consecutiveNoNewRows += 1
                if consecutiveNoNewRows >= 2 {
                    if appendedAnyRows { break }
                    throw ScrollingCaptureError.noMovement
                }
                continue
            }

            if !fixedEdgesResolved {
                fixedEdgesResolved = true
                fixedTop = edges.top
                fixedBottom = edges.bottom
                if fixedTop + fixedBottom < first.signatures.count {
                    fixedTopBytes = Array(first.bytes.prefix(fixedTop * first.bytesPerRow))
                    fixedBottomBytes = Array(first.bytes.suffix(fixedBottom * first.bytesPerRow))
                    let bodyStart = fixedTop
                    let bodyEnd = first.signatures.count - fixedBottom
                    accumulatedSignatures = Array(first.signatures[bodyStart..<bodyEnd])
                    accumulatedBytes = Array(first.bytes[(bodyStart * first.bytesPerRow)..<(bodyEnd * first.bytesPerRow)])
                }
            }

            if fixedTop + fixedBottom >= next.signatures.count { continue }
            if fixedBottom > 0 {
                fixedBottomBytes = Array(next.bytes.suffix(fixedBottom * next.bytesPerRow))
            }
            let bodyEnd = next.signatures.count - fixedBottom
            next.signatures = Array(next.signatures[fixedTop..<bodyEnd])
            next.bytes = Array(next.bytes[(fixedTop * next.bytesPerRow)..<(bodyEnd * next.bytesPerRow)])

            var alignment = await Self.alignOffMain(accumulatedSignatures, next.signatures)
            if alignment == nil {
                try await Task.sleep(nanoseconds: 180_000_000)
                var retry = try await frame(rect: rect, screen: screen)
                if retry.width == width, fixedTop + fixedBottom < retry.signatures.count {
                    if fixedBottom > 0 {
                        fixedBottomBytes = Array(retry.bytes.suffix(fixedBottom * retry.bytesPerRow))
                    }
                    let retryBodyEnd = retry.signatures.count - fixedBottom
                    retry.signatures = Array(retry.signatures[fixedTop..<retryBodyEnd])
                    retry.bytes = Array(
                        retry.bytes[(fixedTop * retry.bytesPerRow)..<(retryBodyEnd * retry.bytesPerRow)]
                    )
                    if let retryAlignment = await Self.alignOffMain(accumulatedSignatures, retry.signatures) {
                        next = retry
                        alignment = retryAlignment
                    }
                }
            }
            guard let alignment else {
                if appendedAnyRows { break }
                throw ScrollingCaptureError.couldNotAlign
            }

            if alignment.newContentStart >= next.signatures.count {
                consecutiveNoNewRows += 1
                if consecutiveNoNewRows >= 2 { break } // bottom reached
                continue
            }
            consecutiveNoNewRows = 0
            let availableRows = next.signatures.count - alignment.newContentStart
            guard availableRows > 0 else { continue }
            let appendedCount = min(availableRows, remainingRows)
            let end = alignment.newContentStart + appendedCount

            accumulatedSignatures.append(contentsOf: next.signatures[alignment.newContentStart..<end])
            let startByte = alignment.newContentStart * next.bytesPerRow
            let endByte = end * next.bytesPerRow
            accumulatedBytes.append(contentsOf: next.bytes[startByte..<endByte])
            appendedAnyRows = true
            assert(
                accumulatedSignatures.count * first.bytesPerRow == accumulatedBytes.count,
                "Rows and bytes must stay aligned."
            )

            if appendedCount < availableRows {
                break
            }
        }

        // Nothing scrolled → hand back the pristine first capture (native
        // color space, no byte-buffer round trip) instead of compositing.
        if !appendedAnyRows {
            return CaptureResult(image: firstCapture.image, scale: scale)
        }
        let chunks = [fixedTopBytes, accumulatedBytes, fixedBottomBytes]
        let image = try await Self.compositeOffMain(chunks: chunks, width: width)
        return CaptureResult(image: image, scale: scale)
    }

    // MARK: Frame capture & pixel access

    private struct FrameData {
        var bytes: [UInt8]
        var hashes: [UInt64]
        var signatures: [StitchEngine.RowSignature]
        var width: Int
        var bytesPerRow: Int
    }

    @MainActor
    private func frame(rect: NSRect, screen: NSScreen) async throws -> FrameData {
        let result = try await capture.captureRegion(rect, on: screen)
        return await Self.extractRowsOffMain(from: result.image)
    }

    /// Signatures only — settle polling runs this several times per scroll step,
    /// so it must not copy every pixel row the way extractRows does.
    @MainActor
    private func frameSignatures(
        rect: NSRect,
        screen: NSScreen
    ) async throws -> [StitchEngine.RowSignature] {
        let result = try await capture.captureRegion(rect, on: screen)
        return await Self.signaturesOffMain(from: result.image)
    }

    private static func extractRows(from image: CGImage) -> FrameData {
        let bytesPerRow = image.width * 4
        var bytes: [UInt8] = []
        bytes.reserveCapacity(bytesPerRow * image.height)
        var hashes: [UInt64] = []
        let signatures = renderRows(from: image) { _, rowBytes, signatures in
            bytes.append(contentsOf: rowBytes)
            hashes.append(StitchEngine.rowHash(rowBytes))
            signatures.append(StitchEngine.rowSignature(rowBytes))
        }
        return FrameData(
            bytes: bytes,
            hashes: hashes,
            signatures: signatures,
            width: image.width,
            bytesPerRow: bytesPerRow
        )
    }

    private static func effectiveMaxRows(
        configuredMaxRows: Int,
        initialRows: Int,
        bytesPerRow: Int,
        frameBytes: Int
    ) -> Int {
        guard bytesPerRow > 0 else { return configuredMaxRows }
        // At the peak we can hold a captured frame, extracted frame bytes,
        // accumulated rows, the final contiguous buffer, and its Data copy.
        let overhead = min(maxAccumulatedPixelBytes, frameBytes * 2)
        let outputBudget = max(frameBytes, (maxAccumulatedPixelBytes - overhead) / 3)
        let memoryLimitedRows = max(initialRows, outputBudget / bytesPerRow)
        return max(initialRows, min(configuredMaxRows, memoryLimitedRows))
    }

    private static func extractRowsOffMain(from image: CGImage) async -> FrameData {
        await Task.detached(priority: .userInitiated) { extractRows(from: image) }.value
    }

    private static func signaturesOffMain(
        from image: CGImage
    ) async -> [StitchEngine.RowSignature] {
        await Task.detached(priority: .userInitiated) {
            renderRows(from: image) { _, rowBytes, signatures in
                signatures.append(StitchEngine.rowSignature(rowBytes))
            }
        }.value
    }

    private static func fixedEdgesOffMain(
        _ previous: [StitchEngine.RowSignature],
        _ next: [StitchEngine.RowSignature]
    ) async -> (top: Int, bottom: Int) {
        await Task.detached(priority: .userInitiated) {
            StitchEngine.fixedEdges(frames: [previous, next])
        }.value
    }

    private static func alignOffMain(
        _ accumulated: [StitchEngine.RowSignature],
        _ frame: [StitchEngine.RowSignature]
    ) async -> StitchEngine.Alignment? {
        await Task.detached(priority: .userInitiated) {
            StitchEngine.align(accumulated: accumulated, frame: frame)
        }.value
    }

    /// Draws the image once into an RGBA buffer and walks it row by row.
    private static func renderRows<Value>(
        from image: CGImage,
        _ visit: (Int, UnsafeRawBufferPointer, inout [Value]) -> Void
    ) -> [Value] {
        let width = image.width
        let height = image.height
        let bytesPerRow = width * 4
        var buffer = [UInt8](repeating: 0, count: bytesPerRow * height)
        buffer.withUnsafeMutableBytes { ptr in
            guard let ctx = CGContext(
                data: ptr.baseAddress,
                width: width, height: height,
                bitsPerComponent: 8, bytesPerRow: bytesPerRow,
                space: CGColorSpace(name: CGColorSpace.sRGB)!,
                bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue
            ) else { return }
            ctx.draw(image, in: CGRect(x: 0, y: 0, width: width, height: height))
        }
        var values: [Value] = []
        values.reserveCapacity(height)
        buffer.withUnsafeBytes { raw in
            for y in 0..<height {
                let row = UnsafeRawBufferPointer(rebasing: raw[(y * bytesPerRow)..<((y + 1) * bytesPerRow)])
                visit(y, row, &values)
            }
        }
        return values
    }

    private static func compositeOffMain(chunks: [[UInt8]], width: Int) async throws -> CGImage {
        try await Task.detached(priority: .userInitiated) {
            try composite(chunks: chunks, width: width)
        }.value
    }

    private static func composite(chunks: [[UInt8]], width: Int) throws -> CGImage {
        var bytes: [UInt8] = []
        bytes.reserveCapacity(chunks.reduce(0) { $0 + $1.count })
        for chunk in chunks { bytes.append(contentsOf: chunk) }
        let bytesPerRow = width * 4
        guard bytesPerRow > 0, bytes.count.isMultiple(of: bytesPerRow) else {
            throw CaptureError.cropFailed
        }
        let height = bytes.count / bytesPerRow

        guard let provider = CGDataProvider(data: Data(bytes) as CFData),
              let image = CGImage(
                  width: width, height: height,
                  bitsPerComponent: 8, bitsPerPixel: 32, bytesPerRow: bytesPerRow,
                  space: CGColorSpace(name: CGColorSpace.sRGB)!,
                  bitmapInfo: CGBitmapInfo(rawValue: CGImageAlphaInfo.premultipliedLast.rawValue),
                  provider: provider, decode: nil, shouldInterpolate: false, intent: .defaultIntent
              )
        else {
            throw CaptureError.cropFailed
        }
        return image
    }

    // MARK: Scroll injection & settling

    private func injectScroll(at location: CGPoint) async {
        let source = CGEventSource(stateID: .hidSystemState)
        for pulse in 0..<Self.scrollPulseCount {
            guard let event = CGEvent(
                scrollWheelEvent2Source: source,
                units: .line,
                wheelCount: 1,
                wheel1: Self.scrollLinesPerPulse,
                wheel2: 0,
                wheel3: 0
            ) else { return }
            event.location = location
            event.post(tap: .cghidEventTap)
            if pulse + 1 < Self.scrollPulseCount {
                try? await Task.sleep(nanoseconds: 25_000_000)
            }
        }
    }

    /// Waits for two nearly identical frames so small animations and carets do
    /// not keep a scrolling capture permanently unsettled.
    @MainActor
    private func settle(rect: NSRect, screen: NSScreen) async throws {
        let deadline = Date().addingTimeInterval(1.2)
        var lastSignatures: [StitchEngine.RowSignature]? = nil
        while Date() < deadline, !stopped {
            try await Task.sleep(nanoseconds: 120_000_000)
            let current = try await frameSignatures(rect: rect, screen: screen)
            if let last = lastSignatures, StitchEngine.similarity(last, current) >= 0.90 { return }
            lastSignatures = current
        }
    }

    private func activateTargetApplication(under point: NSPoint) {
        guard let pid = OverlayController.listWindows()
            .first(where: { NSMouseInRect(point, $0.frame, false) })?
            .ownerPID
        else { return }
        NSRunningApplication(processIdentifier: pid_t(pid))?
            .activate(options: [])
    }

    // MARK: Progress UI

    private func showProgress(on screen: NSScreen) {
        let size = NSSize(width: 252, height: 64)
        let panel = NSPanel(
            contentRect: NSRect(origin: .zero, size: size),
            styleMask: [.nonactivatingPanel, .borderless],
            backing: .buffered,
            defer: false
        )
        panel.level = .screenSaver
        panel.isFloatingPanel = true
        panel.isOpaque = false
        panel.backgroundColor = .clear
        panel.hasShadow = true

        let hud = ScrollProgressHUD(stopTarget: self, stopAction: #selector(stopPressed))
        hud.frame = NSRect(origin: .zero, size: size)
        panel.contentView = hud

        panel.setFrameOrigin(NSPoint(
            x: screen.visibleFrame.maxX - size.width - 20,
            y: screen.visibleFrame.maxY - size.height - 20
        ))
        panel.orderFrontRegardless()
        progressPanel = panel
        progressLabel = hud.countLabel
    }

    private func updateProgress(rows: Int) {
        progressLabel?.stringValue = String(format: "%d px", rows)
    }

    @objc private func stopPressed() {
        stopped = true
    }

    #if DEBUG
    /// Offscreen HUD instance for the UI snapshot smoke test.
    static func debugHUDView() -> NSView {
        let hud = ScrollProgressHUD(stopTarget: NSApp, stopAction: #selector(NSApplication.terminate(_:)))
        hud.frame = NSRect(x: 0, y: 0, width: 252, height: 64)
        hud.countLabel.stringValue = "1240 px"
        return hud
    }
    #endif

    private func teardown() {
        if let escMonitor { NSEvent.removeMonitor(escMonitor) }
        if let localEscMonitor { NSEvent.removeMonitor(localEscMonitor) }
        escMonitor = nil
        localEscMonitor = nil
        progressPanel?.orderOut(nil)
        progressPanel = nil
        progressLabel = nil
    }
}

// MARK: - HUD view

/// Fixed-carbon capture HUD (matches the overlay's tag styling): flat block,
/// sharp corners, pulsing red dot, mono pixel count, red STOP cell.
private final class ScrollProgressHUD: NSView {
    let countLabel = NSTextField(labelWithString: "0 px")

    private let dot = NSView()

    init(stopTarget: AnyObject, stopAction: Selector) {
        super.init(frame: .zero)
        wantsLayer = true
        layer?.backgroundColor = Theme.tagBackground.cgColor
        layer?.borderWidth = 1
        layer?.borderColor = Theme.tagRule.cgColor

        dot.wantsLayer = true
        dot.layer?.backgroundColor = Theme.accent.cgColor
        dot.translatesAutoresizingMaskIntoConstraints = false

        let title = NSTextField(labelWithString: "SCROLLING CAPTURE")
        title.font = NSFont.monospacedSystemFont(ofSize: 10, weight: .semibold)
        title.textColor = Theme.tagDimText
        title.translatesAutoresizingMaskIntoConstraints = false

        countLabel.font = NSFont.monospacedSystemFont(ofSize: 16, weight: .medium)
        countLabel.textColor = Theme.tagText
        countLabel.translatesAutoresizingMaskIntoConstraints = false

        let stop = NSButton(title: "", target: stopTarget, action: stopAction)
        stop.isBordered = false
        stop.wantsLayer = true
        stop.layer?.backgroundColor = Theme.accent.cgColor
        let stopTitle = NSMutableAttributedString(string: "STOP ", attributes: [
            .font: NSFont.monospacedSystemFont(ofSize: 11, weight: .semibold),
            .foregroundColor: Theme.onAccent,
        ])
        stopTitle.append(NSAttributedString(string: "ESC", attributes: [
            .font: NSFont.monospacedSystemFont(ofSize: 10, weight: .medium),
            .foregroundColor: Theme.onAccent.withAlphaComponent(0.70),
        ]))
        stop.attributedTitle = stopTitle
        stop.translatesAutoresizingMaskIntoConstraints = false

        addSubview(dot)
        addSubview(title)
        addSubview(countLabel)
        addSubview(stop)
        NSLayoutConstraint.activate([
            dot.leadingAnchor.constraint(equalTo: leadingAnchor, constant: 14),
            dot.centerYAnchor.constraint(equalTo: title.centerYAnchor),
            dot.widthAnchor.constraint(equalToConstant: 8),
            dot.heightAnchor.constraint(equalToConstant: 8),
            title.leadingAnchor.constraint(equalTo: dot.trailingAnchor, constant: 7),
            title.topAnchor.constraint(equalTo: topAnchor, constant: 12),
            countLabel.leadingAnchor.constraint(equalTo: leadingAnchor, constant: 14),
            countLabel.topAnchor.constraint(equalTo: title.bottomAnchor, constant: 3),
            stop.trailingAnchor.constraint(equalTo: trailingAnchor, constant: -12),
            stop.centerYAnchor.constraint(equalTo: centerYAnchor),
            stop.widthAnchor.constraint(equalToConstant: 78),
            stop.heightAnchor.constraint(equalToConstant: 26),
        ])
    }

    required init?(coder: NSCoder) { fatalError() }

    override func viewDidMoveToWindow() {
        super.viewDidMoveToWindow()
        guard window != nil, dot.layer?.animation(forKey: "pulse") == nil else { return }
        let pulse = CABasicAnimation(keyPath: "opacity")
        pulse.fromValue = 1.0
        pulse.toValue = 0.3
        pulse.duration = 0.8
        pulse.autoreverses = true
        pulse.repeatCount = .infinity
        dot.layer?.add(pulse, forKey: "pulse")
    }
}
