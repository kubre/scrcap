// SettingsStore — portable core (P4). Versioned, human-readable JSON with
// explicit migrations and atomic writes.

import Foundation

public enum CaptureMode: String, Codable, Sendable {
    case region, window, fullscreen, scrolling

    public static let captureOrder: [CaptureMode] = [.region, .window, .fullscreen, .scrolling]
}

public enum AfterCaptureBehavior: String, Codable, Sendable {
    /// Open the annotation editor (default).
    case openEditor
    /// Straight to clipboard, no editor.
    case copyOnly
    /// Copy immediately and also open the editor.
    case both
}

public enum EscBehavior: String, Codable, Sendable {
    /// Flatten, copy PNG, close (default).
    case copyAndClose
    case closeOnly
}

public enum TextEnterBehavior: String, Codable, Sendable {
    /// Return inserts a new line; Shift-Return commits the text annotation.
    case newline
    /// Return commits the text annotation; Shift-Return inserts a new line.
    case commit
}

public enum WindowCaptureTarget: String, Codable, Sendable {
    /// Capture the frontmost usable app window.
    case active
    /// Show the overlay and let the user choose a window.
    case selected
}

public enum ThemeMode: String, Codable, Sendable {
    case system
    case light
    case dark
}

public struct Settings: Codable, Equatable, Sendable {
    public static let currentSchemaVersion = 6
    public static let paletteSlotCount = 7
    public static let minStrokeWidth = 1.0
    public static let maxStrokeWidth = 8.0
    public static let minTextSize = 10.0
    public static let maxTextSize = 36.0
    public static let minScrollingMaxHeight = 1_000
    public static let maxScrollingMaxHeight = 100_000

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
    /// Return key behavior for the text tool (schema v3).
    public var textEnterBehavior: TextEnterBehavior
    /// Which window the Window capture action targets (schema v4).
    public var windowCaptureTarget: WindowCaptureTarget
    public var includeWindowShadow: Bool
    /// Automatically grow the editor canvas when drawing beyond an edge (schema v5).
    public var autoExpandCanvas: Bool
    /// Fill color for newly-created canvas area (schema v5).
    public var canvasExtensionBackgroundHex: String
    /// nil → ~/Desktop
    public var saveFolder: String?
    /// Tokens: {date}, {time}
    public var filenamePattern: String
    /// 1 = points, 2 = Retina pixels.
    public var exportScale: Int
    public var scrollingMaxHeight: Int
    public var launchAtLogin: Bool
    /// Theme override: system follows macOS appearance, light/dark force it (schema v6).
    public var themeMode: ThemeMode
    public var resolvedExportScale: Int { exportScale == 1 ? 1 : 2 }

    public static let defaultPalette = [
        "#FF3B30", "#FF9500", "#FFCC00", "#34C759", "#0A84FF", "#BF5AF2", "#F2F2F7",
    ]

