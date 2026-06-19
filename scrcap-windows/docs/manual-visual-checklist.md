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
