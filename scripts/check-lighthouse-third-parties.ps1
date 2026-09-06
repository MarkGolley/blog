[CmdletBinding()]
param(
    [string]$ReportRoot = (Join-Path (Split-Path -Parent $PSScriptRoot) "artifacts/lighthouse"),
    [int]$GoogleFontRequestBudget = 5
)

$ErrorActionPreference = "Stop"
$allowedThirdParties = @("fonts.googleapis.com", "fonts.gstatic.com")
$reports = Get-ChildItem -LiteralPath $ReportRoot -Filter "*.report.json" -File -Recurse
if ($reports.Count -eq 0) { throw "No Lighthouse JSON reports found under $ReportRoot." }

foreach ($report in $reports) {
    $lhr = Get-Content -LiteralPath $report.FullName -Raw | ConvertFrom-Json
    $hosts = @($lhr.audits.'network-requests'.details.items | ForEach-Object { ([Uri]$_.url).Host })
    $firstPartyHost = ([Uri]$lhr.finalUrl).Host
    $unexpected = @($hosts | Where-Object { $_ -ne $firstPartyHost -and $_ -notin $allowedThirdParties } | Select-Object -Unique)
    if ($unexpected.Count -gt 0) { throw "Unexpected third-party hosts in $($report.Name): $($unexpected -join ', ')." }

    $fontRequests = @($hosts | Where-Object { $_ -in $allowedThirdParties }).Count
    if ($fontRequests -gt $GoogleFontRequestBudget) {
        throw "$($report.Name) made $fontRequests Google Font requests; budget is $GoogleFontRequestBudget."
    }
    Write-Host "$($report.Name): third-party request budget passed ($fontRequests / $GoogleFontRequestBudget Google Font requests)."
}
