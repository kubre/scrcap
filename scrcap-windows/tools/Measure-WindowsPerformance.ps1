param(
  [string]$Configuration = "Release",
  [int]$IdleSeconds = 10,
  [string]$OutputPath,
  [switch]$SkipBuild,
  [nullable[int]]$ManualHotkeyToOverlayMs,
  [nullable[int]]$ManualHiddenOverlayTimerCount,
  [nullable[int]]$ManualSelectingOverlayTimerCount,
  [nullable[int]]$ManualEditor500ShapeRenderMs,
  [nullable[int]]$ManualEditor500ShapeElementCount
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

  $result = [ordered]@{
    measuredAt = [DateTimeOffset]::Now.ToString("o")
    configuration = $Configuration
    idleSeconds = $IdleSeconds
    processorCount = [Environment]::ProcessorCount
    appDll = $appDll
    budgets = [ordered]@{
      warmHotkeyToOverlayMs = 60
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
      hotkeyToOverlayMs = $ManualHotkeyToOverlayMs
      hotkeyToOverlayPass = if ($null -eq $ManualHotkeyToOverlayMs) { $null } else { $ManualHotkeyToOverlayMs -lt 60 }
      hiddenOverlayTimerCount = $ManualHiddenOverlayTimerCount
      hiddenOverlayTimerPass = if ($null -eq $ManualHiddenOverlayTimerCount) { $null } else { $ManualHiddenOverlayTimerCount -eq 0 }
      selectingOverlayTimerCount = $ManualSelectingOverlayTimerCount
      selectingOverlayTimerPass = if ($null -eq $ManualSelectingOverlayTimerCount) { $null } else { $ManualSelectingOverlayTimerCount -eq 1 }
      editor500ShapeRenderMs = $ManualEditor500ShapeRenderMs
      editor500ShapeElementCount = $ManualEditor500ShapeElementCount
      editor500ShapeElementPass = if ($null -eq $ManualEditor500ShapeElementCount) { $null } else { $ManualEditor500ShapeElementCount -le 1 }
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
