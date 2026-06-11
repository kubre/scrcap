// ScrollCaptureController — scroll injection + settle detection + the
// StitchEngine row-hash aligner. Accessibility permission is requested lazily
// here, the first time scrolling capture is invoked (plan §02).

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
        let cgCenter = GeometryMapper.cgRect(fromCocoa: rect)
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
        let maxRows = Int(CGFloat(settingsStore.settings.scrollingMaxHeight) * scale)
        let scrollStepPoints = Int32(rect.height * 0.7)

        let first = try await frame(rect: rect, screen: screen)
        var accumulatedRows = first.rows
        var accumulatedHashes = first.hashes
        var previousFrameHashes = first.hashes
        let width = first.width
        var consecutiveNoNewRows = 0

        while !stopped, accumulatedHashes.count < maxRows {
            updateProgress(rows: accumulatedHashes.count, scale: scale)
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
            accumulatedHashes.append(contentsOf: next.hashes[alignment.newContentStart...])
            accumulatedRows.append(contentsOf: next.rows[alignment.newContentStart...])
        }

        // A single frame's worth of rows means nothing scrolled: deliver a
        // normal region capture instead (graceful bail, plan §06).
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

    private static func extractRows(from image: CGImage) -> FrameData {
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
        var rows: [[UInt8]] = []
        var hashes: [UInt64] = []
        rows.reserveCapacity(height)
        hashes.reserveCapacity(height)
        for y in 0..<height {
            let row = Array(buffer[(y * bytesPerRow)..<((y + 1) * bytesPerRow)])
            rows.append(row)
            hashes.append(row.withUnsafeBytes { StitchEngine.rowHash($0) })
        }
        return FrameData(rows: rows, hashes: hashes, width: width)
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
            let current = try await frame(rect: rect, screen: screen).hashes
            if let last = lastHashes, last == current { return }
            lastHashes = current
        }
    }

    // MARK: Progress UI

    private func showProgress(on screen: NSScreen) {
        let panel = NSPanel(
            contentRect: NSRect(x: 0, y: 0, width: 260, height: 64),
            styleMask: [.nonactivatingPanel, .hudWindow, .titled],
            backing: .buffered,
            defer: false
        )
        panel.title = "Scrolling Capture"
        panel.level = .screenSaver
        panel.isFloatingPanel = true

        let label = NSTextField(labelWithString: "Stitching… 0 px")
        label.frame = NSRect(x: 16, y: 22, width: 140, height: 20)
        let stop = NSButton(title: "Stop (Esc)", target: self, action: #selector(stopPressed))
        stop.frame = NSRect(x: 160, y: 16, width: 90, height: 30)
        stop.bezelStyle = .rounded
        panel.contentView?.addSubview(label)
        panel.contentView?.addSubview(stop)

        panel.setFrameOrigin(NSPoint(
            x: screen.visibleFrame.maxX - 290,
            y: screen.visibleFrame.maxY - 110
        ))
        panel.orderFrontRegardless()
        progressPanel = panel
        progressLabel = label
    }

    private func updateProgress(rows: Int, scale: CGFloat) {
        progressLabel?.stringValue = String(format: "Stitching… %d px", Int(CGFloat(rows) / scale))
    }

    @objc private func stopPressed() {
        stopped = true
    }

    private func teardown() {
        if let escMonitor { NSEvent.removeMonitor(escMonitor) }
        escMonitor = nil
        progressPanel?.orderOut(nil)
        progressPanel = nil
        progressLabel = nil
    }
}
