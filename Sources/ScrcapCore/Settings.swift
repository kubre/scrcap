// SettingsStore — portable core (P4). Versioned, human-readable JSON with
// explicit migrations and atomic writes.

import Foundation

public enum CaptureMode: String, Codable, CaseIterable, Sendable {
    case fullscreen, region, window, scrolling
}

public enum AfterCaptureBehavior: String, Codable, CaseIterable, Sendable {
    /// Open the annotation editor (default).
    case openEditor
    /// Straight to clipboard, no editor.
    case copyOnly
    /// Copy immediately and also open the editor.
    case both
}

public enum EscBehavior: String, Codable, CaseIterable, Sendable {
    /// Flatten, copy PNG, close (default).
    case copyAndClose
    case closeOnly
}

public struct Settings: Codable, Equatable, Sendable {
    public static let currentSchemaVersion = 2

    public var schemaVersion: Int
    /// AppAction.rawValue → chord string ("opt+shift+2").
    public var hotkeys: [String: String]
    /// CaptureMode.rawValue → behavior.
    public var afterCapture: [String: AfterCaptureBehavior]
    /// 7 palette slots as #RRGGBB. Slot 0 is the boot default (red).
    public var paletteHex: [String]
    public var escBehavior: EscBehavior
    public var strokeWidth: Double
    /// Point size for the text tool (schema v2).
    public var textSize: Double
    public var includeWindowShadow: Bool
    /// nil → ~/Desktop
    public var saveFolder: String?
    /// Tokens: {date}, {time}
    public var filenamePattern: String
    /// 1 = points, 2 = Retina pixels.
    public var exportScale: Int
    public var scrollingMaxHeight: Int
    public var launchAtLogin: Bool

    public static let defaultPalette = [
        "#FF3B30", "#FF9500", "#FFCC00", "#34C759", "#0A84FF", "#BF5AF2", "#F2F2F7",
    ]

    public static var defaults: Settings {
        Settings(
            schemaVersion: currentSchemaVersion,
            hotkeys: Keymap.defaults.bindings.reduce(into: [:]) { $0[$1.key.rawValue] = $1.value.stringValue },
            afterCapture: CaptureMode.allCases.reduce(into: [:]) { $0[$1.rawValue] = .openEditor },
            paletteHex: defaultPalette,
            escBehavior: .copyAndClose,
            strokeWidth: 3,
            textSize: 16,
            includeWindowShadow: false,
            saveFolder: nil,
            filenamePattern: "scrcap-{date}-{time}",
            exportScale: 2,
            scrollingMaxHeight: 20_000,
            launchAtLogin: false
        )
    }

    public var keymap: Keymap {
        var bindings: [AppAction: KeyChord] = [:]
        for (raw, chordString) in hotkeys {
            if let action = AppAction(rawValue: raw), let chord = KeyChord(string: chordString) {
                bindings[action] = chord
            }
        }
        return Keymap(bindings: bindings)
    }

    public mutating func apply(_ keymap: Keymap) {
        hotkeys = keymap.bindings.reduce(into: [:]) { $0[$1.key.rawValue] = $1.value.stringValue }
    }

    public func behavior(for mode: CaptureMode) -> AfterCaptureBehavior {
        afterCapture[mode.rawValue] ?? .openEditor
    }
}

public final class SettingsStore {
    public private(set) var settings: Settings
    private let fileURL: URL

    public init(directory: URL) {
        fileURL = directory.appendingPathComponent("settings.json")
        settings = SettingsStore.load(from: fileURL) ?? .defaults
    }

    public static func defaultDirectory() -> URL {
        FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
            .appendingPathComponent("scrcap", isDirectory: true)
    }

    private static func load(from url: URL) -> Settings? {
        guard let data = try? Data(contentsOf: url) else { return nil }
        guard let migrated = migrate(data: data) else { return nil }
        return try? JSONDecoder().decode(Settings.self, from: migrated)
    }

    /// Applies schemaVersion upgrades to raw JSON before decoding. v1 is the
    /// first schema, so today this only validates the version field.
    private static func migrate(data: Data) -> Data? {
        guard let obj = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
              let version = obj["schemaVersion"] as? Int else { return nil }
        var json = obj
        var v = version
        while v < Settings.currentSchemaVersion {
            switch v {
            case 1: // v2 added the text tool's point size
                json["textSize"] = 16.0
            default:
                break
            }
            v += 1
            json["schemaVersion"] = v
        }
        return try? JSONSerialization.data(withJSONObject: json)
    }

    public func update(_ mutate: (inout Settings) -> Void) {
        mutate(&settings)
        save()
    }

    public func save() {
        let dir = fileURL.deletingLastPathComponent()
        try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        let encoder = JSONEncoder()
        encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
        guard let data = try? encoder.encode(settings) else { return }
        // Atomic: temp file + rename.
        try? data.write(to: fileURL, options: .atomic)
    }
}
