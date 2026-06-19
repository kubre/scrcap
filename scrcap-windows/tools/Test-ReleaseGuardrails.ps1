param(
  [switch]$SkipPublish,
  [ValidateSet("framework-dependent", "self-contained")]
  [string]$PublishMode = "framework-dependent",
  [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $root "Scrcap.sln"
$guardScript = Join-Path $PSScriptRoot "Check-HardcodedUiColors.ps1"
$hardeningScript = Join-Path $PSScriptRoot "Test-ReleaseHardening.ps1"
$publishScript = Join-Path $PSScriptRoot "Publish-Windows.ps1"

function Invoke-Step {
  param(
    [string]$Name,
    [scriptblock]$Command
  )

  Write-Host "==> $Name"
  $global:LASTEXITCODE = 0
  & $Command
  if ($global:LASTEXITCODE -ne 0) {
    throw "$Name failed with exit code $global:LASTEXITCODE."
  }
}

Invoke-Step "UI token guard" {
  & $guardScript
}

Invoke-Step "Release hardening checks" {
  & $hardeningScript
}

Invoke-Step "Restore solution" {
  dotnet restore $solution
}

Invoke-Step "Build solution" {
  dotnet build $solution -c $Configuration --no-restore
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
