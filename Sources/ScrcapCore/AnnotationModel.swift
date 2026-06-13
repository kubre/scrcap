// AnnotationModel — portable core (P4). No AppKit/CoreGraphics imports allowed.
//
// The annotation layer is an append-only stack of vector shapes with an undo
// cursor. There is no selection state by design ("draw stays armed").

import Foundation

/// A point in image coordinates, top-left origin, measured in points
/// (not pixels — Retina scaling is applied only at export).
public struct CorePoint: Codable, Equatable, Sendable {
    public var x: Double
    public var y: Double
    public init(x: Double, y: Double) {
        self.x = x
        self.y = y
    }
}

public enum ShapeKind: Codable, Equatable, Sendable {
    case arrow
    case rectangle
    /// Pixelated/obscured region (drag a rectangle); the renderer mosaics the
    /// underlying image within the region. Carries no associated data.
    case pixelate
    /// Counter badge; the number is fixed at creation time. Undo removes the
    /// shape, and the next stamp derives its number from what remains visible.
    case counter(number: Int)
    /// Text annotation anchored at `start` (top-left). Both the string and
    /// the point size are fixed at commit time, so later preference changes
    /// don't reflow existing annotations.
    case text(string: String, size: Double)
}

/// The three drawing sizes selectable in the editor (S/M/L). `scale` multiplies
/// the base stroke width and counter radius; for text the scaled point size is
/// baked into `ShapeKind.text` at creation, so the renderer never scales text.
public enum ShapeSize: String, Codable, Equatable, Sendable, CaseIterable {
    case small, medium, large

    public var scale: Double {
        switch self {
        case .small: return 1
        case .medium: return 1.8
        case .large: return 2.8
        }
    }
}

public struct Shape: Codable, Equatable, Sendable {
    public var kind: ShapeKind
    /// Index into the palette (0-based). Resolved to an actual color by the
    /// presentation layer.
    public var colorIndex: Int
    /// The drawing size this shape was created at (S/M/L).
    public var size: ShapeSize
    public var start: CorePoint
    public var end: CorePoint

    public init(kind: ShapeKind, colorIndex: Int, size: ShapeSize = .small, start: CorePoint, end: CorePoint) {
        self.kind = kind
        self.colorIndex = colorIndex
        self.size = size
        self.start = start
        self.end = end
    }
}

/// Append-only shape stack with an undo cursor.
/// Undo = decrement cursor, redo = increment, new draw truncates the redo tail.
public struct AnnotationStack: Equatable, Sendable {
    public private(set) var shapes: [Shape] = []
    /// shapes[0..<cursor] are visible.
    public private(set) var cursor: Int = 0

    public init() {}

    public var visible: ArraySlice<Shape> { shapes[0..<cursor] }
    public var canUndo: Bool { cursor > 0 }
    public var canRedo: Bool { cursor < shapes.count }
    public var isEmpty: Bool { cursor == 0 }

    public mutating func append(_ shape: Shape) {
        if cursor < shapes.count {
            shapes.removeSubrange(cursor...)
        }
        shapes.append(shape)
        cursor = shapes.count
    }

    @discardableResult
    public mutating func undo() -> Bool {
        guard canUndo else { return false }
        cursor -= 1
        return true
    }

    @discardableResult
    public mutating func redo() -> Bool {
        guard canRedo else { return false }
        cursor += 1
        return true
    }

    /// The number the next counter stamp should carry: visible counters + 1.
    /// Auto-increments as you stamp, decrements on undo.
    public var nextCounterNumber: Int {
        visible.reduce(into: 1) { n, s in
            if case .counter = s.kind { n += 1 }
        }
    }
}
