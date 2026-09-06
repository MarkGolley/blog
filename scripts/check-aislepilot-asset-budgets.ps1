[CmdletBinding()]
param(
    [string]$RepoRoot = (Split-Path -Parent $PSScriptRoot),
    [int]$CssBudgetBytes = 365000,
    [int]$JavaScriptBudgetBytes = 411000,
    [int]$SingleJavaScriptBudgetBytes = 245000
)

$ErrorActionPreference = "Stop"
$cssFiles = Get-ChildItem -LiteralPath (Join-Path $RepoRoot "MyBlog/wwwroot/css") -Filter "aisle-pilot*.css" -File
$javascriptFiles = @(
    Get-ChildItem -LiteralPath (Join-Path $RepoRoot "MyBlog/wwwroot/js/aisle-pilot") -Filter "*.js" -File
    Get-Item -LiteralPath (Join-Path $RepoRoot "MyBlog/wwwroot/js/aisle-pilot.js")
)
$cssBytes = ($cssFiles | Measure-Object Length -Sum).Sum
$javascriptBytes = ($javascriptFiles | Measure-Object Length -Sum).Sum
$largestJavaScript = $javascriptFiles | Sort-Object Length -Descending | Select-Object -First 1

Write-Host "AislePilot CSS: $cssBytes / $CssBudgetBytes bytes"
Write-Host "AislePilot JavaScript: $javascriptBytes / $JavaScriptBudgetBytes bytes"
Write-Host "Largest JavaScript: $($largestJavaScript.Name) $($largestJavaScript.Length) / $SingleJavaScriptBudgetBytes bytes"

$failures = @()
if ($cssBytes -gt $CssBudgetBytes) { $failures += "CSS exceeds its budget by $($cssBytes - $CssBudgetBytes) bytes." }
if ($javascriptBytes -gt $JavaScriptBudgetBytes) { $failures += "JavaScript exceeds its budget by $($javascriptBytes - $JavaScriptBudgetBytes) bytes." }
if ($largestJavaScript.Length -gt $SingleJavaScriptBudgetBytes) { $failures += "$($largestJavaScript.Name) exceeds its single-file budget by $($largestJavaScript.Length - $SingleJavaScriptBudgetBytes) bytes." }

if ($failures.Count -gt 0)
{
    $failures | ForEach-Object { Write-Error $_ }
    exit 1
}

Write-Host "AislePilot asset budget check passed. Ratchet these transitional budgets downward as bundles are reduced."
