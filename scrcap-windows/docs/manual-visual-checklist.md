# Windows manual visual checklist

Complete this checklist before a Windows release. Save screenshots or short
screen recordings for each taskbar mode and link them from the release notes or
QA issue.

## Windows 10 22H2 - light taskbar

| Check | Result | Evidence |
|---|---|---|
| Tray icon is visible against the light taskbar. | | |
| Tray icon keeps crisp edges at 100, 150, and 200 percent DPI. | | |
| Tray menu opens from left and right click. | | |
| Switching Windows color mode while scrcap is running updates the tray icon. | | |
| Capture Region overlay is readable over light desktop content. | | |

## Windows 10 22H2 - dark taskbar

| Check | Result | Evidence |
|---|---|---|
| Tray icon is visible against the dark taskbar. | | |
| Tray icon keeps crisp edges at 100, 150, and 200 percent DPI. | | |
| Tray menu opens from left and right click. | | |
| Switching Windows color mode while scrcap is running updates the tray icon. | | |
| Capture Region overlay is readable over dark desktop content. | | |

## Windows 11 stable - light taskbar

| Check | Result | Evidence |
|---|---|---|
| Tray icon is visible against the light taskbar and overflow flyout. | | |
| Tray icon keeps crisp edges at 100, 150, and 200 percent DPI. | | |
| Tray menu opens from left and right click. | | |
| Switching Windows color mode while scrcap is running updates the tray icon. | | |
| Capture Region overlay is readable over light desktop content. | | |

## Windows 11 stable - dark taskbar

| Check | Result | Evidence |
|---|---|---|
| Tray icon is visible against the dark taskbar and overflow flyout. | | |
| Tray icon keeps crisp edges at 100, 150, and 200 percent DPI. | | |
| Tray menu opens from left and right click. | | |
| Switching Windows color mode while scrcap is running updates the tray icon. | | |
| Capture Region overlay is readable over dark desktop content. | | |

## Evidence template

| Field | Value |
|---|---|
| Tester | |
| Date | |
| Windows edition and build | |
| Hardware or VM | |
| GPU driver | |
| Display layout | |
| DPI values | |
| App build or artifact path | |
| Package mode | framework-dependent / self-contained |
| Screenshots folder | |
| Notes and defects | |

## Current RogStrix evidence - 2026-06-19

| Field | Value |
|---|---|
| Tester | Codex on RogStrix worktree |
| Date | 2026-06-19 |
| Windows edition and build | Microsoft Windows 11 Home Single Language 10.0.26200 build 26200 |
| Hardware or VM | ASUS ROG Strix G513IC |
| GPU driver | NVIDIA GeForce RTX 3050 Laptop GPU 32.0.15.7628; AMD Radeon(TM) Graphics 30.0.13002.19003 |
| Display layout | One primary display, 1920 by 1080, working area 1920 by 1032 |
| DPI values | 100 percent / 96 DPI only |
| App build or artifact path | Release loose build from `src/Scrcap.Windows.UI/bin/Release/net8.0-windows10.0.19041.0` |
| Package mode | framework-dependent publish verified by release guardrails; self-contained publish verified with `tools/Publish-Windows.ps1 -Mode self-contained -Configuration Release` |
| Screenshots folder | `docs/windows-parity` |
| Notes and defects | Current evidence covers editor and preferences full-HWND light captures, window chrome smoke, idle CPU/private memory, fixture capture tests, and process-driven editor Save. Windows 10, multi-monitor, mixed DPI, negative origin, high contrast, taskbar light/dark manual checks, Paint/Chromium paste, true drag-out, and physical overlay interaction checks remain open. Computer Use UI automation was not available in this thread because the Node REPL execution tool was not exposed after discovery. |

## Current row disposition

| Row group | Status | Reason |
|---|---|---|
| Windows 11 at 100 percent DPI, app light theme | Verified | Current full-HWND screenshots, chrome smoke, and process/editor tests were run on Windows 11 build 26200 with one 100 percent / 96 DPI display. |
| Windows 10, 125/150/200 percent DPI, mixed DPI, negative origin | Open | This RogStrix session exposed one Windows 11 display at 100 percent DPI; no second OS, second monitor, or safe DPI switch was available in-thread. |
| Taskbar light/dark visual tray checks and live tray theme switch | Open | Tray icon asset selection and update behavior are covered by service tests, but physical taskbar visibility and overflow-flyout contrast still require screenshots or recording. |
| Paint, Word, Teams, Slack, Chromium, Outlook paste checks | Open | The process test proves a PNG clipboard payload and bitmap paste compatibility data are published, but cross-app paste requires Windows UI automation or manual app access. Computer Use Node execution was unavailable in this thread. |
| Drag-out into another app | Open | The automated test proves the drag-out temp PNG dimensions/DPI metadata and stale-file cleanup. Dropping into a target app remains manual because no Windows UI automation target was available in this thread. |
| Physical tray menu and global hotkey capture modes | Open | These require resident app interaction through the tray/hotkeys and physical overlay input. The current automated evidence covers command routing, fixtures, and editor/capture services, not full manual tray/hotkey capture runs. |
| Window capture variants and protected/elevated/minimized/hidden cases | Open | Fixture and service tests cover deterministic HWND capture, own-process exclusion, hidden/minimized filtering behavior, and protected-content handling code paths; real app matrix windows remain manual. |
| Scrolling browser/chat/sticky/non-scroll/lazy-loaded cases | Partially verified | Core stitcher tests cover sticky, no-scroll, lazy-load, bounce/bottom, and max-height behavior. The deterministic scroll fixture exposes HWND/stdout. Real browser/chat scrolling captures remain manual. |
| Clipboard locked, missing save folder, disk write denied | Open | Recoverable error paths exist, but this thread did not safely lock the clipboard or alter filesystem permissions to force those failures. |

## Required release matrix

Do not mark this matrix complete from code inspection alone. Each checked row
needs a screenshot, short recording, JSON artifact, or linked test result.

| Area | Required coverage |
|---|---|
| Operating systems | Windows 10 22H2 and stable Windows 11. |
| DPI | 100, 125, 150, and 200 percent. Include one mixed layout: 100+150 or 100+200. |
| Virtual screen | At least one negative virtual-screen origin layout. |
| App theme | Light, dark, and system-following. |
| Shell theme | Light and dark taskbar. |
| Accessibility | High contrast. |
| Window states | Normal, minimum supported size, maximized, snapped, and moved between monitors. |
| Capture modes | Region, delayed, window, fullscreen, scrolling, and repeat-last. |
| Outputs | Clipboard, Paint, Chromium paste, save, Save As, drag-out, 1x export, and 2x export. |
| Package modes | Framework-dependent and self-contained artifacts. |

## Runtime UX checks

| Check | Result | Evidence |
|---|---|---|
| Tray menu opens, commands work, and Quit exits without a resident process. | | |
| Global hotkeys invoke the expected capture mode after preferences reopen. | | |
| Clipboard paste lands in Paint and Chromium with expected pixels. | | |
| Drag-out writes a PNG and leaves no stale temp file older than cleanup policy. | | |
| System theme changes update open app surfaces where expected. | | |
| High contrast keeps editor, preferences, overlay, and tray menu legible. | | |
| Mixed-monitor selection maps to the correct physical pixel rectangle. | | |
| Overlay and scrolling HUD never appear in captured output. | | |
| Overlay/HUD closes after cancellation or capture failure and is no longer topmost. | | |
| Toolbar groups retain order, active state, and labels/icons at minimum width. | | |
