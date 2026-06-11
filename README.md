# scrcap

Capture at the speed of thought. A Shottr-inspired screenshot tool that is
tiny, instant, and keyboard-first. macOS native (Swift + AppKit, zero
dependencies); portable core by design. Full plan: `plan.html`.

## Build & run

Requires macOS 14+ and a Swift 6 toolchain (Command Line Tools are enough).

```sh
swift run scrcap              # run directly (dev)
swift run scrcap-core-tests   # run the portable-core test suite
scripts/make_app.sh           # release build → dist/scrcap.app (+ size budget gate)
```

First capture prompts for **Screen Recording** permission (System Settings →
Privacy & Security); scrcap detects the grant live. Scrolling capture
additionally asks for **Accessibility** the first time you use it — never up
front.

> When running via `swift run`, macOS attributes permissions to your terminal.
> Use `dist/scrcap.app` for the real permission flow.

**One-time:** run `scripts/make_dev_cert.sh` to create a stable self-signed
signing identity. Without it the bundle is ad-hoc signed, and macOS treats
every rebuild as a new app — forcing you to re-grant Screen Recording each
time. With it, grant once and rebuilds keep the permission.

## Default hotkeys (all remappable in Preferences → Shortcuts)

| Mode | Hotkey | Notes |
|---|---|---|
| Fullscreen | ⌥⇧1 | display under cursor, zero UI |
| Region | ⌥⇧2 | crosshair + live coords, Space moves selection mid-drag, Esc aborts |
| Window | ⌥⇧3 | hover highlight, ⇥ cycles overlapping windows |
| Scrolling | ⌥⇧4 | select a region, scrcap scrolls & stitches |
| Repeat last | ⌥⇧R | re-captures the previous region/window/screen |

## Editor

Every capture opens the editor (per-mode override in Preferences). Draw stays
armed — no selection state; got it wrong → ⌘Z and draw again.

| Key | Action |
|---|---|
| Q / W / E / R | arrow / rectangle / counter / text tool |
| 1–7 | color (red default on every new shot) |
| ⇧-drag | constrain rectangle to square |
| text tool | click to type · ⏎ confirms · ⇧⏎ new line · Esc cancels entry |
| Esc | **copy to clipboard + close** (also a toolbar button, configurable) |
| ⌘Z / ⇧⌘Z | undo / redo |
| ⌘C | copy, keep editor open |
| ⌘S | save as PNG… (also a toolbar button) |
| ⌘W | discard & close |
| ⌥-drag | drag flattened PNG out (Slack, Finder, mail…) |

Drag the toolbar's empty background to move the editor window.

## Layout

- `Sources/ScrcapCore/` — portable core: annotation model, keymap engine,
  settings schema, stitch aligner. No AppKit. Spec: `docs/core-model.md`.
- `Sources/scrcap/` — macOS app: ScreenCaptureKit capture, Carbon hotkeys,
  overlay, editor, exporter, SwiftUI preferences.
- `Sources/scrcap-core-tests/` — core test suite (plain executable; the
  CLT-only toolchain ships no XCTest).
- `scripts/` — `make_app.sh` (bundle + ad-hoc sign), `check_budgets.sh`
  (size gate, < 5 MB).

Settings live at `~/Library/Application Support/scrcap/settings.json` —
versioned, human-readable, dotfiles-friendly.
