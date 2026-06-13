// Theme — scrcap's app-native design language: warm graphite chrome, a fixed
// red capture accent, compact system text, and monospaced shortcuts/readouts.
// The editor should feel like a fast instrument without copying the website.
//
// Chrome follows the system appearance. Anything drawn over arbitrary screen
// content (overlay tags, the scroll HUD) uses the fixed carbon style.

import AppKit

enum Theme {
    // MARK: Core palette

    /// Fixed product accent. Red is intentionally not appearance-following.
    static let accent = NSColor(srgbRed: 0.890, green: 0.055, blue: 0.125, alpha: 1) // #E30E20
    static let accentDeep = NSColor(srgbRed: 0.660, green: 0.020, blue: 0.075, alpha: 1)
    /// Content on a red block is always white, both appearances.
    static let onAccent = NSColor.white

    // MARK: Chrome (appearance-following: enamel ↔ carbon inversion)

    /// Chrome surface — neutral macOS-style gray (light) / graphite (dark).
    static let chrome = NSColor(name: nil) { appearance in
        appearance.isDark
            ? NSColor(srgbRed: 0.105, green: 0.107, blue: 0.110, alpha: 1)
            : NSColor(srgbRed: 0.948, green: 0.949, blue: 0.952, alpha: 1)
    }
    /// The canvas sheet the screenshot sits on — white in light, near-black in dark.
    static let well = NSColor(name: nil) { appearance in
        appearance.isDark
            ? NSColor(srgbRed: 0.062, green: 0.063, blue: 0.066, alpha: 1)
            : NSColor(srgbRed: 0.984, green: 0.985, blue: 0.988, alpha: 1)
    }
    /// Padding area behind the screenshot in the editor. A neutral mid-gray so a
    /// white screenshot has a visible edge against it.
    static let canvasBackdrop = NSColor(name: nil) { appearance in
        appearance.isDark
            ? NSColor(srgbRed: 0.100, green: 0.102, blue: 0.106, alpha: 1)
            : NSColor(srgbRed: 0.842, green: 0.845, blue: 0.852, alpha: 1)
    }
    /// Primary text/icon ink.
    static let ink = NSColor(name: nil) { appearance in
        appearance.isDark
            ? NSColor(srgbRed: 0.912, green: 0.914, blue: 0.918, alpha: 1)
            : NSColor(srgbRed: 0.098, green: 0.099, blue: 0.104, alpha: 1)
    }
    /// Secondary ink.
    static let inkDim = NSColor(name: nil) { appearance in
        appearance.isDark
            ? NSColor(srgbRed: 0.600, green: 0.605, blue: 0.615, alpha: 1)
            : NSColor(srgbRed: 0.398, green: 0.402, blue: 0.412, alpha: 1)
    }
    /// Structural border.
    static let rule = NSColor(name: nil) { appearance in
        appearance.isDark
            ? NSColor(srgbRed: 0.245, green: 0.248, blue: 0.253, alpha: 1)
            : NSColor(srgbRed: 0.660, green: 0.664, blue: 0.672, alpha: 1)
    }
    /// Quiet divider between toolbar cells.
    static let hairline = NSColor(name: nil) { appearance in
        appearance.isDark
            ? NSColor.white.withAlphaComponent(0.10)
            : NSColor.black.withAlphaComponent(0.11)
    }
    /// Hover wash for flat cells.
    static let hoverWash = accent.withAlphaComponent(0.12)
    /// Pressed wash for flat cells — darker than hover for a distinct mouse-down state.
    static let pressedWash = accent.withAlphaComponent(0.26)
    /// Selected/active wash — a translucent red used uniformly for the active
    /// tool, color, and size (no border).
    static let activeWash = accent.withAlphaComponent(0.20)

    // MARK: Metrics — sharp corners, fixed rhythm

    static let headerHeight: CGFloat = 36
    static let toolbarHeight: CGFloat = 42

    // MARK: Typography

