[CmdletBinding()]
param(
    [string]$BaseUrl = "https://markgolley.dev",

    [string]$AdminKey,

    [int]$TimeoutSeconds = 20
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
    $effectiveAdminKey = $env:DAILY_CAPSULE_WARMUP_KEY
}

if ([string]::IsNullOrWhiteSpace($effectiveAdminKey)) {
    throw "Admin key missing. Pass -AdminKey or set DAILY_CAPSULE_WARMUP_KEY."
}

$trimmedBaseUrl = $BaseUrl.Trim().TrimEnd("/")
$parsedUri = $null
if (-not [Uri]::TryCreate($trimmedBaseUrl, [UriKind]::Absolute, [ref]$parsedUri)) {
    throw "BaseUrl must be an absolute URL, for example https://myblog.example.com"
}

$origin = $parsedUri.GetLeftPart([System.UriPartial]::Authority)
$endpoint = "$origin/admin/daily-capsule/warmup"
$headers = @{
    "X-Admin-Key" = $effectiveAdminKey
}

Write-Host "Triggering daily capsule warm-up..."
Write-Host "Endpoint: $endpoint"

$response = Invoke-RestMethod `
    -Method Post `
    -Uri $endpoint `
    -Headers $headers `
    -TimeoutSec $TimeoutSeconds

Write-Host ""
Write-Host "Completed."
Write-Host "Source: $($response.source)"
Write-Host "Type: $($response.capsuleType)"
Write-Host "Title: $($response.title)"
Write-Host "NextResetUtc: $($response.nextResetUtc)"
