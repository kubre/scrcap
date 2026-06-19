# Windows behavior differences

The Windows port follows the macOS product contract where practical, with these
intentional implementation differences:

- Global shortcuts use Windows display names (`Alt`, `Ctrl`, `Shift`, `Win`) but
  keep portable storage names (`opt`, `ctrl`, `shift`, `cmd`) so settings remain
  compatible with the shared core.
- Capture uses a Windows platform service boundary. Windows.Graphics.Capture is
  the primary monitor/window path, with DC/BitBlt kept as a fallback when WGC is
  unavailable or fails.
- Tray icon contrast follows the Windows taskbar theme instead of the app theme.
- Framework-dependent and self-contained packages are produced as separate
  artifacts because Windows users may or may not already have the .NET Desktop
  Runtime installed.

## Automated coverage

| Difference | Coverage |
|---|---|
| Windows shortcut display names with portable storage names | `Scrcap.Core.Tests.KeymapEngineTests.ChordParserAcceptsWindowsAndPortableModifierNames` |
| WGC primary capture with DC/BitBlt fallback | `Scrcap.Capture.Tests` validates capture backend metadata and fallback contracts where a deterministic desktop fixture is not required. |
| Tray icon follows taskbar theme, not app theme | `Scrcap.Windows.Platform.Tests.TrayIconServiceTests.IconKeyFollowsTaskbarThemeChanges` |
| Separate framework-dependent and self-contained artifacts | `tools/Publish-Windows.ps1` labels output folders by mode and `tools/Test-ReleaseHardening.ps1` verifies release packaging docs and license gates. |

Desktop-only behaviors such as actual WGC pixels, taskbar contrast on Windows
10/11, and mixed-DPI overlay placement still require the manual QA templates in
`manual-visual-checklist.md` and `performance-and-release-qa.md`.
