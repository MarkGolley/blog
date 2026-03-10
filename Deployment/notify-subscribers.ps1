param(
    [Parameter(Mandatory = $true)]
    [string]$BaseUrl,

    [Parameter(Mandatory = $true)]
    [string]$PostSlug,

    [string]$AdminKey,

    [int]$TimeoutSeconds = 30
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($BaseUrl)) {
    throw "BaseUrl is required."
}

if ([string]::IsNullOrWhiteSpace($PostSlug)) {
    throw "PostSlug is required."
}

$effectiveAdminKey = $AdminKey
if ([string]::IsNullOrWhiteSpace($effectiveAdminKey)) {
    $effectiveAdminKey = $env:SUBSCRIBER_NOTIFY_KEY
}

if ([string]::IsNullOrWhiteSpace($effectiveAdminKey)) {
    throw "Admin key missing. Pass -AdminKey or set SUBSCRIBER_NOTIFY_KEY."
}

$trimmedBaseUrl = $BaseUrl.Trim().TrimEnd("/")
$parsedUri = $null
if (-not [Uri]::TryCreate($trimmedBaseUrl, [UriKind]::Absolute, [ref]$parsedUri)) {
    throw "BaseUrl must be an absolute URL, for example https://myblog.example.com"
}

$endpoint = "$trimmedBaseUrl/admin/subscribers/notify-post"
$headers = @{
    "X-Admin-Key" = $effectiveAdminKey
}
$body = @{
    "PostSlug" = $PostSlug
}

Write-Host "Triggering subscriber notification..."
Write-Host "Endpoint: $endpoint"
Write-Host "PostSlug: $PostSlug"

$response = Invoke-RestMethod `
    -Method Post `
    -Uri $endpoint `
    -Headers $headers `
    -Body $body `
    -TimeoutSec $TimeoutSeconds

Write-Host ""
Write-Host "Completed."
Write-Host "PostId: $($response.postId)"
Write-Host "Sent: $($response.sent)"
Write-Host "Skipped: $($response.skipped)"
Write-Host "Failed: $($response.failed)"