    static let brandFont = NSFont.systemFont(ofSize: 12, weight: .black)
    static let cellFont = NSFont.systemFont(ofSize: 11.5, weight: .semibold)
    static let cellKeyFont = NSFont.monospacedSystemFont(ofSize: 10, weight: .medium)
    static let headerFont = NSFont.monospacedSystemFont(ofSize: 11, weight: .semibold)
    static let iconConfiguration = NSImage.SymbolConfiguration(pointSize: 12, weight: .semibold, scale: .medium)

    static let tagFont = NSFont.monospacedSystemFont(ofSize: 11.5, weight: .medium)
    static let tagKeyFont = NSFont.monospacedSystemFont(ofSize: 10.5, weight: .semibold)

    static let brandName = "SCRCAP"

    static func logoImage(size: CGFloat, template: Bool = false) -> NSImage? {
        let resourceURL = Bundle.main.url(forResource: "MenuBarIconTemplate", withExtension: "png")
            ?? Bundle.module.url(forResource: "MenuBarIconTemplate", withExtension: "png")
        guard let resourceURL, let image = NSImage(contentsOf: resourceURL) else { return nil }
        image.size = NSSize(width: size, height: size)
        image.accessibilityDescription = "scrcap"

        if template {
            image.isTemplate = true
            return image
        }

        let tinted = NSImage(size: NSSize(width: size, height: size))
        tinted.lockFocus()
        let rect = NSRect(origin: .zero, size: tinted.size)
        accent.setFill()
        rect.fill()
        image.draw(in: rect, from: .zero, operation: .destinationIn, fraction: 1)
        tinted.unlockFocus()
        tinted.isTemplate = false
        tinted.accessibilityDescription = "scrcap"
        return tinted
    }

    // MARK: Overlay & HUD (fixed carbon — drawn over arbitrary screen content)

    static let overlayDim = NSColor.black.withAlphaComponent(0.38)

    static let tagBackground = NSColor(srgbRed: 0.10, green: 0.10, blue: 0.10, alpha: 0.92)
    static let tagRule = NSColor.white.withAlphaComponent(0.35)
    static let tagText = NSColor(srgbRed: 0.933, green: 0.933, blue: 0.927, alpha: 1)
    static let tagDimText = NSColor(srgbRed: 0.60, green: 0.60, blue: 0.60, alpha: 1)

    // MARK: Hazard ants — red dashes over an appearance-aware base

    static let antsDash: [CGFloat] = [6, 6]
    static let antsBase = NSColor(name: nil) { appearance in
        appearance.isDark ? NSColor.black : NSColor.white
    }

    /// Capture selection stroke: solid appearance-aware underlay + marching
    /// red dashes. Used by the capture overlay and the crop tool so "this
    /// region will be captured" looks identical everywhere.
    static func strokeHazardAnts(_ rect: CGRect, phase: CGFloat, lineWidth: CGFloat = 2, in ctx: CGContext) {
        ctx.saveGState()
        ctx.setLineWidth(lineWidth)
        ctx.setStrokeColor(antsBase.cgColor)
        ctx.stroke(rect)
        ctx.setStrokeColor(accent.cgColor)
        ctx.setLineDash(phase: phase, lengths: antsDash)
        ctx.stroke(rect)
        ctx.restoreGState()
    }

    /// Square corner handles for the hazard selection.
    static func drawHazardHandles(_ rect: NSRect) {
        let r: CGFloat = 3.5
        for corner in [
            NSPoint(x: rect.minX, y: rect.minY), NSPoint(x: rect.maxX, y: rect.minY),
            NSPoint(x: rect.minX, y: rect.maxY), NSPoint(x: rect.maxX, y: rect.maxY),
        ] {
            let box = NSRect(x: corner.x - r, y: corner.y - r, width: r * 2, height: r * 2)
            accent.setFill()
            box.fill()
            NSColor.black.setStroke()
            let path = NSBezierPath(rect: box)
            path.lineWidth = 1
            path.stroke()
        }
    }
}

extension NSAppearance {
    var isDark: Bool { bestMatch(from: [.darkAqua, .aqua]) == .darkAqua }
}

// MARK: - Tag blocks (draw contexts)

