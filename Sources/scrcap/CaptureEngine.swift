// CaptureEngine — ScreenCaptureKit wrapper behind the CaptureProviding seam.
// Per-display, per-window, region crop; Retina-scale aware.

import AppKit
import ScreenCaptureKit

struct CaptureResult {
    let image: CGImage
    /// Backing scale the image was captured at (2 on Retina).
    let scale: CGFloat
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
        let full = try await captureDisplay(screen: screen)
        var crop = GeometryMapper.pixelRect(fromCocoa: rect, on: screen).integral
        // The captured image can differ a hair from the computed size; clamp.
        crop = crop.intersection(CGRect(x: 0, y: 0, width: full.image.width, height: full.image.height))
        guard !crop.isEmpty, let cropped = full.image.cropping(to: crop) else {
            throw CaptureError.cropFailed
        }
        return CaptureResult(image: cropped, scale: full.scale)
    }

    func captureWindow(windowID: CGWindowID, includeShadow: Bool) async throws -> CaptureResult {
        var imageOptions: CGWindowImageOption = [.bestResolution]
        if !includeShadow {
            imageOptions.insert(.boundsIgnoreFraming)
        }

        guard let image = Self.createWindowImage(windowID: windowID, options: imageOptions),
              image.width > 0, image.height > 0 else {
            throw CaptureError.windowNotFound
        }

        return CaptureResult(
            image: image,
            scale: Self.estimatedWindowScale(windowID: windowID, image: image)
        )
    }

    private func shareableContent() async throws -> SCShareableContent {
        try await SCShareableContent.excludingDesktopWindows(false, onScreenWindowsOnly: true)
    }

    private func baseConfig() -> SCStreamConfiguration {
        let config = SCStreamConfiguration()
        config.showsCursor = false
        config.captureResolution = .best
        return config
    }

    @available(macOS, deprecated: 14.0)
    private static func createWindowImage(windowID: CGWindowID, options: CGWindowImageOption) -> CGImage? {
        CGWindowListCreateImage(.null, [.optionIncludingWindow], windowID, options)
    }

    private static func estimatedWindowScale(windowID: CGWindowID, image: CGImage) -> CGFloat {
        guard let bounds = windowBounds(windowID), bounds.width > 0, bounds.height > 0 else {
            return NSScreen.main?.backingScaleFactor ?? 1
        }

        let estimate = max(CGFloat(image.width) / bounds.width, CGFloat(image.height) / bounds.height)
        let knownScales = Set(NSScreen.screens.map(\.backingScaleFactor) + [1, 2, 3])
        return knownScales.min { lhs, rhs in
            abs(lhs - estimate) < abs(rhs - estimate)
        } ?? max(1, estimate.rounded())
    }

    private static func windowBounds(_ windowID: CGWindowID) -> CGRect? {
        guard
            let entries = CGWindowListCopyWindowInfo(.optionIncludingWindow, windowID) as? [[String: Any]],
            let bounds = entries.first?[kCGWindowBounds as String] as? [String: Any],
            let x = cgFloat(bounds["X"]),
            let y = cgFloat(bounds["Y"]),
            let width = cgFloat(bounds["Width"]),
            let height = cgFloat(bounds["Height"])
        else {
            return nil
        }

        return CGRect(x: x, y: y, width: width, height: height)
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
