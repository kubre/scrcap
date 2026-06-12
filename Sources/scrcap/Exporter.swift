// Exporter — flatten bitmap + vectors at native scale, NSPasteboard (PNG +
// TIFF), file save via ImageIO, drag-out support.

import AppKit
import ImageIO
import UniformTypeIdentifiers
import ScrcapCore

enum ExportError: LocalizedError {
    case clipboardWriteFailed
    case pngWriteFailed(URL)

    var errorDescription: String? {
        switch self {
        case .clipboardWriteFailed:
            return "Could not write the screenshot to the clipboard."
        case .pngWriteFailed(let url):
            return "Could not save the PNG to \(url.path)."
        }
    }
}

enum Exporter {
    /// Flattens the bitmap and annotation vectors into a single CGImage at
    /// the image's native pixel scale (capture at 2×, annotate in points,
    /// export at 2×).
    static func flatten(
        bitmap: CGImage,
        shapes: [Shape],
        palette: [String],
        strokeWidth: CGFloat,
        scale: CGFloat,
        exportScale: Int = 2
    ) -> CGImage {
        // No annotations → no repaint. Keeps the capture's exact pixels and
        // color space (Retina captures are Display P3; redrawing into sRGB
        // would clamp them for nothing).
        if shapes.isEmpty {
            return scaledOutput(image: bitmap, captureScale: scale, exportScale: exportScale)
        }

        let pixelWidth = bitmap.width
        let pixelHeight = bitmap.height
        guard let ctx = CGContext(
            data: nil,
            width: pixelWidth,
            height: pixelHeight,
            bitsPerComponent: 8,
            bytesPerRow: 0,
            space: rgbColorSpace(of: bitmap),
            bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue
        ) else {
            return bitmap
        }

        ctx.draw(bitmap, in: CGRect(x: 0, y: 0, width: pixelWidth, height: pixelHeight))
        if !shapes.isEmpty {
            // Flip to top-left origin in point units so the renderer draws with
            // the exact same code path as the live canvas.
            ctx.translateBy(x: 0, y: CGFloat(pixelHeight))
            ctx.scaleBy(x: scale, y: -scale)

            let nsContext = NSGraphicsContext(cgContext: ctx, flipped: true)
            NSGraphicsContext.saveGraphicsState()
            NSGraphicsContext.current = nsContext
            ShapeRenderer.draw(shapes, palette: palette, strokeWidth: strokeWidth)
            NSGraphicsContext.restoreGraphicsState()
        }

        return scaledOutput(image: ctx.makeImage() ?? bitmap, captureScale: scale, exportScale: exportScale)
    }

    private static func scaledOutput(image: CGImage, captureScale: CGFloat, exportScale: Int) -> CGImage {
        let resolvedScale = min(exportScale == 1 ? 1.0 : 2.0, max(captureScale, 1.0))
        let ratio = resolvedScale / captureScale
        if ratio == 1 { return image }

        let targetWidth = max(1, Int((CGFloat(image.width) * ratio).rounded(.toNearestOrAwayFromZero)))
        let targetHeight = max(1, Int((CGFloat(image.height) * ratio).rounded(.toNearestOrAwayFromZero)))

        guard let context = CGContext(
            data: nil,
            width: targetWidth,
            height: targetHeight,
            bitsPerComponent: 8,
            bytesPerRow: 0,
            space: rgbColorSpace(of: image),
            bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue
        ) else {
            return image
        }

        context.interpolationQuality = .high
        context.draw(image, in: CGRect(x: 0, y: 0, width: targetWidth, height: targetHeight))
        return context.makeImage() ?? image
    }

    /// The image's own RGB color space (Display P3 for Retina captures), or
    /// sRGB when it has none / a non-RGB one.
    private static func rgbColorSpace(of image: CGImage) -> CGColorSpace {
        if let space = image.colorSpace, space.model == .rgb { return space }
        return CGColorSpace(name: CGColorSpace.sRGB)!
    }

    /// `pointScale` is pixels-per-point of the image (2 for Retina exports).
    /// Encoded as DPI so receivers display the screenshot at its real
    /// physical size — without it, every app treats a 2× capture as a
    /// double-size 72 DPI image and resamples it down, which reads as blur.
    static func pngData(_ image: CGImage, pointScale: CGFloat) -> Data? {
        let data = NSMutableData()
        guard let dest = CGImageDestinationCreateWithData(data, UTType.png.identifier as CFString, 1, nil) else {
            return nil
        }
        let dpi = 72.0 * max(pointScale, 1)
        let properties: [CFString: Any] = [
            kCGImagePropertyDPIWidth: dpi,
            kCGImagePropertyDPIHeight: dpi,
        ]
        CGImageDestinationAddImage(dest, image, properties as CFDictionary)
        guard CGImageDestinationFinalize(dest) else { return nil }
        return data as Data
    }

    @discardableResult
    static func copyToClipboard(_ image: CGImage, pointScale: CGFloat) -> Bool {
        guard let png = pngData(image, pointScale: pointScale) else { return false }
        let rep = NSBitmapImageRep(cgImage: image)
        // Point size ≠ pixel size on Retina; the TIFF must say so too or
        // TIFF-preferring apps (Notes, Mail, TextEdit) paste at double size.
        rep.size = NSSize(
            width: CGFloat(image.width) / max(pointScale, 1),
            height: CGFloat(image.height) / max(pointScale, 1)
        )
        let pasteboard = NSPasteboard.general
        pasteboard.clearContents()
        pasteboard.declareTypes([.png, .tiff], owner: nil)
        let wrotePNG = pasteboard.setData(png, forType: .png)
        if let tiff = rep.tiffRepresentation {
            pasteboard.setData(tiff, forType: .tiff)
        }
        return wrotePNG
    }

    @discardableResult
    static func writePNG(_ image: CGImage, pointScale: CGFloat, to url: URL) -> Bool {
        guard let png = pngData(image, pointScale: pointScale) else { return false }
        return (try? png.write(to: url, options: .atomic)) != nil
    }

    /// Expands {date} and {time} tokens, e.g. "scrcap-2026-06-11-14.32.05".
    static func filename(pattern: String, now: Date = Date()) -> String {
        FilenameGenerator.filename(pattern: pattern, now: now)
    }

    static func defaultSaveFolder(settings: Settings) -> URL {
        if let folder = settings.saveFolder {
            let expanded = (folder as NSString).expandingTildeInPath
            return URL(fileURLWithPath: expanded, isDirectory: true)
        }
        return FileManager.default.urls(for: .desktopDirectory, in: .userDomainMask).first
            ?? FileManager.default.homeDirectoryForCurrentUser
    }

    /// Temp PNG used as the payload for drag-out.
    static func tempFileForDrag(_ image: CGImage, pattern: String, pointScale: CGFloat) -> URL? {
        let url = FileManager.default.temporaryDirectory
            .appendingPathComponent(filename(pattern: pattern))
        return writePNG(image, pointScale: pointScale, to: url) ? url : nil
    }
}
