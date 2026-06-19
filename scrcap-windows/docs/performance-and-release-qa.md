# Windows performance and release QA

Use this page as the release evidence template for Windows packaging and
performance. Store completed JSON output under `scrcap-windows/artifacts/` and
attach screenshots or screen recordings to the release checklist.

## Budgets

| Area | Budget | Evidence |
|---|---:|---|
| Hotkey to overlay visible latency | warm path under 60 ms | Manual measurement from a warm resident app, using screen recording or ETW trace. |
| Idle CPU after startup plus 10 seconds | under 0.2 percent average | `Measure-WindowsPerformance.ps1` automated idle sample. |
| Idle private memory for framework-dependent build | under 90 MB | `Measure-WindowsPerformance.ps1` automated idle sample. |
| Hidden overlay timers | 0 active animation/timer sources | `Test-ReleaseHardening.ps1` static guard plus optional runtime verification. |
| Selecting overlay timers | exactly 1 marching-ants source while selecting | `Test-ReleaseHardening.ps1` static guard plus optional runtime verification. |
| 500-shape editor rendering | no WPF element per shape | `Test-ReleaseHardening.ps1` static guard; optional render timing in this template. |

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
  -ManualHotkeyToOverlayMs 42 `
  -ManualHiddenOverlayTimerCount 0 `
  -ManualSelectingOverlayTimerCount 1 `
  -ManualEditor500ShapeRenderMs 95 `
  -ManualEditor500ShapeElementCount 1
```

## Hotkey to overlay visible latency

Measure with the app already resident in the tray and hotkeys registered.
Trigger Capture Region with the default or configured hotkey, then record the
time from key-up to the first visible overlay frame.

Evidence fields:

| Field | Value |
|---|---|
| Windows version | |
| CPU/GPU | |
| Display count and DPI | |
| App package mode | framework-dependent / self-contained |
| Warm-up steps | |
| Measurement method | screen recording / ETW / other |
| Trial 1 ms | |
| Trial 2 ms | |
| Trial 3 ms | |
| Average ms | |
| Pass under 60 ms | yes / no |

## Idle CPU and memory

Run the idle measurement with no editor, preferences, overlay, or capture HUD
open. The script starts the tray-resident app with an isolated settings
directory, waits for startup, samples CPU for 10 seconds, records private memory,
and then terminates the process.

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
