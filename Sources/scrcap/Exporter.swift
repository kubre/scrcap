// Exporter — flatten bitmap + vectors at native scale, NSPasteboard (PNG +
// TIFF), file save via ImageIO, drag-out support.

import AppKit
import ImageIO
import UniformTypeIdentifiers
import ScrcapCore

enum ExportError: LocalizedError {
    case clipboardWriteFailed
    case pngEncodeFailed
    case invalidScale(CGFloat)
    case bitmapRenderFailed(width: Int, height: Int)

    var errorDescription: String? {
        switch self {
        case .clipboardWriteFailed:
            return "Could not write the screenshot to the clipboard."
        case .pngEncodeFailed:
            return "Could not encode the screenshot as PNG."
        case .invalidScale(let scale):
            return "Cannot export with capture scale=\(scale); expected a finite positive pixels-per-point value."
        case .bitmapRenderFailed(let width, let height):
            return "Could not render the \(width) × \(height) pixel export. No unannotated image was exported."
        }
    }
}

enum Exporter {
    private static let dragDirectoryName = "scrcap-drag"
    private static let dragDirectoryLock = NSLock()
    private static var preparedDragDirectory: URL?

    /// Flattens the bitmap and annotation vectors into a single CGImage at
    /// the image's native pixel scale (capture at 2×, annotate in points,
    /// export at 2×).
    static func flatten(
        bitmap: CGImage,
        shapes: [Shape],
        palette: [String],
        strokeWidth: CGFloat,
        scale: CGFloat,
        exportScale: Int = 2,
        makeContext: (Int, Int, CGColorSpace) -> CGContext? = bitmapContext
    ) throws -> CGImage {
        guard scale.isFinite, scale > 0 else { throw ExportError.invalidScale(scale) }
        // No annotations → no repaint. Keeps the capture's exact pixels and
        // color space (Retina captures are Display P3; redrawing into sRGB
        // would clamp them for nothing).
        if shapes.isEmpty {
            return try scaledOutput(image: bitmap, captureScale: scale, exportScale: exportScale, makeContext: makeContext)
        }

        let pixelWidth = bitmap.width
        let pixelHeight = bitmap.height
        guard let ctx = makeContext(pixelWidth, pixelHeight, rgbColorSpace(of: bitmap)) else {
            throw ExportError.bitmapRenderFailed(width: pixelWidth, height: pixelHeight)
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
            ShapeRenderer.draw(shapes, palette: palette, strokeWidth: strokeWidth, sourceImage: bitmap, sourceScale: scale)
            NSGraphicsContext.restoreGraphicsState()
        }

        guard let rendered = ctx.makeImage() else {
            throw ExportError.bitmapRenderFailed(width: pixelWidth, height: pixelHeight)
        }
        return try scaledOutput(image: rendered, captureScale: scale, exportScale: exportScale, makeContext: makeContext)
    }

    private static func scaledOutput(
        image: CGImage,
        captureScale: CGFloat,
        exportScale: Int,
        makeContext: (Int, Int, CGColorSpace) -> CGContext?
    ) throws -> CGImage {
        let resolvedScale = min(exportScale == 1 ? 1.0 : 2.0, max(captureScale, 1.0))
        let ratio = resolvedScale / captureScale
        if ratio == 1 { return image }

        guard let roundedWidth = Int(exactly: (CGFloat(image.width) * ratio).rounded(.toNearestOrAwayFromZero)),
              let roundedHeight = Int(exactly: (CGFloat(image.height) * ratio).rounded(.toNearestOrAwayFromZero))
        else { throw ExportError.invalidScale(captureScale) }
        let targetWidth = max(1, roundedWidth)
        let targetHeight = max(1, roundedHeight)

        guard let context = makeContext(targetWidth, targetHeight, rgbColorSpace(of: image)) else {
            throw ExportError.bitmapRenderFailed(width: targetWidth, height: targetHeight)
        }

        context.interpolationQuality = .high
        context.draw(image, in: CGRect(x: 0, y: 0, width: targetWidth, height: targetHeight))
        guard let scaled = context.makeImage() else {
            throw ExportError.bitmapRenderFailed(width: targetWidth, height: targetHeight)
        }
        return scaled
    }

    static func bitmapContext(width: Int, height: Int, space: CGColorSpace) -> CGContext? {
        CGContext(data: nil, width: width, height: height, bitsPerComponent: 8,
                  bytesPerRow: 0, space: space,
                  bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)
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
        guard pointScale.isFinite, pointScale > 0 else { return nil }
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
    static func copyToClipboard(
        _ image: CGImage,
        pointScale: CGFloat,
        metadata: CaptureMetadata? = nil
    ) -> Bool {
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

        let metadataJSON = metadata?.privacyScopedClipboardData(image: image, pointScale: pointScale)

        var types: [NSPasteboard.PasteboardType] = [.png, .tiff]
        if metadataJSON != nil {
            types.append(CaptureMetadata.pasteboardType)
        }
        pasteboard.declareTypes(types, owner: nil)

        let wrotePNG = pasteboard.setData(png, forType: .png)
        if let tiff = rep.tiffRepresentation {
            pasteboard.setData(tiff, forType: .tiff)
        }
        if let data = metadataJSON {
            pasteboard.setData(data, forType: CaptureMetadata.pasteboardType)
        }
        return wrotePNG
    }

    /// Throws so callers can surface the real OS reason (permission, disk full…).
    static func writePNG(_ image: CGImage, pointScale: CGFloat, to url: URL) throws {
        guard let png = pngData(image, pointScale: pointScale) else { throw ExportError.pngEncodeFailed }
        try png.write(to: url, options: .atomic)
    }

    /// Writes without replacing an existing file. The exclusive write closes
    /// the small race between choosing a name and creating it.
    static func writeUniquePNG(
        _ image: CGImage,
        pointScale: CGFloat,
        directory: URL,
        filename: String
    ) throws -> URL {
        guard let png = pngData(image, pointScale: pointScale) else { throw ExportError.pngEncodeFailed }
        return try UniqueFileWriter.write(png, directory: directory, filename: filename)
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
        let dir = prepareDragDirectory()

        let stem = (filename(pattern: pattern) as NSString).deletingPathExtension
        let url = dir.appendingPathComponent("\(stem)-\(UUID().uuidString).png")
        return (try? writePNG(image, pointScale: pointScale, to: url)) != nil ? url : nil
    }

    /// Called when the first editor is created. Leftovers from a terminated
    /// drag are removed once, while files from other open editors stay intact.
    @discardableResult
    static func prepareDragDirectory() -> URL {
        dragDirectoryLock.lock()
        defer { dragDirectoryLock.unlock() }
        if let preparedDragDirectory { return preparedDragDirectory }

        let fileManager = FileManager.default
        let dir = fileManager.temporaryDirectory.appendingPathComponent(dragDirectoryName, isDirectory: true)
        try? fileManager.createDirectory(
            at: dir,
            withIntermediateDirectories: true,
            attributes: [.posixPermissions: 0o700]
        )
        try? fileManager.setAttributes([.posixPermissions: 0o700], ofItemAtPath: dir.path)
        if let files = try? fileManager.contentsOfDirectory(at: dir, includingPropertiesForKeys: nil) {
            for file in files { try? fileManager.removeItem(at: file) }
        }
        preparedDragDirectory = dir
        return dir
    }
}
