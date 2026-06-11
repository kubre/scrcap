// Single owner of all point↔pixel and Cocoa↔CoreGraphics coordinate
// conversion (the classic Retina/multi-monitor trap — see plan §11).

import AppKit

enum GeometryMapper {
    /// Height of the primary screen — the anchor for flipping between
    /// Cocoa (bottom-left origin) and CG global (top-left origin) space.
    static var primaryHeight: CGFloat {
        NSScreen.screens.first?.frame.height ?? 0
    }

    /// Cocoa global rect → CG global rect (top-left origin).
    static func cgRect(fromCocoa rect: NSRect) -> CGRect {
        CGRect(x: rect.minX, y: primaryHeight - rect.maxY, width: rect.width, height: rect.height)
    }

    /// CG global rect → Cocoa global rect.
    static func cocoaRect(fromCG rect: CGRect) -> NSRect {
        NSRect(x: rect.minX, y: primaryHeight - rect.maxY, width: rect.width, height: rect.height)
    }

    /// A Cocoa global rect expressed in a display's local pixel space
    /// (top-left origin, scaled by backing factor) — what image cropping wants.
    static func pixelRect(fromCocoa rect: NSRect, on screen: NSScreen) -> CGRect {
        let scale = screen.backingScaleFactor
        let localX = rect.minX - screen.frame.minX
        let localTopY = screen.frame.maxY - rect.maxY
        return CGRect(
            x: localX * scale,
            y: localTopY * scale,
            width: rect.width * scale,
            height: rect.height * scale
        )
    }

    static func screenUnderMouse() -> NSScreen {
        let mouse = NSEvent.mouseLocation
        return NSScreen.screens.first { NSMouseInRect(mouse, $0.frame, false) }
            ?? NSScreen.main
            ?? NSScreen.screens[0]
    }

    static func displayID(of screen: NSScreen) -> CGDirectDisplayID {
        let key = NSDeviceDescriptionKey("NSScreenNumber")
        return screen.deviceDescription[key] as? CGDirectDisplayID ?? CGMainDisplayID()
    }
}
