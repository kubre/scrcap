import Foundation
import ScrcapCore

// MARK: - AnnotationModel

func makeShape(_ kind: ShapeKind = .arrow, color: Int = 0) -> Shape {
    Shape(kind: kind, colorIndex: color, start: CorePoint(x: 0, y: 0), end: CorePoint(x: 10, y: 10))
}

print("AnnotationModel")

test("append makes visible") {
    var stack = AnnotationStack()
    stack.append(makeShape())
    stack.append(makeShape(.rectangle))
    checkEqual(stack.visible.count, 2)
    check(stack.canUndo)
    check(!stack.canRedo)
}

test("undo/redo") {
    var stack = AnnotationStack()
    stack.append(makeShape())
    stack.append(makeShape(.rectangle))
    check(stack.undo())
    checkEqual(stack.visible.count, 1)
    check(stack.canRedo)
    check(stack.redo())
    checkEqual(stack.visible.count, 2)
    check(!stack.redo())
}

test("undo on empty fails") {
    var stack = AnnotationStack()
    check(!stack.undo())
}

test("new draw truncates redo tail") {
    var stack = AnnotationStack()
    stack.append(makeShape())
    stack.append(makeShape(.rectangle))
    stack.undo()
    stack.append(makeShape(.counter(number: 1)))
    check(!stack.canRedo)
    checkEqual(stack.visible.count, 2)
    checkEqual(stack.visible.last?.kind, .counter(number: 1))
}

test("counter numbering follows undo") {
    var stack = AnnotationStack()
    checkEqual(stack.nextCounterNumber, 1)
    stack.append(makeShape(.counter(number: stack.nextCounterNumber)))
    stack.append(makeShape(.counter(number: stack.nextCounterNumber)))
    checkEqual(stack.nextCounterNumber, 3)
    stack.undo()
    checkEqual(stack.nextCounterNumber, 2)
    stack.append(makeShape(.arrow)) // arrows don't affect numbering
    checkEqual(stack.nextCounterNumber, 2)
}

test("shape codable round trip") {
    let s = makeShape(.counter(number: 7), color: 3)
    let data = try JSONEncoder().encode(s)
    let back = try JSONDecoder().decode(Shape.self, from: data)
    checkEqual(back, s)
}

test("text shape codable round trip") {
    let s = makeShape(.text(string: "hello\nworld", size: 18), color: 4)
    let data = try JSONEncoder().encode(s)
    let back = try JSONDecoder().decode(Shape.self, from: data)
    checkEqual(back, s)
}

test("text shapes don't affect counter numbering") {
    var stack = AnnotationStack()
    stack.append(makeShape(.text(string: "note", size: 16)))
    checkEqual(stack.nextCounterNumber, 1)
}

// MARK: - KeymapEngine

print("\nKeymapEngine")

test("parse chord") {
    checkEqual(KeyChord(string: "opt+shift+2"), KeyChord(key: "2", modifiers: [.option, .shift]))
}

test("parse aliases") {
    checkEqual(KeyChord(string: "alt+SHIFT+R"), KeyChord(key: "r", modifiers: [.option, .shift]))
    checkEqual(KeyChord(string: "command+z"), KeyChord(key: "z", modifiers: [.command]))
    checkEqual(KeyChord(string: "ctrl+alt+space"), KeyChord(key: "space", modifiers: [.control, .option]))
}

test("parse rejects garbage") {
    checkNil(KeyChord(string: ""))
    checkNil(KeyChord(string: "opt+shift")) // no key
    checkNil(KeyChord(string: "a+b"))       // two keys
}

test("string round trip") {
    let chord = KeyChord(key: "2", modifiers: [.option, .shift])
    checkEqual(KeyChord(string: chord.stringValue), chord)
}

test("display value") {
    checkEqual(KeyChord(key: "2", modifiers: [.option, .shift]).displayValue, "⌥⇧2")
    checkEqual(KeyChord(key: "z", modifiers: [.command, .shift]).displayValue, "⇧⌘Z")
}

