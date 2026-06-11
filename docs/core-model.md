# scrcap — portable core model

The three modules in `Sources/ScrcapCore/` (plus the stitch aligner) are pure
Swift with **zero AppKit/CoreGraphics imports**. They are the parts a future
Windows/Linux port (or Rust rewrite) reuses; everything else is per-OS. This
document is the spec a port implements.

## 1. Annotation model (`AnnotationModel.swift`)

- A document is **one bitmap + an append-only stack of vector shapes** with an
  undo cursor. There is no selection state, no transform handles ("draw stays
  armed", plan §03).
- `CorePoint` — image coordinates, **top-left origin, point units**. Retina
  scaling applies only at export (capture at 2×, annotate in points, export
  at 2×).
- `Shape { kind, colorIndex, start, end }`:
  - `arrow` — start = tail, end = tip. Rendered with a slight perpendicular
    bow (~10% of length) and a head scaled to drag length (18%, clamped 9–26 pt).
  - `rectangle` — start/end are opposite corners. Stroke-only, rounded 3 pt.
  - `counter(number:)` — `end` is the badge center; the number is **fixed at
    creation**. The next stamp's number = visible counters + 1, so undo
    decrements naturally.
  - `text(string:, size:)` — `start` is the top-left anchor; the string and
    point size are fixed at commit time (later preference changes don't reflow
    existing annotations). Newlines render as line breaks.
  - `colorIndex` — 0-based index into the 7-slot palette; resolution to actual
    color is presentation-layer.
- `AnnotationStack`: `append` truncates the redo tail; `undo`/`redo` move the
  cursor; `visible = shapes[0..<cursor]`. Trivially correct by construction.

## 2. Keymap engine (`KeymapEngine.swift`)

- `KeyChord = (key name, modifier set)`. Key names are abstract lowercase
  strings ("2", "r", "f5", "space", "esc") — OS keycode translation lives in
  the platform layer (`KeyCodeMap.swift` on macOS).
- Storage form: `"opt+shift+2"` (canonical modifier order ctrl, opt, shift,
  cmd). Display form: `"⌥⇧2"`.
- `Keymap` maps `AppAction → KeyChord` with conflict detection: binding an
  in-use chord *steals* it (the old action becomes unbound) and reports the
  victim so UI can warn. `systemReserved` lists OS-owned chords to reject.
- Defaults: ⌥⇧1 fullscreen, ⌥⇧2 region, ⌥⇧3 window, ⌥⇧4 scrolling, ⌥⇧R
  repeat-last — chosen to avoid macOS ⌘⇧3/4/5.

## 3. Settings schema (`Settings.swift`)

- One versioned JSON file (`settings.json`), human-readable and hand-editable.
  `schemaVersion` + explicit migration functions; unknown/corrupt files fall
  back to defaults. Writes are atomic.
- Fields: hotkeys (action → chord string), per-mode after-capture behavior
  (openEditor / copyOnly / both), 7-color palette as `#RRGGBB` (slot 0 = boot
  default red), Esc behavior, stroke width, window shadow toggle, save folder,
  filename pattern (`{date}`/`{time}` tokens), export scale, scrolling max
  height, launch at login.
- macOS location: `~/Library/Application Support/scrcap/settings.json`.

## 4. Stitch engine (`StitchEngine.swift`)

Scrolling capture's alignment logic, pure over per-row `UInt64` hash arrays
(FNV-1a over raw row bytes):

- `align(accumulated, frame)` — largest-overlap suffix/prefix match with a 98%
  tolerance (antialiasing noise); ≥16-row minimum. Returns where unseen
  content begins; `frame.count` means "nothing new" (bottom/bounce); `nil`
  means unalignable (non-scrollable or content replaced).
- `fixedEdges(frames)` — rows identical at the same index across all frames
  are sticky headers/footers; the platform layer crops them from non-first
  frames before aligning.

Platform layer responsibilities (macOS: `ScrollCaptureController.swift`):
scroll injection, settle detection (two identical frames or 600 ms timeout),
max-height cap, progress UI, Esc/Stop.