    public static var defaults: Settings {
        Settings(
            schemaVersion: currentSchemaVersion,
            hotkeys: Keymap.defaults.bindings.reduce(into: [:]) { $0[$1.key.rawValue] = $1.value.stringValue },
            afterCapture: CaptureMode.captureOrder.reduce(into: [:]) { $0[$1.rawValue] = .openEditor },
            paletteHex: defaultPalette,
            escBehavior: .copyAndClose,
            strokeWidth: 3,
            textSize: 16,
            textEnterBehavior: .newline,
            windowCaptureTarget: .active,
            includeWindowShadow: false,
            autoExpandCanvas: true,
            canvasExtensionBackgroundHex: "#FFFFFF",
            saveFolder: nil,
            filenamePattern: "scrcap-{date}-{time}",
            exportScale: 2,
            scrollingMaxHeight: 20_000,
            launchAtLogin: false,
            themeMode: .system
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

    public mutating func normalizeLegacyDefaultCaptureHotkeys() {
        let region = hotkeys[AppAction.captureRegion.rawValue]
        let window = hotkeys[AppAction.captureWindow.rawValue]
        let fullscreen = hotkeys[AppAction.captureFullscreen.rawValue]
        let scrolling = hotkeys[AppAction.captureScrolling.rawValue]

        guard scrolling == "opt+shift+4" else { return }

        switch (region, window, fullscreen) {
        case ("opt+shift+1", "opt+shift+3", "opt+shift+2"),
             ("opt+shift+2", "opt+shift+3", "opt+shift+1"):
            hotkeys[AppAction.captureRegion.rawValue] = "opt+shift+1"
            hotkeys[AppAction.captureWindow.rawValue] = "opt+shift+2"
            hotkeys[AppAction.captureFullscreen.rawValue] = "opt+shift+3"
        default:
            break
        }
    }

    /// Keeps the human-editable settings file from feeding invalid values into
    /// AppKit controls, large bitmap allocations, or array-indexed palette UI.
    public mutating func normalize() {
        schemaVersion = Self.currentSchemaVersion
        normalizeLegacyDefaultCaptureHotkeys()

        paletteHex = (0..<Self.paletteSlotCount).map { index in
            let candidate = paletteHex.indices.contains(index) ? paletteHex[index] : Self.defaultPalette[index]
            return Self.normalizedHexColor(candidate) ?? Self.defaultPalette[index]
        }
        canvasExtensionBackgroundHex = Self.normalizedHexColor(canvasExtensionBackgroundHex) ?? "#FFFFFF"

        strokeWidth = Self.clampFinite(strokeWidth, min: Self.minStrokeWidth, max: Self.maxStrokeWidth)
        textSize = Self.clampFinite(textSize, min: Self.minTextSize, max: Self.maxTextSize)
        exportScale = exportScale == 1 ? 1 : 2
        scrollingMaxHeight = min(max(scrollingMaxHeight, Self.minScrollingMaxHeight), Self.maxScrollingMaxHeight)
    }

    private static func clampFinite(_ value: Double, min: Double, max: Double) -> Double {
        guard value.isFinite else { return min }
        return Swift.min(Swift.max(value, min), max)
    }

    private static func normalizedHexColor(_ raw: String) -> String? {
        var value = raw.trimmingCharacters(in: .whitespacesAndNewlines).uppercased()
        if value.hasPrefix("#") { value.removeFirst() }
        guard value.count == 6,
              value.unicodeScalars.allSatisfy({ CharacterSet(charactersIn: "0123456789ABCDEF").contains($0) })
        else { return nil }
        return "#\(value)"
    }
}

public final class SettingsStore {
    public private(set) var settings: Settings
    private let fileURL: URL

    public init(directory: URL) {
        fileURL = directory.appendingPathComponent("settings.json")
        settings = SettingsStore.load(from: fileURL) ?? .defaults
        settings.normalize()
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
        guard version <= Settings.currentSchemaVersion else { return nil }
        var json = obj
        var v = version
        while v < Settings.currentSchemaVersion {
            switch v {
            case 1: // v2 added the text tool's point size
                json["textSize"] = 16.0
            case 2: // v3 added configurable Return behavior for text entry
                json["textEnterBehavior"] = TextEnterBehavior.newline.rawValue
            case 3: // v4 made Window capture target configurable
                json["windowCaptureTarget"] = WindowCaptureTarget.active.rawValue
            case 4: // v5 added live canvas auto-expansion
                json["autoExpandCanvas"] = true
                json["canvasExtensionBackgroundHex"] = "#FFFFFF"
            case 5: // v6 added theme mode preference
                json["themeMode"] = ThemeMode.system.rawValue
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
        settings.normalize()
        if !save() {
            NSLog("Scrcap: failed to persist settings update.")
        }
    }

    /// Persists settings to disk and returns whether the write succeeded.
    /// The in-memory settings are updated immediately by callers; callers should
    /// treat a false return as a signal to surface diagnostics.
    @discardableResult
    public func save() -> Bool {
        let dir = fileURL.deletingLastPathComponent()
        do {
            try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
            let encoder = JSONEncoder()
            encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
            let data = try encoder.encode(settings)
            // Atomic: temp file + rename.
            try data.write(to: fileURL, options: .atomic)
            return true
        } catch {
            NSLog("Scrcap: failed to save settings to %@: %@", fileURL.path, String(describing: error))
            return false
        }
    }
}
