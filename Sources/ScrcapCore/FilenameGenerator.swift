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

    /// Returns `filename`, then `filename-2.ext`, `filename-3.ext`, and so on
    /// until the supplied existence check reports a free name.
    public static func availableFilename(
        _ filename: String,
        exists: (String) -> Bool
    ) -> String {
        guard exists(filename) else { return filename }

        let name = filename as NSString
        let stem = name.deletingPathExtension
        let ext = name.pathExtension
        var suffix = 2
        while true {
            let candidate = ext.isEmpty ? "\(stem)-\(suffix)" : "\(stem)-\(suffix).\(ext)"
            if !exists(candidate) { return candidate }
            suffix += 1
        }
    }
}