test("defaults have no conflicts and avoid system shortcuts") {
    let map = Keymap.defaults
    let chords = Array(map.bindings.values)
    checkEqual(Set(chords).count, chords.count)
    for chord in chords {
        check(!Keymap.isSystemReserved(chord), "\(chord.displayValue) collides with macOS")
    }
}

test("capture options follow default shortcut order") {
    checkEqual(CaptureMode.captureOrder, [.region, .window, .fullscreen, .scrolling])
    checkEqual(
        AppAction.shortcutOrder,
        [.captureRegion, .captureWindow, .captureFullscreen, .captureScrolling, .captureDelayed, .repeatLast]
    )
}

test("default capture shortcuts are ordered region window screen scrolling") {
    let map = Keymap.defaults
    checkEqual(map.chord(for: .captureRegion)?.displayValue, "⌥⇧1")
    checkEqual(map.chord(for: .captureWindow)?.displayValue, "⌥⇧2")
    checkEqual(map.chord(for: .captureFullscreen)?.displayValue, "⌥⇧3")
    checkEqual(map.chord(for: .captureScrolling)?.displayValue, "⌥⇧4")
}

test("conflict detection and stealing") {
    var map = Keymap.defaults
    let regionChord = map.chord(for: .captureRegion)!
    checkEqual(map.conflict(for: regionChord), .captureRegion)
    checkNil(map.conflict(for: regionChord, excluding: .captureRegion))

    let stolen = map.set(regionChord, for: .captureWindow)
    checkEqual(stolen, .captureRegion)
    checkNil(map.chord(for: .captureRegion))
    checkEqual(map.chord(for: .captureWindow), regionChord)
}

test("system reserved") {
    check(Keymap.isSystemReserved(KeyChord(string: "cmd+shift+4")!))
    check(!Keymap.isSystemReserved(KeyChord(string: "opt+shift+4")!))
}

// MARK: - Settings

print("\nSettings")

func withTempDir(_ body: (URL) throws -> Void) rethrows {
    let dir = FileManager.default.temporaryDirectory
        .appendingPathComponent("scrcap-tests-\(UUID().uuidString)", isDirectory: true)
    defer { try? FileManager.default.removeItem(at: dir) }
    try body(dir)
}

test("defaults when no file") {
    withTempDir { dir in
        let store = SettingsStore(directory: dir)
        checkEqual(store.settings, .defaults)
        checkEqual(store.settings.paletteHex.count, 5)
        checkEqual(store.settings.paletteHex[0], "#FF3B30")
        checkEqual(store.settings.windowCaptureTarget, .active)
        check(store.settings.autoExpandCanvas)
        checkEqual(store.settings.canvasExtensionBackgroundHex, "#FFFFFF")
    }
}

test("save and reload") {
    withTempDir { dir in
        let store = SettingsStore(directory: dir)
        store.update { $0.strokeWidth = 5; $0.escBehavior = .closeOnly }
        let reloaded = SettingsStore(directory: dir)
        checkEqual(reloaded.settings.strokeWidth, 5)
        checkEqual(reloaded.settings.escBehavior, .closeOnly)
    }
}

test("keymap round trip through settings") {
    withTempDir { dir in
        let store = SettingsStore(directory: dir)
        var map = store.settings.keymap
        let newChord = KeyChord(string: "ctrl+opt+5")!
        map.set(newChord, for: .captureRegion)
        store.update { $0.apply(map) }
        let reloaded = SettingsStore(directory: dir)
        checkEqual(reloaded.settings.keymap.chord(for: .captureRegion), newChord)
    }
}