/// Flat carbon block drawn over screen content — selection sizes, cursor
/// coordinates, window titles, key legends. Sharp corners, 1px rule, mono
/// text; keys render as outlined squares with red glyphs. One renderer so
/// every surface's labels are pixel-identical. Works in flipped and
/// non-flipped contexts.
enum ThemeTag {
    enum Segment {
        case text(String)
        case dim(String)
        case key(String) // outlined keycap square, red glyph
    }

    private static let keycapHeight: CGFloat = 18
    private static let blockPaddingX: CGFloat = 10
    private static let blockHeight: CGFloat = 28

    private static func textAttributes(dimmed: Bool) -> [NSAttributedString.Key: Any] {
        [.font: Theme.tagFont, .foregroundColor: dimmed ? Theme.tagDimText : Theme.tagText]
    }

    private static let keyAttributes: [NSAttributedString.Key: Any] = [
        .font: Theme.tagKeyFont,
        .foregroundColor: Theme.accent,
    ]

    private static func width(of segment: Segment) -> CGFloat {
        switch segment {
        case .text(let s): return NSAttributedString(string: s, attributes: textAttributes(dimmed: false)).size().width
        case .dim(let s): return NSAttributedString(string: s, attributes: textAttributes(dimmed: true)).size().width
        case .key(let s): return NSAttributedString(string: s, attributes: keyAttributes).size().width + 12
        }
    }

    /// Draws the block near `point`, clamped inside `bounds`. When `centered`,
    /// `point.x` is the block's horizontal center; otherwise its left edge.
    static func draw(_ segments: [Segment], near point: NSPoint, in bounds: NSRect, centered: Bool = true) {
        let contentWidth = segments.reduce(0) { $0 + width(of: $1) }
        let size = NSSize(width: contentWidth + blockPaddingX * 2, height: blockHeight)
        var origin = centered
            ? NSPoint(x: point.x - size.width / 2, y: point.y)
            : point
        origin.x = max(6, min(origin.x, bounds.width - size.width - 6))
        origin.y = max(6, min(origin.y, bounds.height - size.height - 6))
        let box = NSRect(origin: origin, size: size)

        // Soft shadow so the readout stays legible over busy/light content.
        NSGraphicsContext.saveGraphicsState()
        let shadow = NSShadow()
        shadow.shadowColor = NSColor.black.withAlphaComponent(0.45)
        shadow.shadowBlurRadius = 8
        shadow.shadowOffset = .zero
        shadow.set()
        Theme.tagBackground.setFill()
        box.fill()
        NSGraphicsContext.restoreGraphicsState()

        Theme.tagRule.setStroke()
        let frame = NSBezierPath(rect: box.insetBy(dx: 0.5, dy: 0.5))
        frame.lineWidth = 1
        frame.stroke()

        var x = origin.x + blockPaddingX
        for segment in segments {
            let w = width(of: segment)
            switch segment {
            case .text(let s), .dim(let s):
                let dimmed = { if case .dim = segment { return true } else { return false } }()
                let str = NSAttributedString(string: s, attributes: textAttributes(dimmed: dimmed))
                let h = str.size().height
                str.draw(at: NSPoint(x: x, y: origin.y + (size.height - h) / 2))
            case .key(let s):
                let cap = NSRect(
                    x: x, y: origin.y + (size.height - keycapHeight) / 2,
                    width: w, height: keycapHeight
                )
                Theme.tagRule.setStroke()
                let capFrame = NSBezierPath(rect: cap.insetBy(dx: 0.5, dy: 0.5))
                capFrame.lineWidth = 1
                capFrame.stroke()
                let str = NSAttributedString(string: s, attributes: keyAttributes)
                let textSize = str.size()
                str.draw(at: NSPoint(
                    x: cap.midX - textSize.width / 2,
                    y: cap.midY - textSize.height / 2
                ))
            }
            x += w
        }
    }

    /// "[⎵] move   [esc] cancel" legend — keys as outlined caps, labels dimmed.
    static func legend(_ hints: [(key: String, label: String)]) -> [Segment] {
        var segments: [Segment] = []
        for (index, hint) in hints.enumerated() {
            if index > 0 { segments.append(.dim("    ")) }
            segments.append(.key(hint.key))
            segments.append(.dim(" \(hint.label)"))
        }
        return segments
    }
}
