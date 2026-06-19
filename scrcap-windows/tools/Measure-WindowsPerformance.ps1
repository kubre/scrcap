param(
  [string]$Configuration = "Release",
  [int]$IdleSeconds = 10,
  [string]$OutputPath,
  [switch]$SkipBuild,
  [int[]]$ManualHotkeyToOverlayMs,
  [nullable[int]]$ManualHiddenOverlayTimerCount,
  [nullable[int]]$ManualSelectingOverlayTimerCount,
  [int[]]$ManualEditor500ShapeRenderMs,
  [nullable[int]]$ManualEditor500ShapeElementCount,
  [int[]]$ManualSelectionToCapturedPixelsMs,
  [int[]]$ManualFourKCaptureMs,
  [int[]]$ManualCaptureToEditorInteractiveMs,
  [int[]]$ManualWarmPixelateRedrawMs,
  [int[]]$ManualFlattenToPngMs,
  [string]$ManualCpuGpu,
  [string]$ManualWindowsBuild,
  [string]$ManualDpi,
  [string]$ManualBackend,
  [string]$ManualPackageMode
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "src/Scrcap.Windows.UI/Scrcap.Windows.UI.csproj"
$framework = "net8.0-windows10.0.19041.0"
$appDll = Join-Path $root "src/Scrcap.Windows.UI/bin/$Configuration/$framework/Scrcap.Windows.UI.dll"

function Quote-Argument {
  param([string]$Value)

  '"' + ($Value -replace '\\', '\\' -replace '"', '\"') + '"'
}

if (-not $OutputPath) {
  $OutputPath = Join-Path $root "artifacts/performance/windows-performance.json"
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
  throw "dotnet is required to measure scrcap for Windows."
}

if (-not $SkipBuild) {
  dotnet build $project -c $Configuration
}

if (-not (Test-Path $appDll)) {
  throw "Could not find app DLL at $appDll. Build first or pass -Configuration matching an existing build."
}

function Get-TrialAverage {
  param([int[]]$Values)

  if ($null -eq $Values -or $Values.Count -eq 0) {
    return $null
  }

  return [Math]::Round(($Values | Measure-Object -Average).Average, 2)
}

function Test-TrialBudget {
  param(
    [int[]]$Values,
    [double]$Budget,
    [switch]$Inclusive
  )

  if ($null -eq $Values -or $Values.Count -lt 3) {
    return $null
  }

  $average = Get-TrialAverage $Values
  if ($Inclusive) {
    return $average -le $Budget
  }

  return $average -lt $Budget
}

$settingsDir = Join-Path ([System.IO.Path]::GetTempPath()) ("scrcap-perf-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force -Path $settingsDir | Out-Null

$startInfo = [System.Diagnostics.ProcessStartInfo]::new("dotnet")
$startInfo.UseShellExecute = $false
$startInfo.RedirectStandardOutput = $true
$startInfo.RedirectStandardError = $true
$startInfo.Arguments = @(
  (Quote-Argument $appDll),
  "--test-settings-dir",
  (Quote-Argument $settingsDir)
) -join " "

$process = [System.Diagnostics.Process]::Start($startInfo)
if (-not $process) {
  throw "Could not start scrcap process."
}

try {
  Start-Sleep -Seconds 2
  if ($process.HasExited) {
    $stdout = $process.StandardOutput.ReadToEnd()
    $stderr = $process.StandardError.ReadToEnd()
    throw "scrcap exited before idle sampling. ExitCode=$($process.ExitCode) stdout=$stdout stderr=$stderr"
  }

  $sampleStartTime = [DateTimeOffset]::Now
  $cpuStart = $process.TotalProcessorTime
  Start-Sleep -Seconds $IdleSeconds
  $process.Refresh()
  $sampleEndTime = [DateTimeOffset]::Now
  $cpuEnd = $process.TotalProcessorTime
  $elapsedMs = [Math]::Max(1, ($sampleEndTime - $sampleStartTime).TotalMilliseconds)
  $cpuMs = ($cpuEnd - $cpuStart).TotalMilliseconds
  $cpuPercent = [Math]::Round(($cpuMs / $elapsedMs / [Environment]::ProcessorCount) * 100, 4)
  $privateMemoryMb = [Math]::Round($process.PrivateMemorySize64 / 1MB, 2)
  $hotkeyAverage = Get-TrialAverage $ManualHotkeyToOverlayMs
  $selectionAverage = Get-TrialAverage $ManualSelectionToCapturedPixelsMs
  $fourKAverage = Get-TrialAverage $ManualFourKCaptureMs
  $editorAverage = Get-TrialAverage $ManualCaptureToEditorInteractiveMs
  $pixelateAverage = Get-TrialAverage $ManualWarmPixelateRedrawMs
  $flattenAverage = Get-TrialAverage $ManualFlattenToPngMs
  $editor500Average = Get-TrialAverage $ManualEditor500ShapeRenderMs

  $result = [ordered]@{
    measuredAt = [DateTimeOffset]::Now.ToString("o")
    configuration = $Configuration
    idleSeconds = $IdleSeconds
    processorCount = [Environment]::ProcessorCount
    appDll = $appDll
    budgets = [ordered]@{
      warmHotkeyToOverlayMs = 60
      smallRegionSelectionToCapturedPixelsMs = 150
      fourKCaptureMs = 300
      capturedPixelsToEditorInteractiveMs = 150
      warmPixelateRedrawMs = 33
      idleCpuPercent = 0.2
      frameworkDependentPrivateMemoryMb = 90
      hiddenOverlayTimerCount = 0
      selectingOverlayTimerCount = 1
      editor500ShapeElementCount = 1
    }
    automated = [ordered]@{
      idleCpuPercent = $cpuPercent
      idleCpuPass = $cpuPercent -lt 0.2
      privateMemoryMb = $privateMemoryMb
      privateMemoryPass = $privateMemoryMb -lt 90
    }
    manual = [ordered]@{
      hotkeyToOverlayTrialsMs = $ManualHotkeyToOverlayMs
      hotkeyToOverlayAverageMs = $hotkeyAverage
      hotkeyToOverlayPass = Test-TrialBudget $ManualHotkeyToOverlayMs 60
      hiddenOverlayTimerCount = $ManualHiddenOverlayTimerCount
      hiddenOverlayTimerPass = if ($null -eq $ManualHiddenOverlayTimerCount) { $null } else { $ManualHiddenOverlayTimerCount -eq 0 }
      selectingOverlayTimerCount = $ManualSelectingOverlayTimerCount
      selectingOverlayTimerPass = if ($null -eq $ManualSelectingOverlayTimerCount) { $null } else { $ManualSelectingOverlayTimerCount -eq 1 }
      editor500ShapeRenderTrialsMs = $ManualEditor500ShapeRenderMs
      editor500ShapeRenderAverageMs = $editor500Average
      editor500ShapeElementCount = $ManualEditor500ShapeElementCount
      editor500ShapeElementPass = if ($null -eq $ManualEditor500ShapeElementCount) { $null } else { $ManualEditor500ShapeElementCount -le 1 }
      selectionToCapturedPixelsTrialsMs = $ManualSelectionToCapturedPixelsMs
      selectionToCapturedPixelsAverageMs = $selectionAverage
      selectionToCapturedPixelsPass = Test-TrialBudget $ManualSelectionToCapturedPixelsMs 150
      fourKCaptureTrialsMs = $ManualFourKCaptureMs
      fourKCaptureAverageMs = $fourKAverage
      fourKCapturePass = Test-TrialBudget $ManualFourKCaptureMs 300
      captureToEditorInteractiveTrialsMs = $ManualCaptureToEditorInteractiveMs
      captureToEditorInteractiveAverageMs = $editorAverage
      captureToEditorInteractivePass = Test-TrialBudget $ManualCaptureToEditorInteractiveMs 150
      warmPixelateRedrawTrialsMs = $ManualWarmPixelateRedrawMs
      warmPixelateRedrawAverageMs = $pixelateAverage
      warmPixelateRedrawPass = Test-TrialBudget $ManualWarmPixelateRedrawMs 33 -Inclusive
      flattenToPngTrialsMs = $ManualFlattenToPngMs
      flattenToPngAverageMs = $flattenAverage
      requiredTimingTrials = 3
      cpuGpu = $ManualCpuGpu
      windowsBuild = $ManualWindowsBuild
      dpi = $ManualDpi
      backend = $ManualBackend
      packageMode = $ManualPackageMode
    }
  }

  New-Item -ItemType Directory -Force -Path (Split-Path -Parent $OutputPath) | Out-Null
  $result | ConvertTo-Json -Depth 6 | Set-Content -Encoding UTF8 $OutputPath
  Write-Host "Wrote Windows performance evidence to $OutputPath"
}
finally {
  if (-not $process.HasExited) {
    Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
    try {
      Wait-Process -Id $process.Id -Timeout 5 -ErrorAction SilentlyContinue
    }
    catch {
      # Best-effort process cleanup for local measurement only.
    }
  }

  Remove-Item $settingsDir -Recurse -Force -ErrorAction SilentlyContinue
}
