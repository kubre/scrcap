param()

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$repoRoot = Split-Path -Parent $root

function Assert-File {
  param([string]$Path)

  if (-not (Test-Path $Path)) {
    throw "Required release-hardening file is missing: $Path"
  }
}

function Assert-Contains {
  param(
    [string]$Path,
    [string]$Pattern,
    [string]$Message
  )

  $text = Get-Content -Raw $Path
  if ($text -notmatch $Pattern) {
    throw $Message
  }
}

function Assert-NotContains {
  param(
    [string]$Path,
    [string]$Pattern,
    [string]$Message
  )

  $text = Get-Content -Raw $Path
  if ($text -match $Pattern) {
    throw $Message
  }
}

$publishScript = Join-Path $PSScriptRoot "Publish-Windows.ps1"
$performanceScript = Join-Path $PSScriptRoot "Measure-WindowsPerformance.ps1"
$licensePath = Join-Path $root "LICENSE.txt"
$windowsReadme = Join-Path $root "README.md"
$differencesDoc = Join-Path $root "docs/windows-differences.md"
$performanceDoc = Join-Path $root "docs/performance-and-release-qa.md"
$manualChecklistDoc = Join-Path $root "docs/manual-visual-checklist.md"
$diagnosticsHelper = Join-Path $root "src/Scrcap.Core/Diagnostics/ScrcapDiagnostics.cs"
$editorCanvas = Join-Path $root "src/Scrcap.Windows.UI/Editor/EditorCanvas.cs"
$overlayXaml = Join-Path $root "src/Scrcap.Windows.UI/Overlay/OverlayWindow.xaml"
$overlayCode = Join-Path $root "src/Scrcap.Windows.UI/Overlay/OverlayWindow.xaml.cs"

Assert-File $publishScript
Assert-File $performanceScript
Assert-File $licensePath
Assert-File $windowsReadme
Assert-File $differencesDoc
Assert-File $performanceDoc
Assert-File $manualChecklistDoc
Assert-File $diagnosticsHelper
Assert-File $editorCanvas
Assert-File $overlayXaml
Assert-File $overlayCode

if ((Get-Item $licensePath).Length -le 0) {
  throw "Windows license notice is empty: $licensePath"
}

Assert-Contains $publishScript "LICENSE\.txt" "Publish script must copy and gate LICENSE.txt."
Assert-Contains $windowsReadme "performance-and-release-qa\.md" "Windows README must link performance and release QA docs."
Assert-Contains $windowsReadme "manual-visual-checklist\.md" "Windows README must link manual visual checklist docs."
Assert-Contains $differencesDoc "Automated coverage" "Windows differences doc must list automated coverage for each documented difference."
Assert-Contains $performanceDoc "Hotkey to overlay visible latency" "Performance QA doc must include hotkey-to-overlay latency evidence."
Assert-Contains $performanceDoc "selection_committed_to_capture_result" "Performance QA doc must include selection-to-capture diagnostic evidence."
Assert-Contains $performanceDoc "capture_result_to_editor_first_interactive_render" "Performance QA doc must include capture-to-editor diagnostic evidence."
Assert-Contains $performanceDoc "warm_pixelate_redraw" "Performance QA doc must include warm pixelate diagnostic evidence."
Assert-Contains $performanceDoc "flatten_to_png" "Performance QA doc must include flatten-to-PNG diagnostic evidence."
Assert-Contains $performanceDoc "scrolling_retained_bytes" "Performance QA doc must include scrolling retained-byte evidence."
Assert-Contains $performanceDoc "Release blockers" "Performance QA doc must list release blockers."
Assert-Contains $performanceDoc "500-shape editor rendering" "Performance QA doc must include the 500-shape editor evidence path."
Assert-Contains $manualChecklistDoc "Windows 10 22H2 - light taskbar" "Manual checklist must cover Windows 10 light taskbar."
Assert-Contains $manualChecklistDoc "Windows 10 22H2 - dark taskbar" "Manual checklist must cover Windows 10 dark taskbar."
Assert-Contains $manualChecklistDoc "Windows 11 stable - light taskbar" "Manual checklist must cover Windows 11 light taskbar."
Assert-Contains $manualChecklistDoc "Windows 11 stable - dark taskbar" "Manual checklist must cover Windows 11 dark taskbar."
Assert-Contains $manualChecklistDoc "100, 125, 150, and 200 percent" "Manual checklist must cover required DPI matrix."
Assert-Contains $manualChecklistDoc "negative virtual-screen origin" "Manual checklist must cover negative virtual-screen origin."
Assert-Contains $manualChecklistDoc "Framework-dependent and self-contained" "Manual checklist must cover package modes."

Assert-Contains $diagnosticsHelper "SCRCAP_DIAGNOSTICS" "Diagnostics must remain opt-in through SCRCAP_DIAGNOSTICS."
Assert-NotContains $diagnosticsHelper "Debugger\.IsAttached" "Diagnostics must not turn on implicitly under a debugger."

Assert-Contains $editorCanvas "protected override void OnRender\(DrawingContext" "EditorCanvas must render committed shapes through OnRender."
Assert-Contains $editorCanvas "foreach \(var shape in document\.Shapes\)" "EditorCanvas must render shape collections without creating per-shape controls."
Assert-NotContains $editorCanvas "Children\.Add|new\s+(Line|Rectangle|Ellipse|TextBlock|Path)\b" "EditorCanvas appears to create WPF elements for shapes; use DrawingContext rendering instead."

Assert-NotContains $overlayXaml 'Storyboard RepeatBehavior="Forever"' "Overlay selection must not use a perpetual XAML Storyboard."
Assert-Contains $overlayCode "DispatcherTimer" "Overlay selection must use a source-level timer that tests can inspect."
Assert-Contains $overlayCode "selectionAntTimer\.Start\(\)" "Overlay selection timer must start only for active selection."
Assert-Contains $overlayCode "selectionAntTimer\.Stop\(\)" "Overlay selection timer must stop when selection ends or closes."

Write-Host "Release hardening checks completed."
