[CmdletBinding()]
param(
    [string]$BaseUrl = "https://markgolley.dev",

    [string]$AdminKey,

    [int]$MinPerSingleMode = 8,

    [int]$MinPerKeyPair = 6,

    [int]$MaxMealsToGenerate = 2,

    [int]$TimeoutSeconds = 30
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ($PSVersionTable.PSEdition -eq "Desktop") {
    try {
        $securityProtocol = [Net.SecurityProtocolType]::Tls12
        if ([Enum]::GetNames([Net.SecurityProtocolType]) -contains "Tls13") {
            $securityProtocol = $securityProtocol -bor [Net.SecurityProtocolType]::Tls13
        }

        [Net.ServicePointManager]::SecurityProtocol = $securityProtocol
    }
    catch {
        # Keep defaults if TLS override is unavailable.
    }
}

$effectiveAdminKey = $AdminKey
if ([string]::IsNullOrWhiteSpace($effectiveAdminKey)) {
    $effectiveAdminKey = $env:AISLEPILOT_WARMUP_KEY
}

if ([string]::IsNullOrWhiteSpace($effectiveAdminKey)) {
    throw "Admin key missing. Pass -AdminKey or set AISLEPILOT_WARMUP_KEY."
}

$trimmedBaseUrl = $BaseUrl.Trim().TrimEnd("/")
$parsedUri = $null
if (-not [Uri]::TryCreate($trimmedBaseUrl, [UriKind]::Absolute, [ref]$parsedUri)) {
    throw "BaseUrl must be an absolute URL, for example https://myblog.example.com"
}

$origin = $parsedUri.GetLeftPart([System.UriPartial]::Authority)
$endpoint = "$origin/admin/aisle-pilot/warmup"
$headers = @{
    "X-Admin-Key" = $effectiveAdminKey
}
$body = @{
    "minPerSingleMode" = $MinPerSingleMode
    "minPerKeyPair" = $MinPerKeyPair
    "maxMealsToGenerate" = $MaxMealsToGenerate
}

Write-Host "Triggering AislePilot warm-up..."
Write-Host "Endpoint: $endpoint"
Write-Host "MinPerSingleMode: $MinPerSingleMode"
Write-Host "MinPerKeyPair: $MinPerKeyPair"
Write-Host "MaxMealsToGenerate: $MaxMealsToGenerate"

$response = Invoke-RestMethod `
    -Method Post `
    -Uri $endpoint `
    -Headers $headers `
    -Body $body `
    -TimeoutSec $TimeoutSeconds

Write-Host ""
Write-Host "Completed."
Write-Host "GeneratedCount: $($response.generatedCount)"
if ($response.generatedMealNames) {
    Write-Host "GeneratedMeals: $($response.generatedMealNames -join ', ')"
}

Write-Host "CoverageAfter:"
foreach ($item in $response.coverageAfter) {
    Write-Host (" - {0}: {1}/{2} (deficit {3})" -f $item.profile, $item.count, $item.target, $item.deficit)
}
