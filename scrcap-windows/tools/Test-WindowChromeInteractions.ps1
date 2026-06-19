param(
  [string]$AppPath = (Join-Path $PSScriptRoot "..\src\Scrcap.Windows.UI\bin\Release\net8.0-windows10.0.19041.0\Scrcap.Windows.UI.dll"),
  [string]$SamplePath = (Join-Path $PSScriptRoot "..\docs\windows-parity\reference-sample.png"),
  [string]$SettingsDirectory = (Join-Path $PSScriptRoot "..\docs\windows-parity\settings-deterministic"),
  [int]$TimeoutSeconds = 15
)

$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Windows.Forms

if (-not ("WindowChromeTestNative" -as [type])) {
Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;

public static class WindowChromeTestNative
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

    [DllImport("user32.dll")]
    public static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hwnd, int command);

    [DllImport("user32.dll")]
    public static extern bool IsZoomed(IntPtr hwnd);

    [DllImport("user32.dll")]
    public static extern bool IsIconic(IntPtr hwnd);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    public static extern bool PostMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);
}
"@
}

function Assert-True([bool]$Condition, [string]$Message) {
  if (-not $Condition) {
    throw $Message
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

function Get-WindowTitle([IntPtr]$Hwnd) {
  $length = [WindowChromeTestNative]::GetWindowTextLength($Hwnd)
  if ($length -le 0) {
    return ""
  }

  $builder = [Text.StringBuilder]::new($length + 1)
  [void][WindowChromeTestNative]::GetWindowText($Hwnd, $builder, $builder.Capacity)
  return $builder.ToString()
}

function Find-WindowForProcess([int]$TargetProcessId) {
  $windowMatches = New-Object System.Collections.ArrayList
  $callback = [WindowChromeTestNative+EnumWindowsProc]{
    param([IntPtr]$hwnd, [IntPtr]$lParam)
    if (-not [WindowChromeTestNative]::IsWindowVisible($hwnd)) {
      return $true
    }

    $windowProcessId = 0
    [void][WindowChromeTestNative]::GetWindowThreadProcessId($hwnd, [ref]$windowProcessId)
    if ($windowProcessId -eq $TargetProcessId -and (Get-WindowTitle $hwnd) -match "scrcap") {
      [void]$windowMatches.Add($hwnd)
    }

    return $true
  }

  [void][WindowChromeTestNative]::EnumWindows($callback, [IntPtr]::Zero)
  return $windowMatches | Select-Object -First 1
}

function Get-Rect([IntPtr]$Hwnd) {
  $rect = New-Object WindowChromeTestNative+NativeRect
  Assert-True ([WindowChromeTestNative]::GetWindowRect($Hwnd, [ref]$rect)) "GetWindowRect failed."
  return $rect
}

function Make-LParam([int]$X, [int]$Y) {
  $unsigned = (($Y -band 0xffff) -shl 16) -bor ($X -band 0xffff)
  return [IntPtr]$unsigned
}

function HitTest([IntPtr]$Hwnd, [int]$X, [int]$Y) {
  return [WindowChromeTestNative]::SendMessage($Hwnd, 0x0084, [IntPtr]::Zero, (Make-LParam $X $Y)).ToInt32()
}

$resolvedAppPath = (Resolve-Path $AppPath).Path
$resolvedSamplePath = (Resolve-Path $SamplePath).Path
New-Item -ItemType Directory -Force -Path $SettingsDirectory | Out-Null
$appArgs = @("--test-mode", "--open-sample-editor", $resolvedSamplePath, "--test-settings-dir", $SettingsDirectory, "--test-app-theme", "light")
$process = if ([IO.Path]::GetExtension($resolvedAppPath).Equals(".dll", [StringComparison]::OrdinalIgnoreCase)) {
  Start-Process -FilePath "dotnet" -ArgumentList (Join-ProcessArguments (@($resolvedAppPath) + $appArgs)) -PassThru
} else {
  Start-Process -FilePath $resolvedAppPath -ArgumentList (Join-ProcessArguments $appArgs) -PassThru
}

try {
  $deadline = [DateTimeOffset]::Now.AddSeconds($TimeoutSeconds)
  $hwnd = $null
  do {
    Start-Sleep -Milliseconds 250
    Assert-True (-not $process.HasExited) "Target process exited before window interaction tests."
    $hwnd = Find-WindowForProcess $process.Id
  } while ($null -eq $hwnd -and [DateTimeOffset]::Now -lt $deadline)
  Assert-True ($null -ne $hwnd) "No visible scrcap HWND appeared."

  $rect = Get-Rect $hwnd
  $captionHit = HitTest $hwnd ($rect.Left + 190) ($rect.Top + 18)
  Assert-True ($captionHit -eq 2) "Expected empty header to hit HTCAPTION (2), got $captionHit."

  $resizeHit = HitTest $hwnd ($rect.Right - 2) ($rect.Top + [Math]::Max(80, [int](($rect.Bottom - $rect.Top) / 2)))
  Assert-True ($resizeHit -eq 11) "Expected right edge to hit HTRIGHT (11), got $resizeHit."

  $maxHit = HitTest $hwnd ($rect.Right - 69) ($rect.Top + 18)
  Assert-True ($maxHit -eq 9) "Expected custom maximize button to hit HTMAXBUTTON (9), got $maxHit."

  [void][WindowChromeTestNative]::ShowWindow($hwnd, 3)
  Start-Sleep -Milliseconds 500
  Assert-True ([WindowChromeTestNative]::IsZoomed($hwnd)) "ShowWindow maximize did not maximize the window."
  $maximizedRect = Get-Rect $hwnd
  $screen = [System.Windows.Forms.Screen]::FromHandle($hwnd)
  Assert-True ($maximizedRect.Bottom -le ($screen.WorkingArea.Bottom + 12)) "Maximized window appears to overlap below taskbar working area."

  [void][WindowChromeTestNative]::ShowWindow($hwnd, 9)
  Start-Sleep -Milliseconds 400
  Assert-True (-not [WindowChromeTestNative]::IsZoomed($hwnd)) "ShowWindow restore did not restore the window."

  [void][WindowChromeTestNative]::ShowWindow($hwnd, 6)
  Start-Sleep -Milliseconds 400
  Assert-True ([WindowChromeTestNative]::IsIconic($hwnd)) "ShowWindow minimize did not minimize the window."
  [void][WindowChromeTestNative]::ShowWindow($hwnd, 9)
  Start-Sleep -Milliseconds 400

  $shell = New-Object -ComObject WScript.Shell
  [void][WindowChromeTestNative]::PostMessage($hwnd, 0x0112, [IntPtr]0xF100, [IntPtr]0x20)
  Start-Sleep -Milliseconds 300
  Assert-True (-not $process.HasExited) "System menu smoke unexpectedly closed the window."
  $shell.SendKeys('{ESC}')
  Start-Sleep -Milliseconds 300

  [void]$shell.AppActivate($process.Id)
  [void][WindowChromeTestNative]::SetForegroundWindow($hwnd)
  Start-Sleep -Milliseconds 500
  $shell.SendKeys('%{F4}')
  Assert-True ($process.WaitForExit(5000)) "Alt+F4 did not close the window."

  [PSCustomObject]@{
    hwnd = $hwnd.ToString()
    captionHitTest = $captionHit
    rightResizeHitTest = $resizeHit
    maximizeHitTest = $maxHit
    maximizedBottom = $maximizedRect.Bottom
    workingAreaBottom = $screen.WorkingArea.Bottom
    closeExitCode = $process.ExitCode
    note = "Verified HWND hit tests, maximize/restore/minimize, taskbar-aware maximized bounds, Alt+Space smoke, and Alt+F4. Win+Arrow and snap-layout hover require manual verification."
  } | ConvertTo-Json -Depth 4
} finally {
  if ($process -and -not $process.HasExited) {
    $process.Kill()
    $process.WaitForExit()
  }
}
