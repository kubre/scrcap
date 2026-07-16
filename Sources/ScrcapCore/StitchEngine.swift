// StitchEngine — portable core. Pure row-hash alignment logic for scrolling
// capture. Pixel access and scroll injection live in the platform layer; this
// operates only on per-row hash sequences so it is fully unit-testable.

import Foundation

public enum StitchEngine {
    public struct RowSignature: Equatable, Sendable {
        let a: UInt64
        let b: UInt64
        let c: UInt64
        let d: UInt64
        let contrast: UInt8

        public init(bins: [UInt8]) {
            func pack(_ start: Int) -> UInt64 {
                var value: UInt64 = 0
                for index in 0..<8 where start + index < bins.count {
                    value |= UInt64(bins[start + index]) << (index * 8)
                }
                return value
            }
            a = pack(0)
            b = pack(8)
            c = pack(16)
            d = pack(24)
            contrast = (bins.max() ?? 0) - (bins.min() ?? 0)
        }

        fileprivate func value(at index: Int) -> Int {
            let word: UInt64
            switch index / 8 {
            case 0: word = a
            case 1: word = b
            case 2: word = c
            default: word = d
            }
            return Int((word >> ((index % 8) * 8)) & 0xFF)
        }

        fileprivate var range: Int {
            Int(contrast)
        }
    }

    /// FNV-1a over a row of pixel bytes.
    public static func rowHash(_ bytes: UnsafeRawBufferPointer) -> UInt64 {
        var h: UInt64 = 0xcbf29ce484222325
        for b in bytes {
            h = (h ^ UInt64(b)) &* 0x100000001b3
        }
        return h
    }

    /// A compact, visually tolerant representation of one RGBA image row.
    public static func rowSignature(_ bytes: UnsafeRawBufferPointer) -> RowSignature {
        let pixelCount = bytes.count / 4
        guard pixelCount >= 32 else {
            let value = bytes.isEmpty
                ? UInt8(0)
                : UInt8(bytes.reduce(0) { $0 + Int($1) } / bytes.count)
            return RowSignature(bins: [UInt8](repeating: value, count: 32))
        }

        let inset = min(pixelCount / 4, max(12, pixelCount / 25))
        let startPixel = inset
        let usablePixels = max(32, pixelCount - inset * 2)
        var bins = [UInt8]()
        bins.reserveCapacity(32)

        for bucket in 0..<32 {
            let start = startPixel + usablePixels * bucket / 32
            let end = startPixel + usablePixels * (bucket + 1) / 32
            var luminance = 0
            var samples = 0
            for pixel in start..<max(start + 1, end) {
                let offset = min(pixel, pixelCount - 1) * 4
                let red = Int(bytes[offset])
                let green = Int(bytes[offset + 1])
                let blue = Int(bytes[offset + 2])
                luminance += (54 * red + 183 * green + 19 * blue) >> 8
                samples += 1
            }
            bins.append(UInt8(luminance / max(1, samples)))
        }
        return RowSignature(bins: bins)
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

    /// Aligns visually similar rows while tolerating antialiasing, scrollbars,
    /// and small animated regions.
    public static func align(
        accumulated: [RowSignature],
        frame: [RowSignature],
        minOverlap: Int = 32,
        rowTolerance: Double = 6,
        requiredMatchRatio: Double = 0.85,
        maximumMeanDistance: Double = 5
    ) -> Alignment? {
        guard !accumulated.isEmpty, !frame.isEmpty else { return nil }

        if frame.count <= accumulated.count {
            let stats = signatureStats(
                accumulated.suffix(frame.count),
                frame[...],
                rowTolerance: rowTolerance
            )
            if stats.matchRatio >= 0.96, stats.meanDistance <= 3 {
                return Alignment(newContentStart: frame.count)
            }
        }

        let maxOverlap = min(accumulated.count, frame.count)
        let minimum = max(minOverlap, maxOverlap / 10)
        guard maxOverlap >= minimum else { return nil }

        for overlap in stride(from: maxOverlap, through: minimum, by: -1) {
            let stats = signatureStats(
                accumulated.suffix(overlap),
                frame.prefix(overlap),
                rowTolerance: rowTolerance
            )
            guard stats.matchRatio >= requiredMatchRatio,
                  stats.meanDistance <= maximumMeanDistance
            else { continue }
            return Alignment(newContentStart: overlap)
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

    public static func fixedEdges(frames: [[RowSignature]]) -> (top: Int, bottom: Int) {
        guard frames.count >= 2, let first = frames.first else { return (0, 0) }
        let minCount = frames.map(\.count).min() ?? 0
        let limit = Int(Double(minCount) * 0.35)

        var top = 0
        while top < limit,
              frames.allSatisfy({ rowsSimilar($0[top], first[top]) })
        {
            top += 1
        }

        var bottom = 0
        while bottom < limit,
              frames.allSatisfy({
                  rowsSimilar($0[$0.count - 1 - bottom], first[first.count - 1 - bottom])
              })
        {
            bottom += 1
        }
        return (top, bottom)
    }

    public static func similarity(_ lhs: [RowSignature], _ rhs: [RowSignature]) -> Double {
        guard lhs.count == rhs.count, !lhs.isEmpty else { return 0 }
        return signatureStats(lhs[...], rhs[...], rowTolerance: 6).matchRatio
    }

    private static func rowsSimilar(
        _ lhs: RowSignature,
        _ rhs: RowSignature,
        tolerance: Double = 6
    ) -> Bool {
        rowDistance(lhs, rhs) <= tolerance
    }

    private static func rowDistance(_ lhs: RowSignature, _ rhs: RowSignature) -> Double {
        var difference = 0
        for index in 0..<32 {
            difference += abs(lhs.value(at: index) - rhs.value(at: index))
        }
        return Double(difference) / 32
    }

    private static func signatureStats(
        _ lhs: ArraySlice<RowSignature>,
        _ rhs: ArraySlice<RowSignature>,
        rowTolerance: Double
    ) -> (matchRatio: Double, meanDistance: Double) {
        guard lhs.count == rhs.count, !lhs.isEmpty else { return (0, .infinity) }

        var informativeCount = 0
        for pair in zip(lhs, rhs) where pair.0.range >= 4 || pair.1.range >= 4 {
            informativeCount += 1
        }
        let useInformative = informativeCount >= max(8, lhs.count / 20)

        var matches = 0
        var distance = 0.0
        var selectedCount = 0
        for pair in zip(lhs, rhs) {
            if useInformative, pair.0.range < 4, pair.1.range < 4 { continue }
            let rowDistance = rowDistance(pair.0, pair.1)
            distance += rowDistance
            if rowDistance <= rowTolerance { matches += 1 }
            selectedCount += 1
        }
        guard selectedCount > 0 else { return (0, .infinity) }
        return (
            Double(matches) / Double(selectedCount),
            distance / Double(selectedCount)
        )
    }
}
