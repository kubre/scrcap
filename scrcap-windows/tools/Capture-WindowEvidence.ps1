param(
  [string]$AppPath = (Join-Path $PSScriptRoot "..\src\Scrcap.Windows.UI\bin\Release\net8.0-windows10.0.19041.0\Scrcap.Windows.UI.dll"),
  [string[]]$AppArguments = @(),
  [Parameter(Mandatory = $true)]
  [string]$OutputPath,
  [string]$MetadataPath,
  [string]$WindowTitlePattern = "scrcap",
  [int]$TimeoutSeconds = 15,
  [int]$ShadowPadding = 24,
  [string]$PackageMode = "Release loose build",
  [string]$Theme = "unspecified",
  [string]$Scenario = "unspecified"
)

$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

if (-not ("EvidenceNative" -as [type])) {
Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;

public static class EvidenceNative
{
    public delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    public struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NativePoint
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int count);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowTextLength(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);

    [DllImport("dwmapi.dll")]
    public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out NativeRect rect, int size);

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);

    [DllImport("shcore.dll")]
    public static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);
}
"@
}

function Get-GitValue([string[]]$Arguments) {
  try {
    return (& git @Arguments 2>$null | Select-Object -First 1)
  } catch {
    return $null
  }
}

function Get-GitValues([string[]]$Arguments) {
  try {
    return @(& git @Arguments 2>$null)
  } catch {
    return @()
  }
}

function Convert-Rect($Rect) {
  [PSCustomObject]@{
    left = $Rect.Left
    top = $Rect.Top
    right = $Rect.Right
    bottom = $Rect.Bottom
    width = [Math]::Max(0, $Rect.Right - $Rect.Left)
    height = [Math]::Max(0, $Rect.Bottom - $Rect.Top)
  }
}

function Get-WindowTitle([IntPtr]$Hwnd) {
  $length = [EvidenceNative]::GetWindowTextLength($Hwnd)
  if ($length -le 0) {
    return ""
  }

  $builder = [Text.StringBuilder]::new($length + 1)
  [void][EvidenceNative]::GetWindowText($Hwnd, $builder, $builder.Capacity)
  return $builder.ToString()
}

function Find-WindowForProcess([int]$TargetProcessId, [string]$TitlePattern) {
  $windowMatches = New-Object System.Collections.ArrayList
  $callback = [EvidenceNative+EnumWindowsProc]{
    param([IntPtr]$hwnd, [IntPtr]$lParam)
    if (-not [EvidenceNative]::IsWindowVisible($hwnd)) {
      return $true
    }

    $windowProcessId = 0
    [void][EvidenceNative]::GetWindowThreadProcessId($hwnd, [ref]$windowProcessId)
    if ($windowProcessId -ne $TargetProcessId) {
      return $true
    }

    $title = Get-WindowTitle $hwnd
    if ($title -match $TitlePattern) {
      [void]$windowMatches.Add([PSCustomObject]@{ Hwnd = $hwnd; Title = $title })
    }

    return $true
  }

  [void][EvidenceNative]::EnumWindows($callback, [IntPtr]::Zero)
  return $windowMatches | Select-Object -First 1
}

function Get-MonitorDpi([int]$Left, [int]$Top) {
  $point = New-Object EvidenceNative+NativePoint
  $point.X = $Left
  $point.Y = $Top
  $monitor = [EvidenceNative]::MonitorFromPoint($point, 2)
  if ($monitor -eq [IntPtr]::Zero) {
    return $null
  }

  $dpiX = 0
  $dpiY = 0
  if ([EvidenceNative]::GetDpiForMonitor($monitor, 0, [ref]$dpiX, [ref]$dpiY) -ne 0) {
    return $null
  }

  [PSCustomObject]@{
    dpiX = $dpiX
    dpiY = $dpiY
    scalePercent = [Math]::Round(($dpiX / 96.0) * 100)
  }
}

function Get-DisplayLayout {
  foreach ($screen in [System.Windows.Forms.Screen]::AllScreens) {
    $dpi = Get-MonitorDpi $screen.Bounds.Left $screen.Bounds.Top
    [PSCustomObject]@{
      deviceName = $screen.DeviceName
      primary = $screen.Primary
      bounds = @{
        left = $screen.Bounds.Left
        top = $screen.Bounds.Top
        width = $screen.Bounds.Width
        height = $screen.Bounds.Height
      }
      workingArea = @{
        left = $screen.WorkingArea.Left
        top = $screen.WorkingArea.Top
        width = $screen.WorkingArea.Width
        height = $screen.WorkingArea.Height
      }
      dpi = $dpi
    }
  }
}

function Join-ProcessArguments([string[]]$Arguments) {
  ($Arguments | ForEach-Object {
    if ($_ -match '[\s"]') {
      '"' + ($_ -replace '"', '\"') + '"'
    } else {
      $_
    }
  }) -join ' '
}

function Start-TargetApp {
  $resolvedAppPath = (Resolve-Path $AppPath).Path
  if ([IO.Path]::GetExtension($resolvedAppPath).Equals(".dll", [StringComparison]::OrdinalIgnoreCase)) {
    return Start-Process -FilePath "dotnet" -ArgumentList (Join-ProcessArguments (@($resolvedAppPath) + $AppArguments)) -PassThru
  }

  return Start-Process -FilePath $resolvedAppPath -ArgumentList (Join-ProcessArguments $AppArguments) -PassThru
}

