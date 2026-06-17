$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$uiRoot = Join-Path $root "src/Scrcap.Windows.UI"
$tokenFile = Join-Path $uiRoot "Resources/ThemeTokens.xaml"

$patterns = @(
  @{
    Name = "hard-coded color"
    Pattern = '#[0-9A-Fa-f]{6,8}'
    Message = "Use a ThemeTokens.xaml color or brush resource."
  },
  @{
    Name = "hard-coded font size"
    Pattern = '\bFontSize\s*=\s*"[0-9]+(?:\.[0-9]+)?"'
    Message = "Use a FontSize* resource from ThemeTokens.xaml."
  },
  @{
    Name = "hard-coded layout metric"
    Pattern = '\b(?:Width|Height|MinWidth|MinHeight|MaxWidth|MaxHeight|CornerRadius|BorderThickness)\s*=\s*"[0-9]+(?:\.[0-9]+)?"'
    Message = "Use a Metric* resource from ThemeTokens.xaml for fixed visible UI metrics."
  },
  @{
    Name = "hard-coded spacing metric"
    Pattern = '\b(?:Margin|Padding)\s*=\s*"[0-9]+(?:\.[0-9]+)?(?:,[0-9]+(?:\.[0-9]+)?){0,3}"'
    Message = "Use a Thickness/Metric resource from ThemeTokens.xaml for fixed visible UI spacing."
  }
)

$violations = Get-ChildItem $uiRoot -Recurse -Filter *.xaml |
  Where-Object { $_.FullName -ne $tokenFile } |
  ForEach-Object {
    $file = $_
    foreach ($entry in $patterns) {
      Select-String -Path $file.FullName -Pattern $entry.Pattern |
        ForEach-Object {
          [PSCustomObject]@{
            Path = $_.Path
            LineNumber = $_.LineNumber
            Rule = $entry.Name
            Message = $entry.Message
            Line = $_.Line.Trim()
          }
        }
    }
  }

if ($violations) {
  foreach ($violation in $violations) {
    Write-Error "$($violation.Path):$($violation.LineNumber): $($violation.Rule): $($violation.Message) $($violation.Line)"
  }
  exit 1
}

Write-Host "No hard-coded XAML colors, font sizes, or fixed visible metrics outside ThemeTokens.xaml."
