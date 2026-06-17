param(
  [switch]$SkipPublish,
  [ValidateSet("framework-dependent", "self-contained")]
  [string]$PublishMode = "framework-dependent",
  [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $root "Scrcap.sln"
$uiProject = Join-Path $root "src/Scrcap.Windows.UI/Scrcap.Windows.UI.csproj"
$guardScript = Join-Path $PSScriptRoot "Check-HardcodedUiColors.ps1"
$publishScript = Join-Path $PSScriptRoot "Publish-Windows.ps1"

function Invoke-Step {
  param(
    [string]$Name,
    [scriptblock]$Command
  )

  Write-Host "==> $Name"
  & $Command
}

Invoke-Step "UI token guard" {
  & $guardScript
}

Invoke-Step "Restore solution" {
  dotnet restore $solution
}

Invoke-Step "Build Windows UI" {
  dotnet build $uiProject -c $Configuration --no-restore
}

Invoke-Step "Run solution tests" {
  dotnet test $solution -c $Configuration --no-build
}

if (-not $SkipPublish) {
  Invoke-Step "Publish release artifact" {
    & $publishScript -Mode $PublishMode -Configuration $Configuration
  }
}

Write-Host "Release guardrails completed."
