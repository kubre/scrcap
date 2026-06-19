# Windows parity evidence manifest

- Source commit: `3e3eca3bf8a0d7b17f5cd5f04830b4c0db5a478e`
- Source branch state: detached worktree at `win_port` / `origin/win_port`
- Evidence date: `2026-06-19`
- Package mode: Release loose build from `src/Scrcap.Windows.UI/bin/Release/net8.0-windows10.0.19041.0`
- OS: Microsoft Windows 11 Home Single Language, version `10.0.26200`, build `26200`, 64-bit
- GPUs: NVIDIA GeForce RTX 3050 Laptop GPU driver `32.0.15.7628`; AMD Radeon(TM) Graphics driver `30.0.13002.19003`
- Display/DPI verified: one primary display, `1920 × 1080` bounds, `1920 × 1032` working area, 96 DPI / 100% scale
- Deterministic settings directory: `docs/windows-parity/settings-deterministic`
- Editor reference image: `docs/windows-parity/reference-sample.png`

## Capture methods

- Full-HWND evidence uses `tools/Capture-WindowEvidence.ps1`, which launches the real app, finds the visible HWND, and captures desktop pixels from the HWND bounds plus shadow padding with `Graphics.CopyFromScreen`. It does not use WPF `RenderTargetBitmap`.
- Client-only evidence uses the existing `--dump-window-png` path, now treated as a client-only WPF visual-tree baseline. It is useful for deterministic UI content checks but excludes native frame/shadow pixels.
- Each `*-comparison.png` places the full-HWND capture next to the client-only capture. The full-HWND side visibly includes outer frame/shadow padding; the client-only side does not.
- Runtime chrome behavior was smoke-tested with `tools/Test-WindowChromeInteractions.ps1`. That helper verified HWND hit tests for empty caption space (`HTCAPTION`), right resize edge (`HTRIGHT`), and the custom maximize target (`HTMAXBUTTON`); maximize/restore/minimize; maximized bounds equal the taskbar working area bottom; the system-menu command path; and Alt+F4 close.

## Evidence matrix

| Scenario | Theme | Reference/client-only | Fresh Windows full-HWND | Comparison | Metadata |
| --- | --- | --- | --- | --- | --- |
| Editor sample | Light | `editor-light-client-only.png` | `editor-light-full-hwnd.png` | `editor-light-comparison.png` | `editor-light-full-hwnd.json` |
| Editor sample | Dark | `editor-dark-client-only.png` | `editor-dark-full-hwnd.png` | `editor-dark-comparison.png` | `editor-dark-full-hwnd.json` |
| Preferences General | Light | `preferences-general-light-client-only.png` | `preferences-general-light-full-hwnd.png` | `preferences-general-light-comparison.png` | `preferences-general-light-full-hwnd.json` |
| Preferences Capture | Light | `preferences-capture-light-client-only.png` | `preferences-capture-light-full-hwnd.png` | `preferences-capture-light-comparison.png` | `preferences-capture-light-full-hwnd.json` |
| Preferences Shortcuts | Light | `preferences-shortcuts-light-client-only.png` | `preferences-shortcuts-light-full-hwnd.png` | `preferences-shortcuts-light-comparison.png` | `preferences-shortcuts-light-full-hwnd.json` |
| Preferences Editor | Light | `preferences-editor-light-client-only.png` | `preferences-editor-light-full-hwnd.png` | `preferences-editor-light-comparison.png` | `preferences-editor-light-full-hwnd.json` |
| Preferences Output | Light | `preferences-output-light-client-only.png` | `preferences-output-light-full-hwnd.png` | `preferences-output-light-comparison.png` | `preferences-output-light-full-hwnd.json` |
| Preferences About | Light | `preferences-about-light-client-only.png` | `preferences-about-light-full-hwnd.png` | `preferences-about-light-comparison.png` | `preferences-about-light-full-hwnd.json` |
| Preferences General | Dark | `preferences-general-dark-client-only.png` | `preferences-general-dark-full-hwnd.png` | `preferences-general-dark-comparison.png` | `preferences-general-dark-full-hwnd.json` |
| Preferences Capture | Dark | `preferences-capture-dark-client-only.png` | `preferences-capture-dark-full-hwnd.png` | `preferences-capture-dark-comparison.png` | `preferences-capture-dark-full-hwnd.json` |
| Preferences Shortcuts | Dark | `preferences-shortcuts-dark-client-only.png` | `preferences-shortcuts-dark-full-hwnd.png` | `preferences-shortcuts-dark-comparison.png` | `preferences-shortcuts-dark-full-hwnd.json` |
| Preferences Editor | Dark | `preferences-editor-dark-client-only.png` | `preferences-editor-dark-full-hwnd.png` | `preferences-editor-dark-comparison.png` | `preferences-editor-dark-full-hwnd.json` |
| Preferences Output | Dark | `preferences-output-dark-client-only.png` | `preferences-output-dark-full-hwnd.png` | `preferences-output-dark-comparison.png` | `preferences-output-dark-full-hwnd.json` |
| Preferences About | Dark | `preferences-about-dark-client-only.png` | `preferences-about-dark-full-hwnd.png` | `preferences-about-dark-comparison.png` | `preferences-about-dark-full-hwnd.json` |

## Known deltas and gaps

- Verified screenshots show exactly one 36-DIP scrcap header and one close affordance in editor and preferences windows. No native title strip appears above the shared header.
- Full-HWND captures are intentionally larger than client-only dumps because they include desktop pixels around the native HWND bounds and shadow padding.
- Only 100% DPI was available in this environment. 150% and 200% DPI are blocked verification items, not covered.
- Only one monitor was available. Mixed-monitor and mixed-DPI drag verification is blocked.
- Windows 10 outer-corner behavior was not available; this run covers Windows 11 build `26200` only.
- High contrast mode was not toggled for screenshot verification. The DWM helper avoids forcing immersive dark mode while `SystemParameters.HighContrast` is true, but manual high-contrast visual QA remains open.
- Runtime smoke confirmed caption hit testing, resize-border hit testing, `HTMAXBUTTON` for the custom maximize button, maximize/restore/minimize, taskbar-aware maximization, system-menu command routing, and Alt+F4 close. The smoke result reported `maximizedBottom = 1032` and `workingAreaBottom = 1032`.
- Manual interaction checks for Win+Arrow snapping, hover snap-layout UI, double-click maximize/restore with physical pointer input, drag-resize with physical pointer input, and cross-monitor drag are not fully covered by these screenshots/scripts.