$process = Start-TargetApp
$deadline = [DateTimeOffset]::Now.AddSeconds($TimeoutSeconds)
$window = $null
try {
  do {
    Start-Sleep -Milliseconds 250
    if ($process.HasExited) {
      throw "Target process exited before an HWND was visible. Exit code: $($process.ExitCode)"
    }

    $window = Find-WindowForProcess $process.Id $WindowTitlePattern
  } while ($null -eq $window -and [DateTimeOffset]::Now -lt $deadline)

  if ($null -eq $window) {
    throw "No visible HWND matching '$WindowTitlePattern' appeared for process $($process.Id) within $TimeoutSeconds seconds."
  }

  $windowRect = New-Object EvidenceNative+NativeRect
  if (-not [EvidenceNative]::GetWindowRect($window.Hwnd, [ref]$windowRect)) {
    throw "GetWindowRect failed for HWND $($window.Hwnd)."
  }

  $dwmRect = New-Object EvidenceNative+NativeRect
  $hasDwmRect = [EvidenceNative]::DwmGetWindowAttribute($window.Hwnd, 9, [ref]$dwmRect, [Runtime.InteropServices.Marshal]::SizeOf([type][EvidenceNative+NativeRect])) -eq 0

  $left = $windowRect.Left
  $top = $windowRect.Top
  $right = $windowRect.Right
  $bottom = $windowRect.Bottom
  if ($hasDwmRect) {
    $left = [Math]::Min($left, $dwmRect.Left)
    $top = [Math]::Min($top, $dwmRect.Top)
    $right = [Math]::Max($right, $dwmRect.Right)
    $bottom = [Math]::Max($bottom, $dwmRect.Bottom)
  }

  $left -= $ShadowPadding
  $top -= $ShadowPadding
  $right += $ShadowPadding
  $bottom += $ShadowPadding
  $width = [Math]::Max(1, $right - $left)
  $height = [Math]::Max(1, $bottom - $top)

  $outputFullPath = [IO.Path]::GetFullPath($OutputPath)
  New-Item -ItemType Directory -Force -Path ([IO.Path]::GetDirectoryName($outputFullPath)) | Out-Null
  $bitmap = [Drawing.Bitmap]::new($width, $height, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
  try {
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    try {
      $graphics.CopyFromScreen($left, $top, 0, 0, [Drawing.Size]::new($width, $height), [Drawing.CopyPixelOperation]::SourceCopy)
    } finally {
      $graphics.Dispose()
    }

    $bitmap.Save($outputFullPath, [Drawing.Imaging.ImageFormat]::Png)
  } finally {
    $bitmap.Dispose()
  }

  if ([string]::IsNullOrWhiteSpace($MetadataPath)) {
    $MetadataPath = [IO.Path]::ChangeExtension($outputFullPath, ".json")
  }

  $os = Get-CimInstance Win32_OperatingSystem
  $gpu = Get-CimInstance Win32_VideoController | Select-Object Name, DriverVersion, AdapterRAM
  $repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
  $branch = Get-GitValue @("-C", $repoRoot, "branch", "--show-current")
  $headName = Get-GitValue @("-C", $repoRoot, "name-rev", "--name-only", "--refs=refs/heads/*", "--refs=refs/remotes/*", "HEAD")
  $refsPointingAtHead = @(Get-GitValues @("-C", $repoRoot, "branch", "--points-at", "HEAD", "--format", "%(refname:short)") | Where-Object { $_ -and $_ -ne "(no branch)" })
  $metadata = [PSCustomObject]@{
    scenario = $Scenario
    theme = $Theme
    evidenceDateUtc = [DateTimeOffset]::UtcNow.ToString("o")
    commit = Get-GitValue @("-C", $repoRoot, "rev-parse", "HEAD")
    branch = $branch
    headName = $headName
    refsPointingAtHead = $refsPointingAtHead
    branchContext = if ($branch) { $branch } elseif ($headName) { $headName } elseif ($refsPointingAtHead.Count -gt 0) { $refsPointingAtHead[0] } else { "unknown" }
    packageMode = $PackageMode
    appPath = (Resolve-Path $AppPath).Path
    appArguments = $AppArguments
    processId = $process.Id
    hwnd = $window.Hwnd.ToString()
    windowTitle = $window.Title
    captureMethod = "desktop CopyFromScreen using HWND bounds plus shadow padding; not WPF RenderTargetBitmap"
    shadowPadding = $ShadowPadding
    output = $outputFullPath
    image = @{ width = $width; height = $height }
    windowRect = Convert-Rect $windowRect
    dwmExtendedFrameBounds = if ($hasDwmRect) { Convert-Rect $dwmRect } else { $null }
    captureBounds = @{ left = $left; top = $top; width = $width; height = $height }
    os = @{
      caption = $os.Caption
      version = $os.Version
      buildNumber = $os.BuildNumber
      architecture = $os.OSArchitecture
    }
    gpu = $gpu
    displays = @(Get-DisplayLayout)
  }

  $metadata | ConvertTo-Json -Depth 8 | Set-Content -Path $MetadataPath -Encoding UTF8
  Write-Host "Captured $Scenario full-HWND evidence to $outputFullPath"
  Write-Host "Metadata written to $MetadataPath"
} finally {
  if ($process -and -not $process.HasExited) {
    [void]$process.CloseMainWindow()
    if (-not $process.WaitForExit(3000)) {
      $process.Kill()
      $process.WaitForExit()
    }
  }
}
