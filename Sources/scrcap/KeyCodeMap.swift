// Carbon virtual keycode ↔ abstract key-name translation. The portable
// KeymapEngine speaks names; this is the macOS dialect table.

import Carbon.HIToolbox
import AppKit
import ScrcapCore

enum KeyCodeMap {
    static let nameToCode: [String: UInt32] = [
        "a": 0, "s": 1, "d": 2, "f": 3, "h": 4, "g": 5, "z": 6, "x": 7,
        "c": 8, "v": 9, "b": 11, "q": 12, "w": 13, "e": 14, "r": 15,
        "y": 16, "t": 17, "1": 18, "2": 19, "3": 20, "4": 21, "6": 22,
        "5": 23, "=": 24, "9": 25, "7": 26, "-": 27, "8": 28, "0": 29,
        "]": 30, "o": 31, "u": 32, "[": 33, "i": 34, "p": 35, "l": 37,
        "j": 38, "'": 39, "k": 40, ";": 41, "\\": 42, ",": 43, "/": 44,
        "n": 45, "m": 46, ".": 47, "`": 50,
        "return": 36, "tab": 48, "space": 49, "delete": 51, "esc": 53,
        "f1": 122, "f2": 120, "f3": 99, "f4": 118, "f5": 96, "f6": 97,
        "f7": 98, "f8": 100, "f9": 101, "f10": 109, "f11": 103, "f12": 111,
        "left": 123, "right": 124, "down": 125, "up": 126,
        "home": 115, "end": 119, "pageup": 116, "pagedown": 121,
    ]

    static let codeToName: [UInt32: String] = {
        var map: [UInt32: String] = [:]
        for (name, code) in nameToCode { map[code] = name }
        return map
    }()

    static func carbonKeyCode(for chord: KeyChord) -> UInt32? {
        nameToCode[chord.key]
    }

    static func carbonModifiers(for chord: KeyChord) -> UInt32 {
        var flags: UInt32 = 0
        if chord.modifiers.contains(.command) { flags |= UInt32(cmdKey) }
        if chord.modifiers.contains(.shift) { flags |= UInt32(shiftKey) }
        if chord.modifiers.contains(.option) { flags |= UInt32(optionKey) }
        if chord.modifiers.contains(.control) { flags |= UInt32(controlKey) }
        return flags
    }

    /// Builds a KeyChord from an NSEvent (used by the shortcut recorder).
    static func chord(from event: NSEvent) -> KeyChord? {
        guard let name = codeToName[UInt32(event.keyCode)] else { return nil }
        var mods: ChordModifiers = []
        if event.modifierFlags.contains(.command) { mods.insert(.command) }
        if event.modifierFlags.contains(.option) { mods.insert(.option) }
        if event.modifierFlags.contains(.shift) { mods.insert(.shift) }
        if event.modifierFlags.contains(.control) { mods.insert(.control) }
        return KeyChord(key: name, modifiers: mods)
    }
}
