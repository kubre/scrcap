param(
  [ValidateSet("framework-dependent", "self-contained")]
  [string]$Mode = "framework-dependent",
  [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "src/Scrcap.Windows.UI/Scrcap.Windows.UI.csproj"
$output = Join-Path $root "artifacts/scrcap-windows-$Mode"
$selfContained = if ($Mode -eq "self-contained") { "true" } else { "false" }

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
  throw "dotnet is required to publish scrcap for Windows."
}

if (Test-Path $output) {
  Remove-Item $output -Recurse -Force
}

dotnet publish $project `
  -c $Configuration `
  -r win-x64 `
  --self-contained:$selfContained `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:DebugType=none `
  -p:DebugSymbols=false `
  -o $output

$blocked = Get-ChildItem $output -Recurse |
  Where-Object {
    $_.Extension -in ".pdb", ".tmp" -or
    $_.FullName -match "\\(obj|bin|TestResults|baselines)\\"
  }

if ($blocked) {
  $blocked | ForEach-Object { Write-Error "Release output contains development artifact: $($_.FullName)" }
  exit 1
}

$exe = Join-Path $output "Scrcap.Windows.UI.exe"
if (-not (Test-Path $exe)) {
  Write-Error "Release output is missing Scrcap.Windows.UI.exe."
  exit 1
}

Write-Host "Published $Mode package to $output"
