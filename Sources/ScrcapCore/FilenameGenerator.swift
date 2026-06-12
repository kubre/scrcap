// FilenameGenerator — portable core. Pure Foundation; no AppKit.

import Foundation

public enum FilenameGenerator {
    /// Expands {date} and {time} tokens in `pattern` and appends ".png".
    /// `now` is injectable for testing.
    public static func filename(pattern: String, now: Date = Date()) -> String {
        let date = DateFormatter()
        date.dateFormat = "yyyy-MM-dd"
        let time = DateFormatter()
        time.dateFormat = "HH.mm.ss"
        let expanded = pattern
            .replacingOccurrences(of: "{date}", with: date.string(from: now))
            .replacingOccurrences(of: "{time}", with: time.string(from: now))
        return safeFilenameStem(expanded) + ".png"
    }

    /// Strips characters illegal in filenames; joins remaining segments with "-".
    /// Returns "scrcap" if nothing remains.
    public static func safeFilenameStem(_ raw: String) -> String {
        let invalid = CharacterSet(charactersIn: "/:\\")
            .union(.controlCharacters)
            .union(.newlines)
        let parts = raw
            .components(separatedBy: invalid)
            .map { $0.trimmingCharacters(in: .whitespacesAndNewlines) }
            .filter { !$0.isEmpty }
        let stem = parts.joined(separator: "-")
        return stem.isEmpty ? "scrcap" : stem
    }
}