test("legacy default capture hotkeys normalize to current order") {
    var regionFirstLegacy = Settings.defaults
    regionFirstLegacy.hotkeys[AppAction.captureRegion.rawValue] = "opt+shift+1"
    regionFirstLegacy.hotkeys[AppAction.captureFullscreen.rawValue] = "opt+shift+2"
    regionFirstLegacy.hotkeys[AppAction.captureWindow.rawValue] = "opt+shift+3"
    regionFirstLegacy.normalizeLegacyDefaultCaptureHotkeys()
    checkEqual(regionFirstLegacy.keymap.chord(for: .captureRegion)?.displayValue, "⌥⇧1")
    checkEqual(regionFirstLegacy.keymap.chord(for: .captureWindow)?.displayValue, "⌥⇧2")
    checkEqual(regionFirstLegacy.keymap.chord(for: .captureFullscreen)?.displayValue, "⌥⇧3")

    var fullscreenFirstLegacy = Settings.defaults
    fullscreenFirstLegacy.hotkeys[AppAction.captureFullscreen.rawValue] = "opt+shift+1"
    fullscreenFirstLegacy.hotkeys[AppAction.captureRegion.rawValue] = "opt+shift+2"
    fullscreenFirstLegacy.hotkeys[AppAction.captureWindow.rawValue] = "opt+shift+3"
    fullscreenFirstLegacy.normalizeLegacyDefaultCaptureHotkeys()
    checkEqual(fullscreenFirstLegacy.keymap.chord(for: .captureRegion)?.displayValue, "⌥⇧1")
    checkEqual(fullscreenFirstLegacy.keymap.chord(for: .captureWindow)?.displayValue, "⌥⇧2")
    checkEqual(fullscreenFirstLegacy.keymap.chord(for: .captureFullscreen)?.displayValue, "⌥⇧3")

    var custom = Settings.defaults
    custom.hotkeys[AppAction.captureRegion.rawValue] = "opt+shift+9"
    custom.normalizeLegacyDefaultCaptureHotkeys()
    checkEqual(custom.keymap.chord(for: .captureRegion)?.displayValue, "⌥⇧9")
}

test("corrupt file falls back to defaults") {
    try withTempDir { dir in
        try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        try Data("not json".utf8).write(to: dir.appendingPathComponent("settings.json"))
        let store = SettingsStore(directory: dir)
        checkEqual(store.settings, .defaults)
    }
}

test("missing schemaVersion falls back to defaults") {
    try withTempDir { dir in
        try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        try Data("{\"strokeWidth\": 9}".utf8).write(to: dir.appendingPathComponent("settings.json"))
        let store = SettingsStore(directory: dir)
        checkEqual(store.settings, .defaults)
    }
}

test("future schemaVersion falls back to defaults") {
    try withTempDir { dir in
        try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        var json = try JSONSerialization.jsonObject(
            with: JSONEncoder().encode(Settings.defaults)
        ) as! [String: Any]
        json["schemaVersion"] = Settings.currentSchemaVersion + 1
        json["strokeWidth"] = 5.0
        try JSONSerialization.data(withJSONObject: json)
            .write(to: dir.appendingPathComponent("settings.json"))

        let store = SettingsStore(directory: dir)
        checkEqual(store.settings, .defaults)
    }
}

test("decodable settings are normalized on load") {
    try withTempDir { dir in
        try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        var settings = Settings.defaults
        settings.paletteHex = ["#abcdef", "not-a-color"]
        settings.canvasExtensionBackgroundHex = "00ff00"
        settings.strokeWidth = 99
        settings.textSize = -4
        settings.exportScale = 9
        settings.scrollingMaxHeight = 999_999
        try JSONEncoder().encode(settings).write(to: dir.appendingPathComponent("settings.json"))

        let store = SettingsStore(directory: dir)
        checkEqual(store.settings.paletteHex, [
            "#ABCDEF", "#FF9500", "#34C759", "#0A84FF", "#1C1C1E",
        ])
        checkEqual(store.settings.canvasExtensionBackgroundHex, "#00FF00")
        checkEqual(store.settings.strokeWidth, Settings.maxStrokeWidth)
        checkEqual(store.settings.textSize, Settings.minTextSize)
        checkEqual(store.settings.exportScale, 2)
        checkEqual(store.settings.scrollingMaxHeight, Settings.maxScrollingMaxHeight)
    }
}

