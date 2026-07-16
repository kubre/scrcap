// CaptureEngine — ScreenCaptureKit wrapper behind the CaptureProviding seam.
// Per-display, per-window, region crop; Retina-scale aware.

import AppKit
import ScreenCaptureKit

struct CaptureResult {
    let image: CGImage
    /// Backing scale the image was captured at (2 on Retina).
    let scale: CGFloat
    let metadata: CaptureMetadata?

    init(image: CGImage, scale: CGFloat, metadata: CaptureMetadata? = nil) {
        self.image = image
        self.scale = scale
        self.metadata = metadata
    }
}

extension CaptureResult {
    func withMetadata(_ metadata: CaptureMetadata?) -> CaptureResult {
        CaptureResult(image: image, scale: scale, metadata: metadata)
    }
}

enum CaptureError: LocalizedError {
    case noPermission
    case displayNotFound
    case windowNotFound
    case cropFailed

    var errorDescription: String? {
        switch self {
        case .noPermission: return "Screen Recording permission is required."
        case .displayNotFound: return "Could not find the display to capture."
        case .windowNotFound: return "Could not find the window to capture."
        case .cropFailed: return "Failed to crop the captured image."
        }
    }
}

protocol CaptureProviding {
    func captureDisplay(screen: NSScreen) async throws -> CaptureResult
    func captureRegion(_ rect: NSRect, on screen: NSScreen) async throws -> CaptureResult
    func captureWindow(windowID: CGWindowID, includeShadow: Bool) async throws -> CaptureResult
}

final class CaptureEngine: CaptureProviding {
    /// Whether captures include the mouse pointer. Set from settings before use.
    var includeCursor = false
    /// Fill behind window captures (rounded corners / shadow are transparent).
    /// nil keeps the alpha; a color composites the shot onto it. Set before use.
    var windowBackground: NSColor? = .white

    func captureDisplay(screen: NSScreen) async throws -> CaptureResult {
        let displayID = GeometryMapper.displayID(of: screen)
        let content = try await shareableContent()
        guard let display = content.displays.first(where: { $0.displayID == displayID }) else {
            throw CaptureError.displayNotFound
        }
        // Exclude scrcap's own windows (overlay, editor) from the shot.
        let ourWindows = content.windows.filter {
            $0.owningApplication?.processID == pid_t(ProcessInfo.processInfo.processIdentifier)
        }
        let filter = SCContentFilter(display: display, excludingWindows: ourWindows)
        let scale = screen.backingScaleFactor
        let config = baseConfig()
        config.width = Int(CGFloat(display.width) * scale)
        config.height = Int(CGFloat(display.height) * scale)

        let image = try await SCScreenshotManager.captureImage(contentFilter: filter, configuration: config)
        return CaptureResult(image: image, scale: scale)
    }

    func captureRegion(_ rect: NSRect, on screen: NSScreen) async throws -> CaptureResult {
        let displayID = GeometryMapper.displayID(of: screen)
        let content = try await shareableContent()
        guard let display = content.displays.first(where: { $0.displayID == displayID }) else {
            throw CaptureError.displayNotFound
        }
        let sourceRect = GeometryMapper.displayLocalRect(fromCocoa: rect, on: screen)
            .intersection(CGRect(origin: .zero, size: screen.frame.size))
        guard !sourceRect.isEmpty else {
            throw CaptureError.cropFailed
        }
        let ourWindows = content.windows.filter {
            $0.owningApplication?.processID == pid_t(ProcessInfo.processInfo.processIdentifier)
        }
        let filter = SCContentFilter(display: display, excludingWindows: ourWindows)
        let scale = screen.backingScaleFactor
        let config = baseConfig()
        config.sourceRect = sourceRect
        config.width = max(1, Int((sourceRect.width * scale).rounded(.toNearestOrAwayFromZero)))
        config.height = max(1, Int((sourceRect.height * scale).rounded(.toNearestOrAwayFromZero)))

        let image = try await SCScreenshotManager.captureImage(contentFilter: filter, configuration: config)
        return CaptureResult(image: image, scale: scale)
    }

    func captureWindow(windowID: CGWindowID, includeShadow: Bool) async throws -> CaptureResult {
        let content = try await shareableContent()
        guard let window = content.windows.first(where: { $0.windowID == windowID }) else {
            throw CaptureError.windowNotFound
        }
        let filter = SCContentFilter(desktopIndependentWindow: window)
        let scale = CGFloat(filter.pointPixelScale)
        let config = baseConfig()
        // Always capture the clean window (no system shadow); we synthesize a
        // symmetric one so the margin is even on every side. The system shadow
        // is bottom-heavy, which reads as uneven/clipped padding once a
        // background is filled in.
        config.ignoreShadowsSingleWindow = true
        config.width = Int(filter.contentRect.width * scale)
        config.height = Int(filter.contentRect.height * scale)

        let image = try await SCScreenshotManager.captureImage(contentFilter: filter, configuration: config)
        return CaptureResult(image: framed(image, includeShadow: includeShadow, scale: scale), scale: scale)
    }

    private func rgbaContext(width: Int, height: Int) -> CGContext? {
        CGContext(
            data: nil, width: width, height: height,
            bitsPerComponent: 8, bytesPerRow: 0,
            space: CGColorSpace(name: CGColorSpace.sRGB)!,
            bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue
        )
    }

    /// Frames the clean window on `windowBackground` with a uniform margin and,
    /// when requested, a symmetric soft drop shadow — so every side matches.
    /// With a transparent background the shadow lives in the alpha channel.
    private func framed(_ window: CGImage, includeShadow: Bool, scale: CGFloat) -> CGImage {
        let pad = includeShadow ? Int((20 * scale).rounded()) : 0
        let canvasW = window.width + 2 * pad
        let canvasH = window.height + 2 * pad
        guard pad > 0, let ctx = rgbaContext(width: canvasW, height: canvasH) else {
            return composited(window) // no shadow → just fill the background
        }
        let rect = CGRect(x: pad, y: pad, width: window.width, height: window.height)
        if let background = windowBackground {
            ctx.setFillColor((background.usingColorSpace(.sRGB) ?? .white).cgColor)
            ctx.fill(CGRect(x: 0, y: 0, width: canvasW, height: canvasH))
        }
        ctx.setShadow(
            offset: .zero,
            blur: CGFloat(pad) * 0.55,
            color: NSColor.black.withAlphaComponent(0.30).cgColor
        )
        ctx.draw(window, in: rect)
        return ctx.makeImage() ?? window
    }

    /// Fills `windowBackground` behind the (rounded-corner) window. Returns the
    /// image unchanged when the background is transparent.
    private func composited(_ image: CGImage) -> CGImage {
        guard let background = windowBackground, let ctx = rgbaContext(width: image.width, height: image.height)
        else { return image }
        let rect = CGRect(x: 0, y: 0, width: image.width, height: image.height)
        ctx.setFillColor((background.usingColorSpace(.sRGB) ?? .white).cgColor)
        ctx.fill(rect)
        ctx.draw(image, in: rect)
        return ctx.makeImage() ?? image
    }

    private func shareableContent() async throws -> SCShareableContent {
        try await SCShareableContent.excludingDesktopWindows(false, onScreenWindowsOnly: true)
    }

    private func baseConfig() -> SCStreamConfiguration {
        let config = SCStreamConfiguration()
        config.showsCursor = includeCursor
        config.captureResolution = .best
        return config
    }
}
