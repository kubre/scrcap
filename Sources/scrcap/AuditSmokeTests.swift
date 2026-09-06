#if DEBUG
import AppKit
import ScrcapCore

/// Native, synthetic-image regressions; no screen-recording permission required.
enum AuditSmokeTests {
    private struct Failure: Error, CustomStringConvertible {
        let description: String
    }

    static func run() {
        var passed = 0
        func check(_ name: String, _ body: () throws -> Void) throws {
            try body()
            passed += 1
            print("AUDIT PASS: \(name)")
        }
        do {
            let bitmap = try image(width: 32, height: 16)
            let shape = Shape(kind: .rectangle, colorIndex: 0,
                              start: CorePoint(x: 2, y: 2), end: CorePoint(x: 12, y: 12))
            try check("unannotated native-scale export does not allocate or repaint") {
                let output = try Exporter.flatten(bitmap: bitmap, shapes: [], palette: Settings.defaultPalette,
                                                  strokeWidth: 3, scale: 1, makeContext: { _, _, _ in
                    preconditionFailure("Native-scale fast path must not allocate a bitmap.")
                })
                try require(output === bitmap, "Fast path replaced the source image")
            }
            try check("annotated export fails closed when bitmap allocation fails") {
                do {
                    _ = try Exporter.flatten(bitmap: bitmap, shapes: [shape], palette: Settings.defaultPalette,
                                             strokeWidth: 3, scale: 1, makeContext: { _, _, _ in nil })
                    throw Failure(description: "Returned an image after annotation allocation failure")
                } catch ExportError.bitmapRenderFailed(let width, let height) {
                    try require(width == 32 && height == 16, "Wrong dimensions in render error")
                }
            }
            try check("scale conversion fails explicitly rather than exporting the wrong size") {
                do {
                    _ = try Exporter.flatten(bitmap: bitmap, shapes: [], palette: Settings.defaultPalette,
                                             strokeWidth: 3, scale: 2, exportScale: 1,
                                             makeContext: { _, _, _ in nil })
                    throw Failure(description: "Returned original image after scale allocation failure")
                } catch ExportError.bitmapRenderFailed(let width, let height) {
                    try require(width == 16 && height == 8, "Wrong target dimensions in scale error")
                }
            }
            try check("invalid scales cannot reach integer conversion or PNG metadata") {
                for scale: CGFloat in [0, -1, .nan, .infinity, .leastNonzeroMagnitude] {
                    do {
                        _ = try Exporter.flatten(bitmap: bitmap, shapes: [], palette: [], strokeWidth: 3,
                                                 scale: scale, exportScale: 1)
                        throw Failure(description: "Accepted invalid capture scale \(scale)")
                    } catch ExportError.invalidScale { }
                }
                try require(Exporter.pngData(bitmap, pointScale: .nan) == nil, "PNG accepted NaN DPI")
            }
            try check("successful downscale retains requested pixel dimensions") {
                let output = try Exporter.flatten(bitmap: bitmap, shapes: [], palette: [], strokeWidth: 3,
                                                  scale: 2, exportScale: 1)
                try require(output.width == 16 && output.height == 8, "Wrong scaled output dimensions")
            }
            try check("annotated export preserves the source RGB gamut") {
                let p3 = try image(width: 32, height: 16, space: CGColorSpace(name: CGColorSpace.displayP3)!)
                let output = try Exporter.flatten(bitmap: p3, shapes: [shape], palette: Settings.defaultPalette,
                                                  strokeWidth: 3, scale: 1)
                try require(output.colorSpace?.name == CGColorSpace.displayP3, "Display P3 was converted to sRGB")
            }
            try check("pixelation obscures a region when its source crop is unavailable") {
                let context = Exporter.bitmapContext(width: 32, height: 32, space: CGColorSpace(name: CGColorSpace.sRGB)!)!
                context.setFillColor(NSColor.white.cgColor)
                context.fill(CGRect(x: 0, y: 0, width: 32, height: 32))
                let bytes = context.data!.assumingMemoryBound(to: UInt8.self)
                let original = Array(UnsafeBufferPointer(start: bytes, count: context.bytesPerRow * 32))
                let nsContext = NSGraphicsContext(cgContext: context, flipped: false)
                NSGraphicsContext.saveGraphicsState()
                NSGraphicsContext.current = nsContext
                ShapeRenderer.draw(Shape(kind: .pixelate, colorIndex: 0,
                                          start: CorePoint(x: 20, y: 20), end: CorePoint(x: 28, y: 28)),
                                   palette: Settings.defaultPalette, strokeWidth: 3,
                                   sourceImage: try image(width: 16, height: 16))
                NSGraphicsContext.restoreGraphicsState()
                let output = Array(UnsafeBufferPointer(start: bytes, count: context.bytesPerRow * 32))
                try require(output != original, "Unavailable crop silently skipped redaction")
            }
            let directory = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
            defer { try? FileManager.default.removeItem(at: directory) }
            let store = SettingsStore(directory: directory)
            try require(store.update { $0.suppressCopyNotification = true }, "Cannot create isolated smoke settings")
            try check("4K shape-only snapshots retain all existing twenty undo slots") {
                // 4096 × 2160 × 4 = 35,389,440 image bytes: one image fits the
                // existing 256 MiB budget, twenty incorrectly counted copies do not.
                let capture = CaptureResult(image: try image(width: 4096, height: 2160), scale: 2)
                let editor = EditorWindowController(capture: capture, settingsStore: store)
                try require(editor.debugExerciseSharedImageHistory() == 20, "Shared image charged repeatedly to undo budget")
            }
            try check("save completion keeps the editor open after a newer edit") {
                let editor = EditorWindowController(capture: CaptureResult(image: bitmap, scale: 1), settingsStore: store)
                try require(editor.debugExerciseEditDuringSave(), "Saving an older snapshot discarded a newer edit")
            }
            print("AUDIT: \(passed) passed, 0 failed")
            exit(0)
        } catch {
            print("AUDIT FAIL after \(passed) passes: \(error)")
            exit(1)
        }
    }

    private static func require(_ condition: Bool, _ message: String) throws {
        if !condition { throw Failure(description: message) }
    }

    private static func image(width: Int, height: Int,
                              space: CGColorSpace = CGColorSpace(name: CGColorSpace.sRGB)!) throws -> CGImage {
        guard let context = Exporter.bitmapContext(width: width, height: height, space: space) else {
            throw Failure(description: "Could not create \(width) × \(height) test image")
        }
        context.setFillColor(NSColor.white.cgColor)
        context.fill(CGRect(x: 0, y: 0, width: width, height: height))
        guard let result = context.makeImage() else { throw Failure(description: "Could not snapshot test image") }
        return result
    }
}
#endif
