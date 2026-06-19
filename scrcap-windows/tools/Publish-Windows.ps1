param(
  [ValidateSet("framework-dependent", "self-contained")]
  [string]$Mode = "framework-dependent",
  [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$repoRoot = Split-Path -Parent $root
$project = Join-Path $root "src/Scrcap.Windows.UI/Scrcap.Windows.UI.csproj"
$output = Join-Path $root "artifacts/scrcap-windows-$Mode"
$selfContained = if ($Mode -eq "self-contained") { "true" } else { "false" }
$licenseCandidates = @(
  (Join-Path $root "LICENSE.txt"),
  (Join-Path $root "LICENSE"),
  (Join-Path $repoRoot "LICENSE.txt"),
  (Join-Path $repoRoot "LICENSE")
)

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
  throw "dotnet is required to publish scrcap for Windows."
}

if (Test-Path $output) {
  Remove-Item $output -Recurse -Force
}

$global:LASTEXITCODE = 0
dotnet publish $project `
  -c $Configuration `
  -r win-x64 `
  --self-contained:$selfContained `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:DebugType=none `
  -p:DebugSymbols=false `
  -o $output

if ($global:LASTEXITCODE -ne 0) {
  throw "dotnet publish failed with exit code $global:LASTEXITCODE."
}

if (-not (Test-Path $output)) {
  Write-Error "Publish did not create output directory: $output"
  exit 1
}

$publishedExe = Join-Path $output "Scrcap.Windows.UI.exe"
$releaseExe = Join-Path $output "scrcap.exe"
if (Test-Path $publishedExe) {
  Move-Item $publishedExe $releaseExe -Force
}

$licenseSource = $licenseCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $licenseSource) {
  Write-Error "Release output requires a license file. Add scrcap-windows/LICENSE.txt or a root LICENSE file before publishing."
  exit 1
}

Copy-Item $licenseSource (Join-Path $output "LICENSE.txt") -Force

$blocked = Get-ChildItem $output -Recurse |
  Where-Object {
    $_.Extension -in ".pdb", ".tmp" -or
    $_.FullName -match "\\(obj|bin|TestResults|baselines)\\"
  }

if ($blocked) {
  $blocked | ForEach-Object { Write-Error "Release output contains development artifact: $($_.FullName)" }
  exit 1
}

if (-not (Test-Path $releaseExe)) {
  Write-Error "Release output is missing scrcap.exe."
  exit 1
}

$releaseLicense = Join-Path $output "LICENSE.txt"
if (-not (Test-Path $releaseLicense) -or (Get-Item $releaseLicense).Length -le 0) {
  Write-Error "Release output is missing LICENSE.txt."
  exit 1
}

Write-Host "Published $Mode package to $output"
