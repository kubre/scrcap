// KeymapEngine — portable core (P4). Action ↔ chord mapping, chord parsing,
// conflict detection. Keys are named abstractly ("2", "r", "f5"); translation
// to OS keycodes (Carbon on macOS) lives in the platform layer.

import Foundation

public struct ChordModifiers: OptionSet, Codable, Hashable, Sendable {
    public let rawValue: UInt8
    public init(rawValue: UInt8) { self.rawValue = rawValue }

    public static let command = ChordModifiers(rawValue: 1 << 0)
    public static let option  = ChordModifiers(rawValue: 1 << 1)
    public static let shift   = ChordModifiers(rawValue: 1 << 2)
    public static let control = ChordModifiers(rawValue: 1 << 3)
}

public struct KeyChord: Codable, Hashable, Sendable {
    /// Normalized lowercase key name: "a"…"z", "0"…"9", "f1"…"f12",
    /// "space", "esc", punctuation characters.
    public var key: String
    public var modifiers: ChordModifiers

    public init(key: String, modifiers: ChordModifiers) {
        self.key = key.lowercased()
        self.modifiers = modifiers
    }

    /// Parses "opt+shift+2", "cmd+shift+z", "ctrl+alt+space". Returns nil on
    /// empty key or unknown modifier token.
    public init?(string: String) {
        var mods: ChordModifiers = []
        var key: String?
        for raw in string.split(separator: "+") {
            let token = raw.trimmingCharacters(in: .whitespaces).lowercased()
            switch token {
            case "cmd", "command", "⌘": mods.insert(.command)
            case "opt", "option", "alt", "⌥": mods.insert(.option)
            case "shift", "⇧": mods.insert(.shift)
            case "ctrl", "control", "⌃": mods.insert(.control)
            case "": continue
            default:
                if key != nil { return nil } // two non-modifier tokens
                key = token
            }
        }
        guard let k = key, !k.isEmpty else { return nil }
        self.init(key: k, modifiers: mods)
    }

    /// Canonical storage form, e.g. "opt+shift+2".
    public var stringValue: String {
        var parts: [String] = []
        if modifiers.contains(.control) { parts.append("ctrl") }
        if modifiers.contains(.option) { parts.append("opt") }
        if modifiers.contains(.shift) { parts.append("shift") }
        if modifiers.contains(.command) { parts.append("cmd") }
        parts.append(key)
        return parts.joined(separator: "+")
    }

    /// Display form with mac symbols, e.g. "⌥⇧2".
    public var displayValue: String {
        var s = ""
        if modifiers.contains(.control) { s += "⌃" }
        if modifiers.contains(.option) { s += "⌥" }
        if modifiers.contains(.shift) { s += "⇧" }
        if modifiers.contains(.command) { s += "⌘" }
        return s + key.uppercased()
    }
}

/// Globally-hotkeyed actions. Editor-local keys (A/R/C, 1–7, Esc…) are
/// handled by the editor against `Settings.editorKeys`.
public enum AppAction: String, Codable, Sendable {
    case captureRegion
    case captureWindow
    case captureFullscreen
    case captureScrolling
    case repeatLast

    public static let shortcutOrder: [AppAction] = [
        .captureRegion, .captureWindow, .captureFullscreen, .captureScrolling, .repeatLast,
    ]

    public var title: String {
        switch self {
        case .captureFullscreen: return "Capture Fullscreen"
        case .captureRegion: return "Capture Region"
        case .captureWindow: return "Capture Window"
        case .captureScrolling: return "Scrolling Capture"
        case .repeatLast: return "Repeat Last Capture"
        }
    }
}

public struct Keymap: Codable, Equatable, Sendable {
    public private(set) var bindings: [AppAction: KeyChord]

    /// Region is the daily-driver capture, so it gets the primary slot (⌥⇧1).
    public static let defaults = Keymap(bindings: [
        .captureRegion: KeyChord(key: "1", modifiers: [.option, .shift]),
        .captureWindow: KeyChord(key: "2", modifiers: [.option, .shift]),
        .captureFullscreen: KeyChord(key: "3", modifiers: [.option, .shift]),
        .captureScrolling: KeyChord(key: "4", modifiers: [.option, .shift]),
        .repeatLast: KeyChord(key: "r", modifiers: [.option, .shift]),
    ])

    public init(bindings: [AppAction: KeyChord]) {
        self.bindings = bindings
    }

    public func chord(for action: AppAction) -> KeyChord? { bindings[action] }

    /// The action already bound to `chord`, if any (excluding `excluding`).
    public func conflict(for chord: KeyChord, excluding: AppAction? = nil) -> AppAction? {
        bindings.first { $0.value == chord && $0.key != excluding }?.key
    }

    /// Rebinds `action`. Returns the action the chord was stolen from, if the
    /// chord was already in use (the caller decides how to surface that).
    @discardableResult
    public mutating func set(_ chord: KeyChord, for action: AppAction) -> AppAction? {
        let conflicted = conflict(for: chord, excluding: action)
        if let conflicted { bindings.removeValue(forKey: conflicted) }
        bindings[action] = chord
        return conflicted
    }

    /// Chords claimed by macOS itself that we warn about in the recorder UI.
    public static let systemReserved: Set<KeyChord> = [
        KeyChord(key: "3", modifiers: [.command, .shift]),
        KeyChord(key: "4", modifiers: [.command, .shift]),
        KeyChord(key: "5", modifiers: [.command, .shift]),
        KeyChord(key: "space", modifiers: [.command]),
        KeyChord(key: "q", modifiers: [.command]),
        KeyChord(key: "tab", modifiers: [.command]),
    ]

    public static func isSystemReserved(_ chord: KeyChord) -> Bool {
        systemReserved.contains(chord)
    }
}
