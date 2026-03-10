[CmdletBinding()]
param(
    [string]$BaseUrl = "https://markgolley.dev",

    [string]$PostSlug,

    [string]$AdminKey,

    [int]$TimeoutSeconds = 30,

    [string]$BlogStoragePath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($BlogStoragePath)) {
    $scriptDir = Split-Path -Parent $PSCommandPath
    $repoRoot = (Resolve-Path (Join-Path $scriptDir "..")).Path
    $BlogStoragePath = Join-Path $repoRoot "MyBlog\wwwroot\BlogStorage"
}

if ([string]::IsNullOrWhiteSpace($BaseUrl)) {
    throw "BaseUrl cannot be empty."
}

if ([string]::IsNullOrWhiteSpace($PostSlug)) {
    if (-not (Test-Path -Path $BlogStoragePath -PathType Container)) {
        throw "PostSlug not provided and BlogStorage path was not found: $BlogStoragePath"
    }

    $postFiles = Get-ChildItem -Path $BlogStoragePath -Filter "*.html" -File |
        Sort-Object LastWriteTime -Descending

    if ($postFiles.Count -eq 0) {
        throw "No .html posts found in BlogStorage path: $BlogStoragePath"
    }

    Write-Host "PostSlug not provided. Select a post:"
    for ($i = 0; $i -lt $postFiles.Count; $i++) {
        $slug = [System.IO.Path]::GetFileNameWithoutExtension($postFiles[$i].Name)
        $updated = $postFiles[$i].LastWriteTime.ToString("yyyy-MM-dd")
        Write-Host ("[{0}] {1} ({2})" -f ($i + 1), $slug, $updated)
    }

    $selected = Read-Host "Enter number (1-$($postFiles.Count))"
    $selectedIndex = 0
    if (-not [int]::TryParse($selected, [ref]$selectedIndex)) {
        throw "Invalid selection: '$selected'"
    }

    if ($selectedIndex -lt 1 -or $selectedIndex -gt $postFiles.Count) {
        throw "Selection out of range: $selectedIndex"
    }

    $PostSlug = [System.IO.Path]::GetFileNameWithoutExtension($postFiles[$selectedIndex - 1].Name)
    Write-Host "Selected PostSlug: $PostSlug"
}
else {
    if (Test-Path -Path $BlogStoragePath -PathType Container) {
        $knownSlugs = Get-ChildItem -Path $BlogStoragePath -Filter "*.html" -File |
            ForEach-Object { [System.IO.Path]::GetFileNameWithoutExtension($_.Name) }
        if ($knownSlugs.Count -gt 0 -and -not ($knownSlugs -contains $PostSlug)) {
            Write-Warning "PostSlug '$PostSlug' was not found in local BlogStorage list. Continuing anyway."
        }
    }
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

$origin = $parsedUri.GetLeftPart([System.UriPartial]::Authority)
$endpoint = "$origin/admin/subscribers/notify-post"

$headers = @{
    "X-Admin-Key" = $effectiveAdminKey
}
$body = @{
    "PostSlug" = $PostSlug
}

Write-Host "Triggering subscriber notification..."
Write-Host "PostSlug: $PostSlug"
Write-Host "Endpoint: $endpoint"

$response = Invoke-RestMethod `
    -Method Post `5
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
