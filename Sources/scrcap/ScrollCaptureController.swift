// ScrollCaptureController — scroll injection + settle detection + the
// StitchEngine row-hash aligner. Accessibility permission is requested lazily
// here, the first time scrolling capture is invoked.

import AppKit
import ApplicationServices
import ScrcapCore

final class ScrollCaptureController {
    private let capture: CaptureProviding
    private let settingsStore: SettingsStore

    private var stopped = false
    private var progressPanel: NSPanel?
    private var progressLabel: NSTextField?
    private var escMonitor: Any?

    init(capture: CaptureProviding, settingsStore: SettingsStore) {
        self.capture = capture
        self.settingsStore = settingsStore
    }

    static func ensureAccessibility() -> Bool {
        if AXIsProcessTrusted() { return true }
        let options = [kAXTrustedCheckOptionPrompt.takeUnretainedValue() as String: true] as CFDictionary
        AXIsProcessTrustedWithOptions(options)
        return false
    }

    /// Runs the scroll-and-stitch loop over `rect` and returns the composite,
    /// or a plain capture of the region when the target turns out not to
    /// scroll. Calls `completion` on the main thread.
    func run(rect: NSRect, screen: NSScreen, completion: @escaping (Result<CaptureResult, Error>) -> Void) {
        stopped = false
        showProgress(on: screen)

        // Cursor must be over the region for scroll events to land there.
        let cgCenter = GeometryMapper.cgRect(fromCocoa: rect, on: screen)
        CGWarpMouseCursorPosition(CGPoint(x: cgCenter.midX, y: cgCenter.midY))

        escMonitor = NSEvent.addGlobalMonitorForEvents(matching: .keyDown) { [weak self] event in
            if event.keyCode == 53 { self?.stopped = true }
        }

        Task { @MainActor in
            defer { self.teardown() }
            do {
                let result = try await self.stitchLoop(rect: rect, screen: screen)
                completion(.success(result))
            } catch {
                completion(.failure(error))
            }
        }
    }

    @MainActor
    private func stitchLoop(rect: NSRect, screen: NSScreen) async throws -> CaptureResult {
        let scale = screen.backingScaleFactor
        let maxRows = settingsStore.settings.scrollingMaxHeight
        let scrollStepPoints = Int32(rect.height * 0.7)

        let firstCapture = try await capture.captureRegion(rect, on: screen)
        let first = Self.extractRows(from: firstCapture.image)
        var accumulatedRows = first.rows
        var accumulatedHashes = first.hashes
        var previousFrameHashes = first.hashes
        let width = first.width
        var consecutiveNoNewRows = 0

        while !stopped {
            let remainingRows = maxRows - accumulatedHashes.count
            guard remainingRows > 0 else { break }

            updateProgress(rows: accumulatedHashes.count)
            injectScroll(amount: scrollStepPoints)
            try await settle(rect: rect, screen: screen)
            guard !stopped else { break }

            var next = try await frame(rect: rect, screen: screen)
            guard next.width == width else { break }

            // Sticky header: rows identical at the same index in consecutive
            // frames didn't scroll — crop them from this frame before aligning.
            var fixedTop = 0
            while fixedTop < next.hashes.count,
                  fixedTop < previousFrameHashes.count,
                  next.hashes[fixedTop] == previousFrameHashes[fixedTop] {
                fixedTop += 1
            }
            previousFrameHashes = next.hashes
            if fixedTop > 0, fixedTop < Int(Double(next.hashes.count) * 0.9) {
                next.hashes.removeFirst(fixedTop)
                next.rows.removeFirst(fixedTop)
            } else if fixedTop >= Int(Double(next.hashes.count) * 0.9) {
                // Nothing moved at all — non-scrollable or bottom.
                consecutiveNoNewRows += 1
                if consecutiveNoNewRows >= 2 { break }
                continue
            }

            guard let alignment = StitchEngine.align(accumulated: accumulatedHashes, frame: next.hashes) else {
                // Content replaced wholesale (e.g. page navigation) — stop
                // with what we have rather than stitching garbage.
                break
            }

            if alignment.newContentStart >= next.hashes.count {
                consecutiveNoNewRows += 1
                if consecutiveNoNewRows >= 2 { break } // bottom reached
                continue
            }
            consecutiveNoNewRows = 0
            let availableRows = next.hashes.count - alignment.newContentStart
            guard availableRows > 0 else { continue }
            let appendedCount = min(availableRows, remainingRows)
            let end = alignment.newContentStart + appendedCount

            accumulatedHashes.append(contentsOf: next.hashes[alignment.newContentStart..<end])
            accumulatedRows.append(contentsOf: next.rows[alignment.newContentStart..<end])
            assert(accumulatedHashes.count == accumulatedRows.count, "Rows and hashes must stay aligned.")

            if appendedCount < availableRows {
                break
            }
        }

        // Nothing scrolled → hand back the pristine first capture (native
        // color space, no byte-buffer round trip) instead of compositing.
        if accumulatedHashes.count == first.hashes.count {
            return CaptureResult(image: firstCapture.image, scale: scale)
        }
        let image = try composite(rows: accumulatedRows, width: width)
        return CaptureResult(image: image, scale: scale)
    }

