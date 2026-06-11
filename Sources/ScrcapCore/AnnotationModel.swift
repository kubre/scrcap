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
    /// Counter badge; the number is fixed at creation time. Undo removes the
    /// shape, and the next stamp derives its number from what remains visible.
    case counter(number: Int)
    /// Text annotation anchored at `start` (top-left). Both the string and
    /// the point size are fixed at commit time, so later preference changes
    /// don't reflow existing annotations.
    case text(string: String, size: Double)
}

public struct Shape: Codable, Equatable, Sendable {
    public var kind: ShapeKind
    /// Index into the 7-slot palette (0-based). Resolved to an actual color by
    /// the presentation layer.
    public var colorIndex: Int
    public var start: CorePoint
    public var end: CorePoint

    public init(kind: ShapeKind, colorIndex: Int, start: CorePoint, end: CorePoint) {
        self.kind = kind
        self.colorIndex = colorIndex
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