test("settings updates are normalized before saving") {
    withTempDir { dir in
        let store = SettingsStore(directory: dir)
        store.update {
            $0.paletteHex = []
            $0.strokeWidth = -10
            $0.textSize = 200
            $0.exportScale = 0
            $0.scrollingMaxHeight = 10
        }

        let reloaded = SettingsStore(directory: dir)
        checkEqual(reloaded.settings.paletteHex, Settings.defaultPalette)
        checkEqual(reloaded.settings.strokeWidth, Settings.minStrokeWidth)
        checkEqual(reloaded.settings.textSize, Settings.maxTextSize)
        checkEqual(reloaded.settings.exportScale, 2)
        checkEqual(reloaded.settings.scrollingMaxHeight, Settings.minScrollingMaxHeight)
    }
}

test("v1 settings migrate to current schema with text, window, and canvas defaults") {
    try withTempDir { dir in
        try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        // Build a faithful v1 file: today's defaults minus fields added later.
        var json = try JSONSerialization.jsonObject(
            with: JSONEncoder().encode(Settings.defaults)
        ) as! [String: Any]
        json["schemaVersion"] = 1
        json.removeValue(forKey: "textSize")
        json.removeValue(forKey: "textEnterBehavior")
        json.removeValue(forKey: "windowCaptureTarget")
        json.removeValue(forKey: "autoExpandCanvas")
        json.removeValue(forKey: "canvasExtensionBackgroundHex")
        json["strokeWidth"] = 5.0 // a non-default to prove the rest survives
        try JSONSerialization.data(withJSONObject: json)
            .write(to: dir.appendingPathComponent("settings.json"))

        let store = SettingsStore(directory: dir)
        checkEqual(store.settings.schemaVersion, Settings.currentSchemaVersion)
        checkEqual(store.settings.textSize, 16)
        checkEqual(store.settings.textEnterBehavior, .newline)
        checkEqual(store.settings.windowCaptureTarget, .active)
        check(store.settings.autoExpandCanvas)
        checkEqual(store.settings.canvasExtensionBackgroundHex, "#FFFFFF")
        checkEqual(store.settings.strokeWidth, 5)
    }
}

test("v2 settings migrate to current schema with new line Return, active window, and canvas defaults") {
    try withTempDir { dir in
        try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        var json = try JSONSerialization.jsonObject(
            with: JSONEncoder().encode(Settings.defaults)
        ) as! [String: Any]
        json["schemaVersion"] = 2
        json.removeValue(forKey: "textEnterBehavior")
        json.removeValue(forKey: "windowCaptureTarget")
        json.removeValue(forKey: "autoExpandCanvas")
        json.removeValue(forKey: "canvasExtensionBackgroundHex")
        json["strokeWidth"] = 5.0
        try JSONSerialization.data(withJSONObject: json)
            .write(to: dir.appendingPathComponent("settings.json"))

        let store = SettingsStore(directory: dir)
        checkEqual(store.settings.schemaVersion, Settings.currentSchemaVersion)
        checkEqual(store.settings.textEnterBehavior, .newline)
        checkEqual(store.settings.windowCaptureTarget, .active)
        check(store.settings.autoExpandCanvas)
        checkEqual(store.settings.canvasExtensionBackgroundHex, "#FFFFFF")
        checkEqual(store.settings.strokeWidth, 5)
    }
}

test("v3 settings migrate to current schema with active window and canvas defaults") {
    try withTempDir { dir in
        try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        var json = try JSONSerialization.jsonObject(
            with: JSONEncoder().encode(Settings.defaults)
        ) as! [String: Any]
        json["schemaVersion"] = 3
        json.removeValue(forKey: "windowCaptureTarget")
        json.removeValue(forKey: "autoExpandCanvas")
        json.removeValue(forKey: "canvasExtensionBackgroundHex")
        json["strokeWidth"] = 5.0
        try JSONSerialization.data(withJSONObject: json)
            .write(to: dir.appendingPathComponent("settings.json"))

        let store = SettingsStore(directory: dir)
        checkEqual(store.settings.schemaVersion, Settings.currentSchemaVersion)
        checkEqual(store.settings.windowCaptureTarget, .active)
        check(store.settings.autoExpandCanvas)
        checkEqual(store.settings.canvasExtensionBackgroundHex, "#FFFFFF")
        checkEqual(store.settings.strokeWidth, 5)
    }
}

