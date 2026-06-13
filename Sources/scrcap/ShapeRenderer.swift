// ShapeRenderer — draws the portable core's vector shapes. Used identically
// by the live editor canvas and by the export flattener; both supply a
// flipped (top-left origin) graphics context in point coordinates.

import AppKit
import ScrcapCore

enum ShapeRenderer {
    static func color(at index: Int, palette: [String]) -> NSColor {
        guard palette.indices.contains(index) else { return .systemRed }
        return NSColor(hex: palette[index]) ?? .systemRed
    }

    static func draw(_ shapes: [Shape], palette: [String], strokeWidth: CGFloat) {
        for shape in shapes {
            draw(shape, palette: palette, strokeWidth: strokeWidth)
        }
    }

    static func draw(_ shape: Shape, palette: [String], strokeWidth: CGFloat) {
        let color = color(at: shape.colorIndex, palette: palette)
        let start = NSPoint(x: shape.start.x, y: shape.start.y)
        let end = NSPoint(x: shape.end.x, y: shape.end.y)

        switch shape.kind {
        case .arrow:
            drawArrow(from: start, to: end, color: color, strokeWidth: strokeWidth)
        case .rectangle:
            drawRectangle(from: start, to: end, color: color, strokeWidth: strokeWidth)
        case .counter(let number):
            drawCounter(number: number, at: end, color: color, strokeWidth: strokeWidth)
        case .text(let string, let size):
            drawText(string, at: start, color: color, size: size)
        }
    }

    static func textFont(size: CGFloat) -> NSFont {
        .systemFont(ofSize: size + 1, weight: .bold)
    }

    static func textAttributes(size: CGFloat, color: NSColor) -> [NSAttributedString.Key: Any] {
        [
            .font: textFont(size: size),
            .foregroundColor: color,
        ]
    }

    /// Anchored at `origin` (top-left); newlines render as line breaks. Must
    /// stay metrics-identical to the live editing text view.
    private static func drawText(_ string: String, at origin: NSPoint, color: NSColor, size: Double) {
        let attrs = textAttributes(size: size, color: color)
        NSAttributedString(string: string, attributes: attrs).draw(at: origin)
    }

    /// Straight shaft + filled head scaled to drag length.
    private static func drawArrow(from start: NSPoint, to end: NSPoint, color: NSColor, strokeWidth: CGFloat) {
        let dx = end.x - start.x
        let dy = end.y - start.y
        let length = max(hypot(dx, dy), 1)
        let headLength = min(max(length * 0.18, 9), 26)

        let dirX = dx / length
        let dirY = dy / length
        // Shorten the shaft so it doesn't poke through the head.
        let shaftEnd = NSPoint(x: end.x - dirX * headLength * 0.6, y: end.y - dirY * headLength * 0.6)

        color.setStroke()
        color.setFill()

        let shaft = NSBezierPath()
        shaft.lineWidth = strokeWidth
        shaft.lineCapStyle = .round
        shaft.move(to: start)
        shaft.line(to: shaftEnd)
        shaft.stroke()

        // Filled triangular head.
        let angle = atan2(dirY, dirX)
        let spread: CGFloat = 0.46
        let head = NSBezierPath()
        head.move(to: end)
        head.line(to: NSPoint(
            x: end.x - headLength * cos(angle - spread),
            y: end.y - headLength * sin(angle - spread)
        ))
        head.line(to: NSPoint(
            x: end.x - headLength * cos(angle + spread),
            y: end.y - headLength * sin(angle + spread)
        ))
        head.close()
        head.fill()
    }

    /// Stroke-only, rounded corners, adaptive stroke.
    private static func drawRectangle(from start: NSPoint, to end: NSPoint, color: NSColor, strokeWidth: CGFloat) {
        let rect = NSRect(
            x: min(start.x, end.x), y: min(start.y, end.y),
            width: abs(start.x - end.x), height: abs(start.y - end.y)
        )
        guard rect.width > 1, rect.height > 1 else { return }
        color.setStroke()
        let path = NSBezierPath(roundedRect: rect, xRadius: 3, yRadius: 3)
        path.lineWidth = min(strokeWidth, max(2, min(rect.width, rect.height) * 0.08))
        path.stroke()
    }

    static let counterRadius: CGFloat = 14

    /// Filled circle with a centered white numeral.
    private static func drawCounter(number: Int, at center: NSPoint, color: NSColor, strokeWidth: CGFloat) {
        let r = counterRadius
        let circleRect = NSRect(x: center.x - r, y: center.y - r, width: r * 2, height: r * 2)
        color.setFill()
        NSBezierPath(ovalIn: circleRect).fill()

        let text = "\(number)"
        let fontSize: CGFloat = text.count > 2 ? 11 : 14
        let numeralColor: NSColor = color.isLight ? .black : .white
        let attrs: [NSAttributedString.Key: Any] = [
            .font: NSFont.systemFont(ofSize: fontSize, weight: .bold),
            .foregroundColor: numeralColor,
        ]
        let str = NSAttributedString(string: text, attributes: attrs)
        let size = str.size()
        str.draw(at: NSPoint(x: center.x - size.width / 2, y: center.y - size.height / 2))
    }
}

extension NSColor {
    convenience init?(hex: String) {
        var cleaned = hex.trimmingCharacters(in: .whitespaces)
        if cleaned.hasPrefix("#") { cleaned.removeFirst() }
        guard cleaned.count == 6, let value = UInt32(cleaned, radix: 16) else { return nil }
        self.init(
            srgbRed: CGFloat((value >> 16) & 0xFF) / 255,
            green: CGFloat((value >> 8) & 0xFF) / 255,
            blue: CGFloat(value & 0xFF) / 255,
            alpha: 1
        )
    }

    var hexString: String {
        guard let rgb = usingColorSpace(.sRGB) else { return "#FF3B30" }
        return String(
            format: "#%02X%02X%02X",
            Int(round(rgb.redComponent * 255)),
            Int(round(rgb.greenComponent * 255)),
            Int(round(rgb.blueComponent * 255))
        )
    }

    var isLight: Bool {
        guard let rgb = usingColorSpace(.sRGB) else { return false }
        let luma = 0.299 * rgb.redComponent + 0.587 * rgb.greenComponent + 0.114 * rgb.blueComponent
        return luma > 0.72
    }
}