    // MARK: Frame capture & pixel access

    private struct FrameData {
        var rows: [[UInt8]]
        var hashes: [UInt64]
        var width: Int
    }

    @MainActor
    private func frame(rect: NSRect, screen: NSScreen) async throws -> FrameData {
        let result = try await capture.captureRegion(rect, on: screen)
        return Self.extractRows(from: result.image)
    }

    /// Hashes only — settle polling runs this several times per scroll step,
    /// so it must not copy every pixel row the way extractRows does.
    @MainActor
    private func frameHashes(rect: NSRect, screen: NSScreen) async throws -> [UInt64] {
        let result = try await capture.captureRegion(rect, on: screen)
        return Self.renderRows(from: result.image) { _, rowBytes, hashes in
            hashes.append(StitchEngine.rowHash(rowBytes))
        }
    }

    private static func extractRows(from image: CGImage) -> FrameData {
        var rows: [[UInt8]] = []
        rows.reserveCapacity(image.height)
        let hashes = renderRows(from: image) { _, rowBytes, hashes in
            rows.append([UInt8](rowBytes))
            hashes.append(StitchEngine.rowHash(rowBytes))
        }
        return FrameData(rows: rows, hashes: hashes, width: image.width)
    }

    /// Draws the image once into an RGBA buffer and walks it row by row.
    private static func renderRows(
        from image: CGImage,
        _ visit: (Int, UnsafeRawBufferPointer, inout [UInt64]) -> Void
    ) -> [UInt64] {
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
        var hashes: [UInt64] = []
        hashes.reserveCapacity(height)
        buffer.withUnsafeBytes { raw in
            for y in 0..<height {
                let row = UnsafeRawBufferPointer(rebasing: raw[(y * bytesPerRow)..<((y + 1) * bytesPerRow)])
                visit(y, row, &hashes)
            }
        }
        return hashes
    }

    private func composite(rows: [[UInt8]], width: Int) throws -> CGImage {
        let height = rows.count
        let bytesPerRow = width * 4
        var buffer = [UInt8]()
        buffer.reserveCapacity(bytesPerRow * height)
        for row in rows { buffer.append(contentsOf: row) }

        guard let provider = CGDataProvider(data: Data(buffer) as CFData),
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

    private func injectScroll(amount: Int32) {
        guard let event = CGEvent(
            scrollWheelEvent2Source: nil,
            units: .pixel,
            wheelCount: 1,
            wheel1: -amount,
            wheel2: 0,
            wheel3: 0
        ) else { return }
        event.post(tap: .cghidEventTap)
    }

    /// Waits for two consecutive identical frames (handles bounce,
    /// lazy-loading, animations) with a 600 ms timeout.
    @MainActor
    private func settle(rect: NSRect, screen: NSScreen) async throws {
        let deadline = Date().addingTimeInterval(0.6)
        var lastHashes: [UInt64]? = nil
        while Date() < deadline, !stopped {
            try await Task.sleep(nanoseconds: 120_000_000)
            let current = try await frameHashes(rect: rect, screen: screen)
            if let last = lastHashes, last == current { return }
            lastHashes = current
        }
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
        escMonitor = nil
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
