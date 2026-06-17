# scrcap Windows

This subtree is the Windows port described in the implementation plan. It keeps
portable behavior in `Scrcap.Core` and isolates Windows APIs behind
`Scrcap.Windows.Platform`.

## Projects

- `src/Scrcap.Core` - portable annotation, keymap, settings, filename, and
  scrolling stitch logic.
- `src/Scrcap.Windows.Platform` - hotkey, tray, theme, and capture service
  boundaries.
- `src/Scrcap.Windows.UI` - WPF editor, overlay, and preferences shell.
- `tests/Scrcap.Core.Tests` - cross-platform core tests.
- `tests/Scrcap.Windows.Platform.Tests` - hotkey/tray platform service tests.
- `tests/Scrcap.Rendering.Tests` - WPF rendering baseline test home.
- `tests/Scrcap.UiAutomation.Tests` - FlaUI/UIA scenario test home.
- `tests/Scrcap.Capture.Tests` - capture implementation regression tests.

## Build

Requires .NET 8 SDK. The portable core can be built and tested on macOS or
Windows. The WPF, WinForms tray/hotkey, WindowsDesktop runtime, and capture
projects must be validated on Windows.

```powershell
dotnet restore .\Scrcap.sln
dotnet test .\tests\Scrcap.Core.Tests\Scrcap.Core.Tests.csproj
dotnet test .\tests\Scrcap.Windows.Platform.Tests\Scrcap.Windows.Platform.Tests.csproj
dotnet build .\src\Scrcap.Windows.UI\Scrcap.Windows.UI.csproj
pwsh .\tools\Check-HardcodedUiColors.ps1
```

On non-Windows hosts, `Scrcap.Windows.Platform.Tests` may compile but cannot run
because it targets `Microsoft.WindowsDesktop.App`.

The stricter release guard runs the UI token guard, restore, build, solution
tests, and publish artifact gate:

```powershell
pwsh .\tools\Test-ReleaseGuardrails.ps1
```

Use `-SkipPublish` for a faster preflight while iterating.

## Publish

Framework-dependent Windows x64 single-file package:

```powershell
pwsh .\tools\Publish-Windows.ps1 -Mode framework-dependent
```

Self-contained Windows x64 single-file package:

```powershell
pwsh .\tools\Publish-Windows.ps1 -Mode self-contained
```

The publish script writes to `artifacts/`, cleans the previous output for that
mode, strips debug symbols, and fails if the release output is missing the app
executable or contains PDBs, test baselines, or intermediate build folders.

## Current readiness

- Portable core: annotation stack, document snapshots, crop survival/coordinate
  shifting, keymap parsing/conflict handling, settings migration/normalization,
  filename generation, and stitch row alignment are implemented with tests.
- Windows shell: tray residency, global hotkey service, region overlay,
  preferences, app test hooks, and editor shell exist as first-pass WPF/platform
  implementations.
- Capture: region, selected-window, active-window, monitor-under-cursor, and
  scrolling capture are implemented behind `IWindowsCaptureService`.
  Windows.Graphics.Capture is the primary monitor/window path, with DC/BitBlt
  isolated as a fallback for unsupported or failed capture paths.
- Scrolling capture: the region overlay routes into the scrolling capture loop,
  reports frame progress to a top-right HUD, and supports Stop/Esc cancellation.
- Guardrails: `Check-HardcodedUiColors.ps1` now fails on hard-coded XAML colors,
  font sizes, and fixed visible layout metrics outside `ThemeTokens.xaml`.
- Deferred verification: visual baselines, UI automation against a real desktop,
  capture fixture pixel comparisons, performance budgets, release artifact
  naming/assets/license checks, and Windows 10/11 manual QA must pass on a
  Windows machine before release.