test("v4 settings migrate to current schema with canvas defaults") {
    try withTempDir { dir in
        try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        var json = try JSONSerialization.jsonObject(
            with: JSONEncoder().encode(Settings.defaults)
        ) as! [String: Any]
        json["schemaVersion"] = 4
        json.removeValue(forKey: "autoExpandCanvas")
        json.removeValue(forKey: "canvasExtensionBackgroundHex")
        json["strokeWidth"] = 5.0
        try JSONSerialization.data(withJSONObject: json)
            .write(to: dir.appendingPathComponent("settings.json"))

        let store = SettingsStore(directory: dir)
        checkEqual(store.settings.schemaVersion, Settings.currentSchemaVersion)
        check(store.settings.autoExpandCanvas)
        checkEqual(store.settings.canvasExtensionBackgroundHex, "#FFFFFF")
        checkEqual(store.settings.strokeWidth, 5)
    }
}

test("behavior for mode defaults to editor") {
    var s = Settings.defaults
    checkEqual(s.behavior(for: .region), .openEditor)
    s.afterCapture[CaptureMode.fullscreen.rawValue] = .copyOnly
    checkEqual(s.behavior(for: .fullscreen), .copyOnly)
    checkEqual(s.behavior(for: .region), .openEditor)
}

// MARK: - StitchEngine

print("\nStitchEngine")

func docRows(_ range: Range<Int>) -> [UInt64] {
    range.map { UInt64($0) &* 0x9E3779B97F4A7C15 &+ 1 }
}

test("align finds overlap") {
    let alignment = StitchEngine.align(accumulated: docRows(0..<100), frame: docRows(70..<170))
    checkEqual(alignment, .init(newContentStart: 30))
}

test("identical frame yields no new rows") {
    let alignment = StitchEngine.align(accumulated: docRows(0..<100), frame: docRows(0..<100))
    checkEqual(alignment?.newContentStart, 100)
}

test("bottom reached: tail frame adds nothing") {
    let alignment = StitchEngine.align(accumulated: docRows(0..<200), frame: docRows(100..<200))
    checkEqual(alignment?.newContentStart, 100)
}

test("unrelated content fails to align") {
    checkNil(StitchEngine.align(accumulated: docRows(0..<100), frame: docRows(5000..<5100)))
}

test("tolerance allows small noise") {
    var frame = docRows(40..<140)
    frame[10] = 0xDEAD // one noisy row inside the 60-row overlap (> 98% match)
    let alignment = StitchEngine.align(accumulated: docRows(0..<100), frame: frame)
    checkEqual(alignment, .init(newContentStart: 60))
}

test("perceptual alignment tolerates rerasterized rows") {
    func signature(_ row: Int, noise: Int = 0) -> StitchEngine.RowSignature {
        StitchEngine.RowSignature(bins: (0..<32).map { bucket in
            UInt8(min(255, 24 + ((row * 37 + bucket * 11) % 200) + noise))
        })
    }
    let accumulated = (0..<100).map { signature($0) }
    let frame = (40..<140).map { signature($0, noise: $0 < 100 ? 2 : 0) }
    let alignment = StitchEngine.align(accumulated: accumulated, frame: frame)
    checkEqual(alignment, .init(newContentStart: 60))
}

test("fixedEdges detects sticky header") {
    let header = docRows(9000..<9010)
    let frames = [
        header + docRows(0..<90),
        header + docRows(60..<150),
        header + docRows(120..<210),
    ]
    let edges = StitchEngine.fixedEdges(frames: frames)
    checkEqual(edges.top, 10)
    checkEqual(edges.bottom, 0)
}

test("fixedEdges single frame is zero") {
    let edges = StitchEngine.fixedEdges(frames: [docRows(0..<50)])
    checkEqual(edges.top, 0)
    checkEqual(edges.bottom, 0)
}

