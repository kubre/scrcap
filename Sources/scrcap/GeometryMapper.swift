// Single owner of all point↔pixel and Cocoa↔CoreGraphics coordinate
// conversion (the classic Retina/multi-monitor trap — see plan §11).

import AppKit

enum GeometryMapper {
    static func cgRect(fromCocoa rect: NSRect) -> CGRect {
        cgRect(fromCocoa: rect, on: screenUnderMouse())
    }

    /// Backward-compatible overload for any callers that don't have an NSScreen
    /// handy. Uses the screen under the mouse, which is the prior default.
    static func cocoaRect(fromCG rect: CGRect) -> NSRect {
        cocoaRect(fromCG: rect, on: screenUnderMouse())
    }

    /// Cocoa global rect → CG global rect (top-left origin).
    static func cgRect(fromCocoa rect: NSRect, on screen: NSScreen) -> CGRect {
        CGRect(
            x: rect.minX - screen.frame.minX,
            y: screen.frame.maxY - rect.maxY,
            width: rect.width,
            height: rect.height
        )
    }

    /// CG global rect → Cocoa global rect.
    static func cocoaRect(fromCG rect: CGRect, on screen: NSScreen) -> NSRect {
        NSRect(
            x: rect.minX + screen.frame.minX,
            y: screen.frame.maxY - rect.maxY,
            width: rect.width,
            height: rect.height
        )
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

    static func screen(containing point: NSPoint) -> NSScreen {
        NSScreen.screens.first { NSMouseInRect(point, $0.frame, false) }
            ?? NSScreen.main
            ?? NSScreen.screens[0]
    }

    static func displayID(of screen: NSScreen) -> CGDirectDisplayID {
        let key = NSDeviceDescriptionKey("NSScreenNumber")
        return screen.deviceDescription[key] as? CGDirectDisplayID ?? CGMainDisplayID()
    }
}
