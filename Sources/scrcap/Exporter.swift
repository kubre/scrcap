// Exporter — flatten bitmap + vectors at native scale, NSPasteboard (PNG +
// TIFF), file save via ImageIO, drag-out support.

import AppKit
import ImageIO
import UniformTypeIdentifiers
import ScrcapCore

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
        let pixelWidth = bitmap.width
        let pixelHeight = bitmap.height
        guard let ctx = CGContext(
            data: nil,
            width: pixelWidth,
            height: pixelHeight,
            bitsPerComponent: 8,
            bytesPerRow: 0,
            space: CGColorSpace(name: CGColorSpace.sRGB)!,
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
        let resolvedScale = exportScale == 1 ? 1.0 : 2.0
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
            space: CGColorSpace(name: CGColorSpace.sRGB)!,
            bitmapInfo: image.bitmapInfo.rawValue
        ) else {
            return image
        }

        context.interpolationQuality = .high
        context.draw(image, in: CGRect(x: 0, y: 0, width: targetWidth, height: targetHeight))
        return context.makeImage() ?? image
    }

    static func pngData(_ image: CGImage) -> Data? {
        let data = NSMutableData()
        guard let dest = CGImageDestinationCreateWithData(data, UTType.png.identifier as CFString, 1, nil) else {
            return nil
        }
        CGImageDestinationAddImage(dest, image, nil)
        guard CGImageDestinationFinalize(dest) else { return nil }
        return data as Data
    }

    @discardableResult
    static func copyToClipboard(_ image: CGImage) -> Bool {
        guard let png = pngData(image) else { return false }
        let rep = NSBitmapImageRep(cgImage: image)
        let pasteboard = NSPasteboard.general
        pasteboard.clearContents()
        pasteboard.declareTypes([.png, .tiff], owner: nil)
        pasteboard.setData(png, forType: .png)
        if let tiff = rep.tiffRepresentation {
            pasteboard.setData(tiff, forType: .tiff)
        }
        return true
    }

    @discardableResult
    static func writePNG(_ image: CGImage, to url: URL) -> Bool {
        guard let png = pngData(image) else { return false }
        return (try? png.write(to: url, options: .atomic)) != nil
    }

    /// Expands {date} and {time} tokens, e.g. "scrcap-2026-06-11-14.32.05".
    static func filename(pattern: String, now: Date = Date()) -> String {
        let date = DateFormatter()
        date.dateFormat = "yyyy-MM-dd"
        let time = DateFormatter()
        time.dateFormat = "HH.mm.ss"
        let expanded = pattern
            .replacingOccurrences(of: "{date}", with: date.string(from: now))
            .replacingOccurrences(of: "{time}", with: time.string(from: now))
        return safeFilenameStem(expanded) + ".png"
    }

    private static func safeFilenameStem(_ raw: String) -> String {
        let invalid = CharacterSet(charactersIn: "/:\\")
            .union(.controlCharacters)
            .union(.newlines)
        let parts = raw
            .components(separatedBy: invalid)
            .map { $0.trimmingCharacters(in: .whitespacesAndNewlines) }
            .filter { !$0.isEmpty }
        let stem = parts.joined(separator: "-")
        return stem.isEmpty ? "scrcap" : stem
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
    static func tempFileForDrag(_ image: CGImage, pattern: String) -> URL? {
        let url = FileManager.default.temporaryDirectory
            .appendingPathComponent(filename(pattern: pattern))
        return writePNG(image, to: url) ? url : nil
    }
}
