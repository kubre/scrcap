# scrcap — portable core model

`Sources/ScrcapCore/` contains six Foundation-only Swift files with no AppKit
or CoreGraphics imports. The Windows port mirrors this behavior in
`scrcap-windows/src/Scrcap.Core`; OS capture, UI, hotkeys, and rendering remain
platform-specific.

## 1. Annotation model (`AnnotationModel.swift`)

- A document is one bitmap plus an append-only stack of vector shapes and an
  undo cursor. Appending after undo truncates the redo tail.
- `CorePoint` uses image coordinates with a top-left origin, measured in
  points. Retina scaling happens at export.
- Shape kinds are arrow, rectangle, pixelate, numbered counter, and committed
  text. Text stores its point size; counters store their number.
- Every shape stores a five-color palette index and its creation size
  (`small`, `medium`, or `large`). Rendering and color resolution belong to the
  platform layer.

## 2. Filename generation (`FilenameGenerator.swift`)

- Expands `{date}` and `{time}` in a filename pattern and appends `.png`.
- Removes path separators, control characters, and newlines. An empty result
  falls back to `scrcap.png`.

## 3. Keymap engine (`KeymapEngine.swift`)

- `KeyChord` stores an abstract lowercase key name and modifier set. OS keycode
  translation stays in the platform layer.
- Storage uses canonical strings such as `opt+shift+2`; display uses macOS
  symbols such as `⌥⇧2`.
- Binding an occupied chord transfers it to the new action and reports the
  action that became unbound. Known OS-reserved chords are rejected by the UI.
- Defaults are ⌥⇧1 region, ⌥⇧2 window, ⌥⇧3 fullscreen, ⌥⇧4 scrolling,
  ⌥⇧5 delayed region, and ⌥⇧R repeat-last.

## 4. Release versions (`ReleaseVersion.swift`)

- Parses numeric dotted versions, accepting an optional `v` prefix and
  ignoring prerelease/build suffixes.
- Comparison pads missing components with zero, so `1.2` equals `1.2.0`.

## 5. Settings schema (`Settings.swift`)

- One human-readable, atomically written JSON file at
  `~/Library/Application Support/scrcap/settings.json` on macOS.
- Current schema version is **8**. Older supported schemas migrate forward;
  unknown future or corrupt schemas fall back to defaults.
- Current fields cover hotkeys, per-mode after-capture behavior, the five-color
  palette, Esc and text-Return behavior, stroke and text size, window target,
  shadow/cursor inclusion, window background transparency and color, canvas
  auto-expansion and fill color, save folder, filename pattern, export scale,
  scrolling height cap, launch at login, theme, delayed-capture seconds, and
  copy-notification suppression.
- Normalization clamps numeric values, validates colors, fixes palette length,
  constrains export scale, and upgrades legacy default capture shortcuts.

## 6. Stitch engine (`StitchEngine.swift`)

- Operates on per-row FNV-1a hashes; pixel access and scroll injection remain
  platform-specific.
- `align(accumulated:frame:)` finds the largest suffix/prefix overlap with a
  default 98% tolerance and 16-row minimum. A full-frame overlap means no new
  content; `nil` means the frames cannot be aligned.
- `fixedEdges(frames:)` finds identical top and bottom runs for sticky chrome
  that middle frames should omit.

## Windows port status

The current Windows port is a C#/.NET 8 solution under `scrcap-windows/` with a
portable core, Windows platform services, WPF UI, and dedicated test projects.
Region, selected/active window, monitor, and scrolling capture paths are
implemented; Windows Graphics Capture is primary with DC/BitBlt fallback.
Automated build, rendering, capture-fixture, UI/static, packaging, and
performance guardrails exist. Live tray/global-hotkey flows, warm latency,
cross-app clipboard behavior, high contrast, and Windows 10/11 visual QA still
require representative Windows machines before release.
