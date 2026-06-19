# Windows performance and release QA

Use this page as the release evidence template for Windows packaging and
performance. Store completed JSON output under `scrcap-windows/artifacts/` and
attach screenshots or screen recordings to the release checklist.

## Budgets

| Area | Budget | Evidence |
|---|---:|---|
| Warm hotkey to overlay visible latency | average under 60 ms | `hotkey_received_to_overlay_first_frame` diagnostic span plus high-frame-rate screen recording or ETW trace on a resident Release build. |
| Small region selection to captured pixels | typical under 150 ms | `selection_committed_to_capture_result` span and `capture_result` marker with backend used. |
| 4K monitor capture | typical under 300 ms | `selection_committed_to_capture_result` span or ETW timing on WGC path, excluding deliberate delay. |
| Captured pixels to editor interactive | under 150 ms | `capture_result_to_editor_first_interactive_render` diagnostic span after layout, focus, render, and app idle. |
| Warm pixelate redraw | 33 ms or less | `warm_pixelate_redraw` diagnostic span after cache warm-up on representative 4K region. |
| Flatten to PNG | record, compare against baseline | `flatten_to_png` diagnostic span for configured 1x and 2x output. |
| Scrolling retained bytes | under 256 MB cap | `scrolling_retained_bytes` diagnostic markers and scrolling stop reason. |
| Idle CPU after startup plus 10 seconds | under 0.2 percent average | `Measure-WindowsPerformance.ps1` automated idle sample. |
| Idle private memory for framework-dependent build | under 90 MB | `Measure-WindowsPerformance.ps1` automated idle sample. |
| Hidden overlay timers | 0 active animation/timer sources | `Test-ReleaseHardening.ps1` static guard plus optional runtime verification. |
| Selecting overlay timers | exactly 1 marching-ants source while selecting | `Test-ReleaseHardening.ps1` static guard plus optional runtime verification. |
| 500-shape editor rendering | no WPF element per shape | `Test-ReleaseHardening.ps1` static guard; optional render timing in this template. |

Set `SCRCAP_DIAGNOSTICS=1` before launching scrcap to emit the diagnostic spans
and markers to `System.Diagnostics.Trace`. Diagnostics are opt-in and are not
telemetry; they are not sent off-machine.

## Commands

Run the full release guardrail from `scrcap-windows/`:

```powershell
.\tools\Test-ReleaseGuardrails.ps1
```

Run the packaging script directly when validating release output:

```powershell
.\tools\Publish-Windows.ps1 -Mode framework-dependent
.\tools\Publish-Windows.ps1 -Mode self-contained
```

The publish script must write `scrcap.exe` and `LICENSE.txt` and must reject
PDBs, test baselines, temporary files, and intermediate build folders.

Collect idle CPU and memory evidence:

```powershell
.\tools\Measure-WindowsPerformance.ps1 -Configuration Release -IdleSeconds 10
```

Add manual numbers when they are available:

```powershell
.\tools\Measure-WindowsPerformance.ps1 `
  -Configuration Release `
  -ManualHotkeyToOverlayMs 42,44,41 `
  -ManualSelectionToCapturedPixelsMs 118,121,116 `
  -ManualFourKCaptureMs 240,246,238 `
  -ManualCaptureToEditorInteractiveMs 92,89,95 `
  -ManualWarmPixelateRedrawMs 18,19,17 `
  -ManualFlattenToPngMs 64,66,63 `
  -ManualHiddenOverlayTimerCount 0 `
  -ManualSelectingOverlayTimerCount 1 `
  -ManualEditor500ShapeRenderMs 95,97,94 `
  -ManualEditor500ShapeElementCount 1 `
  -ManualCpuGpu "CPU / GPU model" `
  -ManualWindowsBuild "Windows 11 build ..." `
  -ManualDpi "100 + 150 mixed" `
  -ManualBackend "WindowsGraphicsCapture" `
  -ManualPackageMode "framework-dependent"
```

## Timing evidence

Measure with the app already resident in the tray and hotkeys registered. Run
one warm-up first, then record at least three measured trials per timing. GDI
fallback captures must be labeled as GDI and must not be counted as WGC
regressions.

Evidence fields:

| Field | Value |
|---|---|
| Windows version | |
| CPU/GPU | |
| Display count and DPI | |
| Negative virtual-screen origin present | yes / no |
| App package mode | framework-dependent / self-contained |
| Capture backend requested | auto / WGC / GDI |
| Capture backend used | WGC / GDI |
| Fallback reason | |
| Warm-up steps | |
| Measurement method | diagnostic span / screen recording / ETW / other |
| Hotkey to overlay trial 1/2/3 ms | |
| Selection to capture trial 1/2/3 ms | |
| 4K capture trial 1/2/3 ms | |
| Capture to editor interactive trial 1/2/3 ms | |
| Warm pixelate redraw trial 1/2/3 ms | |
| Flatten to PNG trial 1/2/3 ms | |
| Averages and pass/fail | |

## Diagnostic spans