test("rowHash differs on byte change") {
    var a: [UInt8] = [1, 2, 3, 4, 5, 6, 7, 8]
    let h1 = a.withUnsafeBytes { StitchEngine.rowHash($0) }
    a[3] = 9
    let h2 = a.withUnsafeBytes { StitchEngine.rowHash($0) }
    check(h1 != h2)
}

// MARK: - ReleaseVersion

print("\nReleaseVersion")

test("release versions ignore leading v") {
    checkEqual(ReleaseVersion("v1.2.3"), ReleaseVersion("1.2.3"))
}

test("release versions compare numeric components") {
    check(ReleaseVersion("1.10.0")! > ReleaseVersion("1.9.9")!)
    check(ReleaseVersion("2.0")! > ReleaseVersion("1.99.99")!)
}

test("release versions treat missing trailing zeroes as equal") {
    checkEqual(ReleaseVersion("1.0"), ReleaseVersion("1.0.0"))
}

test("release versions strip metadata") {
    checkEqual(ReleaseVersion("v1.2.3+4"), ReleaseVersion("1.2.3"))
    checkEqual(ReleaseVersion("v1.2.3-beta"), ReleaseVersion("1.2.3"))
}

test("release versions reject non-numeric versions") {
    checkNil(ReleaseVersion("dev"))
    checkNil(ReleaseVersion("1.x"))
    checkNil(ReleaseVersion("1..2"))
}

test("updateAvailable only when latest is newer") {
    check(ReleaseVersion.updateAvailable(current: "1.0", latest: "v1.0.1"))
    check(!ReleaseVersion.updateAvailable(current: "1.0.1", latest: "v1.0.1"))
    check(!ReleaseVersion.updateAvailable(current: "dev", latest: "v1.0.1"))
}

// MARK: - FilenameGenerator

print("\nFilenameGenerator")

test("token expansion") {
    var comps = DateComponents()
    comps.year = 2026; comps.month = 6; comps.day = 12
    comps.hour = 14; comps.minute = 32; comps.second = 5
    let now = Calendar(identifier: .gregorian).date(from: comps)!
    let result = FilenameGenerator.filename(pattern: "scrcap-{date}-{time}", now: now)
    checkEqual(result, "scrcap-2026-06-12-14.32.05.png")
}

test("no tokens passes through unchanged") {
    var comps = DateComponents()
    comps.year = 2026; comps.month = 1; comps.day = 1
    comps.hour = 0; comps.minute = 0; comps.second = 0
    let now = Calendar(identifier: .gregorian).date(from: comps)!
    let result = FilenameGenerator.filename(pattern: "my-screenshot", now: now)
    checkEqual(result, "my-screenshot.png")
}

test("safeFilenameStem strips slashes") {
    checkEqual(FilenameGenerator.safeFilenameStem("a/b/c"), "a-b-c")
}

test("safeFilenameStem strips backslash and colon") {
    checkEqual(FilenameGenerator.safeFilenameStem("a\\b:c"), "a-b-c")
}

test("safeFilenameStem strips control characters") {
    checkEqual(FilenameGenerator.safeFilenameStem("a\u{01}b"), "a-b")
}

test("safeFilenameStem empty input returns scrcap") {
    checkEqual(FilenameGenerator.safeFilenameStem(""), "scrcap")
}

test("safeFilenameStem all-invalid input returns scrcap") {
    checkEqual(FilenameGenerator.safeFilenameStem("///"), "scrcap")
}

test("safeFilenameStem trims interior whitespace segments") {
    checkEqual(FilenameGenerator.safeFilenameStem("foo  /  bar"), "foo-bar")
}

test("available filename advances past collisions") {
    let existing = Set(["scrcap.png", "scrcap-2.png"])
    checkEqual(
        FilenameGenerator.availableFilename("scrcap.png", exists: existing.contains),
        "scrcap-3.png"
    )
}

TestRun.shared.finish()
