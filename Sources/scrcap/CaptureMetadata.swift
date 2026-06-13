import AppKit
import ScrcapCore

struct CaptureMetadata {
    static let pasteboardType = NSPasteboard.PasteboardType("com.scrcap.capture-metadata+json")

    let capturedAt = Date()
    let mode: CaptureMode

    static func fullscreen(on screen: NSScreen) -> CaptureMetadata {
        CaptureMetadata(mode: .fullscreen)
    }

    static func region(_ rect: NSRect, on screen: NSScreen, mode: CaptureMode = .region) -> CaptureMetadata {
        CaptureMetadata(mode: mode)
    }

    static func window(_ window: PickableWindow) -> CaptureMetadata {
        CaptureMetadata(mode: .window)
    }

    func privacyScopedClipboardData(image: CGImage, pointScale: CGFloat) -> Data? {
        let scale = max(pointScale, 1)
        let date = ISO8601DateFormatter().string(from: capturedAt)
        let pointWidth = Double(CGFloat(image.width) / scale)
        let pointHeight = Double(CGFloat(image.height) / scale)
        let json = """
        {
          "capturedAt" : "\(date)",
          "mode" : "\(mode.rawValue)",
          "outputImage" : {
            "pixelHeight" : \(image.height),
            "pixelWidth" : \(image.width),
            "pointHeight" : \(pointHeight),
            "pointScale" : \(Double(scale)),
            "pointWidth" : \(pointWidth)
          },
          "schema" : "com.scrcap.clipboard-metadata.v1"
        }
        """
        return json.data(using: .utf8)
    }
}
