// Single owner of all point↔pixel and Cocoa↔CoreGraphics coordinate
// conversion for Retina and multi-monitor setups.

import AppKit

enum GeometryMapper {
    /// Cocoa global rect → Quartz global rect (top-left origin).
    static func quartzGlobalRect(fromCocoa rect: NSRect) -> CGRect {
        let top = NSScreen.screens.first(where: { displayID(of: $0) == CGMainDisplayID() })?.frame.maxY ?? 0
        return CGRect(
            x: rect.minX,
            y: top - rect.maxY,
            width: rect.width,
            height: rect.height
        )
    }

    /// Quartz global rect → Cocoa global rect.
    static func cocoaGlobalRect(fromQuartz rect: CGRect) -> NSRect {
        let top = NSScreen.screens.first(where: { displayID(of: $0) == CGMainDisplayID() })?.frame.maxY ?? 0
        return NSRect(
            x: rect.minX,
            y: top - rect.maxY,
            width: rect.width,
            height: rect.height
        )
    }

    /// Cocoa global rect expressed in display-local ScreenCaptureKit points.
    static func displayLocalRect(fromCocoa rect: NSRect, on screen: NSScreen) -> CGRect {
        CGRect(
            x: rect.minX - screen.frame.minX,
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
            ?? NSScreen.screens.first
            ?? { fatalError("scrcap: no connected display — cannot continue") }()
    }

    static func screen(containing point: NSPoint) -> NSScreen {
        NSScreen.screens.first { NSMouseInRect(point, $0.frame, false) }
            ?? NSScreen.main
            ?? NSScreen.screens.first
            ?? { fatalError("scrcap: no connected display — cannot continue") }()
    }

    static func displayID(of screen: NSScreen) -> CGDirectDisplayID {
        let key = NSDeviceDescriptionKey("NSScreenNumber")
        return screen.deviceDescription[key] as? CGDirectDisplayID ?? CGMainDisplayID()
    }
}
