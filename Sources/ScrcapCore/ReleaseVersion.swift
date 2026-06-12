import Foundation

public struct ReleaseVersion: Comparable, Equatable, Sendable {
    private let components: [Int]

    public init?(_ rawValue: String) {
        var value = rawValue.trimmingCharacters(in: .whitespacesAndNewlines)
        if value.hasPrefix("v") || value.hasPrefix("V") {
            value.removeFirst()
        }
        value = value.split(separator: "-", maxSplits: 1).first.map(String.init) ?? value
        value = value.split(separator: "+", maxSplits: 1).first.map(String.init) ?? value

        let parts = value.split(separator: ".", omittingEmptySubsequences: false)
        guard !parts.isEmpty else { return nil }
        let parsed = parts.compactMap { part -> Int? in
            guard !part.isEmpty,
                  part.unicodeScalars.allSatisfy({ CharacterSet.decimalDigits.contains($0) })
            else { return nil }
            return Int(part)
        }
        guard parsed.count == parts.count else { return nil }
        components = parsed
    }

    public static func == (lhs: ReleaseVersion, rhs: ReleaseVersion) -> Bool {
        !(lhs < rhs) && !(rhs < lhs)
    }

    public static func < (lhs: ReleaseVersion, rhs: ReleaseVersion) -> Bool {
        let count = max(lhs.components.count, rhs.components.count)
        for index in 0..<count {
            let left = lhs.components.indices.contains(index) ? lhs.components[index] : 0
            let right = rhs.components.indices.contains(index) ? rhs.components[index] : 0
            if left != right { return left < right }
        }
        return false
    }

    public static func updateAvailable(current: String, latest: String) -> Bool {
        guard let currentVersion = ReleaseVersion(current),
              let latestVersion = ReleaseVersion(latest)
        else { return false }
        return latestVersion > currentVersion
    }
}