| Span or marker | Meaning |
|---|---|
| `hotkey_received_to_overlay_first_frame` | Hotkey callback to first rendered overlay frame. Fullscreen and repeat-last do not show an overlay. |
| `selection_committed_to_capture_result` | Selection accepted to returned capture result. |
| `capture_result` | Backend, fallback reason, bounds, DPI, output pixels, and scrolling stop reason for the result. |
| `capture_result_to_editor_first_interactive_render` | Capture result handoff to editor after first render/app-idle focus path. |
| `warm_pixelate_redraw` | Cached pixelate bitmap draw path. Cold cache population is intentionally separate. |
| `flatten_to_png` | One export/output boundary PNG encode. |
| `scrolling_retained_bytes` | Retained scrolling memory estimate, frame count, output height, cap, and stop reason. |

## Idle CPU and memory

Run the idle measurement with no editor, preferences, overlay, or capture HUD
open. The script starts the tray-resident app with an isolated settings
directory, waits for startup, samples CPU for 10 seconds, records private memory,
and then terminates the process.

Current RogStrix evidence from 2026-06-19 is stored at
`docs/windows-parity/windows-performance-current.json`. It covers a Release
loose framework-dependent build on Windows 11 Home Single Language build 26200,
one 1920 by 1080 display at 100 percent / 96 DPI, idle CPU `0`, and private
memory `14.23 MB`. Manual timing fields remain null because this thread did not
collect three real hotkey/capture/pixelate trials.

Evidence fields:

| Field | Value |
|---|---|
| JSON path | |
| Idle CPU percent | |
| Private memory MB | |
| Pass CPU budget | yes / no |
| Pass memory budget | yes / no |

## Overlay timers

The overlay uses a source-level `DispatcherTimer` for marching ants. The
release-hardening guard fails if a perpetual XAML storyboard returns or if the
timer no longer has explicit start/stop calls. Runtime validation with WPF
diagnostics can supplement the static gate:

| State | Expected |
|---|---|
| App resident, overlay hidden | no active overlay windows and 0 overlay animation/timer sources |
| Overlay visible before drag | no marching-ants animation source |
| Region selection in progress | exactly one marching-ants animation source |
| Selection committed or Esc cancelled | overlay hidden and animation/timer source stopped |

Current implementation note: the timer starts on active drag and stops on mouse
up, Esc cancellation, and window close.

## 500-shape editor rendering

The editor canvas must draw annotations with `DrawingContext` from
`EditorCanvas.OnRender` and must not create a WPF `Line`, `Rectangle`, `Path`,
`Ellipse`, or `TextBlock` per annotation. `Test-ReleaseHardening.ps1` checks that
static invariant. For manual timing, open a sample image, add or load 500 mixed
shapes, force a redraw, and record render duration and visual child count.

Evidence fields:

| Field | Value |
|---|---|
| Shape count | 500 |
| Render duration ms | |
| EditorCanvas visual child count | |
| WPF element per shape avoided | yes / no |

## Release evidence layers

| Layer | Required evidence before release |
|---|---|
| Core unit tests | Shape geometry calculations, crop survival, compound auto-expand interaction, DPI conversions, keymap display/storage, and settings normalization. |
| Renderer tests | Live/export parity through one renderer, golden PNGs at 1x and 2x, text metrics, pixelate cache hit/invalidation, and 500 shapes. |
| WPF client snapshots | Editor light/dark, all active states, every preferences tab, disabled states, and focus states. |
| Full-HWND screenshots | Editor and preferences on Windows 10 and Windows 11, including outer frame and shadows for the titlebar gate. |
| Process-driven UI tests | Launch test mode, locate controls by automation ID, click tools, draw with real mouse input, type text, undo, change preferences, close/reopen, and verify JSON. |
| Capture fixtures | Deterministic colored-window and scroll fixtures verifying bounds, DPI, self-exclusion, shadow/background variants, and stitched rows. |
| Manual UX matrix | Tray menu, hotkeys, clipboard paste, drag-out, system theme changes, high contrast, mixed monitors, and package modes. |

Fast source guards are release-hardening checks, not a substitute for runtime UI
automation. A release can use them to fail early, but cannot call a source-string
assertion completed UI automation evidence.

## Release blockers

Any of these blocks release until fixed or explicitly accepted with evidence:

| Blocker | Gate |
|---|---|
| Second/native titlebar strip, duplicate close control, unresizable edge, or taskbar overlap. | Full-HWND titlebar evidence. |
| Toolbar groups clip, reorder, or lose active state at minimum width. | WPF snapshots and manual UX matrix. |
| Live canvas and export differ materially in shape geometry or text placement. | Renderer parity and golden PNG evidence. |
| Settings require Save, show raw enum/hex values, or fail to persist after reopen. | Process-driven UI test plus manual preferences pass. |
| Overlay/HUD pixels appear in captured output or remain topmost after cancellation/failure. | Capture fixtures and guarded live smoke. |
| Mixed-DPI selection produces the wrong physical pixel rectangle. | DPI conversion tests and mixed-monitor manual evidence. |
| Any measured budget regresses materially from baseline without accepted rationale. | Performance JSON, diagnostics, and release notes. |
