// StitchEngine — portable core. Pure row-hash alignment logic for scrolling
// capture. Pixel access and scroll injection live in the platform layer; this
// operates only on per-row hash sequences so it is fully unit-testable.

import Foundation

public enum StitchEngine {
    /// FNV-1a over a row of pixel bytes.
    public static func rowHash(_ bytes: UnsafeRawBufferPointer) -> UInt64 {
        var h: UInt64 = 0xcbf29ce484222325
        for b in bytes {
            h = (h ^ UInt64(b)) &* 0x100000001b3
        }
        return h
    }

    public struct Alignment: Equatable, Sendable {
        /// Index into the new frame's rows where unseen content begins.
        /// (Rows [0..<newContentStart] overlap the previous frame's tail.)
        public var newContentStart: Int
        public init(newContentStart: Int) { self.newContentStart = newContentStart }
    }

    /// Aligns a new frame against the accumulated image's tail.
    ///
    /// Finds the largest overlap `o` (>= minOverlap) where the last `o` row
    /// hashes of `accumulated` match the first `o` row hashes of `frame` with
    /// at least `tolerance` agreement. Returns nil when no alignment is found
    /// (non-scrollable target, or content fully changed).
    ///
    /// Returns newContentStart == frame.count when the frame adds nothing
    /// (bottom reached / elastic bounce).
    public static func align(
        accumulated: [UInt64],
        frame: [UInt64],
        minOverlap: Int = 16,
        tolerance: Double = 0.98
    ) -> Alignment? {
        guard !accumulated.isEmpty, !frame.isEmpty else { return nil }

        // Identical frame → no new rows (settle/bounce/bottom).
        if frame.count <= accumulated.count,
           matchRatio(accumulated.suffix(frame.count), frame[...]) >= tolerance {
            return Alignment(newContentStart: frame.count)
        }

        let maxOverlap = min(accumulated.count, frame.count)
        guard maxOverlap >= minOverlap else { return nil }
        for o in stride(from: maxOverlap, through: minOverlap, by: -1) {
            if matchRatio(accumulated.suffix(o), frame.prefix(o)) >= tolerance {
                return Alignment(newContentStart: o)
            }
        }
        return nil
    }

    private static func matchRatio(_ a: ArraySlice<UInt64>, _ b: ArraySlice<UInt64>) -> Double {
        guard a.count == b.count, !a.isEmpty else { return 0 }
        var equal = 0
        for (x, y) in zip(a, b) where x == y { equal += 1 }
        return Double(equal) / Double(a.count)
    }

    /// Detects sticky headers/footers: row indices whose hash is identical in
    /// every captured frame. Only meaningful with >= 2 frames; returns the
    /// (topFixedRows, bottomFixedRows) run lengths to crop from middle frames.
    public static func fixedEdges(frames: [[UInt64]]) -> (top: Int, bottom: Int) {
        guard frames.count >= 2, let first = frames.first else { return (0, 0) }
        let minCount = frames.map(\.count).min() ?? 0

        var top = 0
        while top < minCount, frames.allSatisfy({ $0[top] == first[top] }) {
            top += 1
        }
        var bottom = 0
        while bottom < minCount - top,
              frames.allSatisfy({ $0[$0.count - 1 - bottom] == first[first.count - 1 - bottom] }) {
            bottom += 1
        }
        return (top, bottom)
    }
}
