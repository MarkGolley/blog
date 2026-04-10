[CmdletBinding()]
param(
    [ValidateSet("Production", "Staging")]
    [string]$EnvironmentName = "Production",
    [string]$ProjectId = "my-blog-website-470819",
    [string]$Region = "europe-west2",
    [string]$BlogService = "",
    [string]$AislePilotService = "",
    [string]$PublicBaseUrl = "",
    [string]$AislePilotPublicBaseUrl = "",
    [switch]$SkipPreDeployChecks,
    [switch]$SkipBrowserInstall,
    [switch]$FullPreDeployE2E,
    [switch]$SkipAislePilotSmokeCheck,
    [switch]$SkipProductionSmokeCheck,
    [switch]$SkipVersionParityCheck,
    [int]$DockerStartupTimeoutSeconds = 120
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Invoke-External {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Label,
        [Parameter(Mandatory = $true)]
        [string]$Command,
        [Parameter(Mandatory = $false)]
        [string[]]$Arguments = @()
    )

    Write-Host "==> $Label"
    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Label failed with exit code $LASTEXITCODE."
    }
}

function Get-ServiceDefaults {
    param(
        [Parameter(Mandatory = $true)]
        [string]$EnvironmentName
    )

    if ($EnvironmentName -eq "Production") {
        return [PSCustomObject]@{
            BlogService       = "myblog-app"
            AislePilotService = "myblog-aislepilot"
            PublicBaseUrl     = "https://markgolley.dev"
            AspNetEnvironment = "Production"
        }
    }

    return [PSCustomObject]@{
        BlogService       = "myblog-app-staging"
        AislePilotService = "myblog-aislepilot-staging"
        PublicBaseUrl     = ""
        AspNetEnvironment = "Staging"
    }
}

function Get-CloudRunService {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Service,
        [Parameter(Mandatory = $true)]
        [string]$Region,
        [Parameter(Mandatory = $true)]
        [string]$ProjectId
    )

    $json = & gcloud run services describe $Service --region $Region --project $ProjectId --format json
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($json)) {
        throw "Unable to describe Cloud Run service '$Service'."
    }

    return $json | ConvertFrom-Json
}

function Get-CloudRunRevision {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Revision,
        [Parameter(Mandatory = $true)]
        [string]$Region,
        [Parameter(Mandatory = $true)]
        [string]$ProjectId
    )

    $json = & gcloud run revisions describe $Revision --region $Region --project $ProjectId --format json
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($json)) {
        throw "Unable to describe Cloud Run revision '$Revision'."
    }

    return $json | ConvertFrom-Json
}

function Get-AppVersionFromUrl {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Url
    )

    $headers = & curl.exe -sD - -o NUL -L $Url
    if ($LASTEXITCODE -ne 0) {
        throw "Header request failed for '$Url'."
    }

    $versions = @()
    foreach ($line in $headers) {
        if ($line -match '^[xX]-app-version:\s*(.+)\s*$') {
            $versions += $Matches[1].Trim()
        }
    }

    if ($versions.Count -eq 0) {
        throw "x-app-version header was not found for '$Url'."
    }

    return $versions[-1]
}

function Get-BaseUrl {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Url
    )

    return $Url.Trim().TrimEnd("/")
}

$defaults = Get-ServiceDefaults -EnvironmentName $EnvironmentName
$effectiveBlogService = if ([string]::IsNullOrWhiteSpace($BlogService)) { $defaults.BlogService } else { $BlogService.Trim() }
$effectiveAislePilotService = if ([string]::IsNullOrWhiteSpace($AislePilotService)) { $defaults.AislePilotService } else { $AislePilotService.Trim() }
$effectivePublicBaseUrl = if ([string]::IsNullOrWhiteSpace($PublicBaseUrl)) { $defaults.PublicBaseUrl } else { $PublicBaseUrl.Trim() }

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$deployScript = Join-Path $repoRoot "Deployment\deploy.ps1"

$blogDeployArgs = @(
    "-NoProfile",
    "-ExecutionPolicy", "Bypass",
    "-File", $deployScript,
    "-EnvironmentName", $EnvironmentName,
    "-ProjectId", $ProjectId,
    "-Region", $Region,
    "-Service", $effectiveBlogService,
    "-AppMode", "Combined",
    "-DockerStartupTimeoutSeconds", "$DockerStartupTimeoutSeconds"
)

if (-not [string]::IsNullOrWhiteSpace($PublicBaseUrl)) {
    $blogDeployArgs += @("-PublicBaseUrl", $PublicBaseUrl.Trim())
}

if (-not [string]::IsNullOrWhiteSpace($AislePilotPublicBaseUrl)) {
    $blogDeployArgs += @("-AislePilotPublicBaseUrl", $AislePilotPublicBaseUrl.Trim())
}

if ($SkipPreDeployChecks) {
    $blogDeployArgs += "-SkipPreDeployChecks"
}

if ($SkipBrowserInstall) {
    $blogDeployArgs += "-SkipBrowserInstall"
}

if ($FullPreDeployE2E) {
    $blogDeployArgs += "-FullPreDeployE2E"
}

if ($SkipAislePilotSmokeCheck) {
    $blogDeployArgs += "-SkipAislePilotSmokeCheck"
}

if ($SkipProductionSmokeCheck) {
    $blogDeployArgs += "-SkipProductionSmokeCheck"
}

Invoke-External -Label "Deploying blog service ($effectiveBlogService) with existing deployment pipeline" -Command "powershell" -Arguments $blogDeployArgs

$blogServiceDescription = Get-CloudRunService -Service $effectiveBlogService -Region $Region -ProjectId $ProjectId
$blogRevision = [string]$blogServiceDescription.status.latestReadyRevisionName
if ([string]::IsNullOrWhiteSpace($blogRevision)) {
    throw "Unable to determine latest ready revision for '$effectiveBlogService'."
}

$blogRevisionDescription = Get-CloudRunRevision -Revision $blogRevision -Region $Region -ProjectId $ProjectId
$blogImage = [string]$blogRevisionDescription.spec.containers[0].image
if ([string]::IsNullOrWhiteSpace($blogImage)) {
    throw "Unable to determine container image for revision '$blogRevision'."
}

$blogDirectBaseUrl = Get-BaseUrl -Url ([string]$blogServiceDescription.status.url)
$versionProbePath = "/projects/aisle-pilot?returnUrl=%2Fprojects"
$blogVersion = Get-AppVersionFromUrl -Url "$blogDirectBaseUrl$versionProbePath"

$envVars = @(
    "APP_VERSION=$blogVersion",
    "ASPNETCORE_ENVIRONMENT=$($defaults.AspNetEnvironment)",
    "App__Mode=AislePilotOnly"
)
$envVarsArgument = [string]::Join(",", $envVars)

Invoke-External `
    -Label "Deploying AislePilot service ($effectiveAislePilotService) from blog image digest" `
    -Command "gcloud" `
    -Arguments @(
        "run", "deploy", $effectiveAislePilotService,
        "--image", $blogImage,
        "--region", $Region,
        "--project", $ProjectId,
        "--platform", "managed",
        "--allow-unauthenticated",
        "--update-env-vars", $envVarsArgument,
        "--memory=1Gi",
        "--cpu=1",
        "--concurrency=10",
        "--min-instances=0",
        "--max-instances=3",
        "--timeout", "10m"
    )

$aislePilotServiceDescription = Get-CloudRunService -Service $effectiveAislePilotService -Region $Region -ProjectId $ProjectId
$aislePilotDirectBaseUrl = Get-BaseUrl -Url ([string]$aislePilotServiceDescription.status.url)

if ($SkipVersionParityCheck) {
    Write-Host "Skipping version parity checks because -SkipVersionParityCheck was provided."
}
else {
    $targets = @(
        [PSCustomObject]@{ Name = "$effectiveBlogService (direct)"; Url = "$blogDirectBaseUrl$versionProbePath" },
        [PSCustomObject]@{ Name = "$effectiveAislePilotService (direct)"; Url = "$aislePilotDirectBaseUrl$versionProbePath" }
    )

    if (-not [string]::IsNullOrWhiteSpace($effectivePublicBaseUrl)) {
        $publicBase = Get-BaseUrl -Url $effectivePublicBaseUrl
        $targets += [PSCustomObject]@{ Name = "Public domain"; Url = "$publicBase$versionProbePath" }
    }

    $results = @()
    foreach ($target in $targets) {
        $version = Get-AppVersionFromUrl -Url $target.Url
        $results += [PSCustomObject]@{
            Name    = $target.Name
            Url     = $target.Url
            Version = $version
        }
    }

    $uniqueVersions = @($results | Select-Object -ExpandProperty Version -Unique)
    if ($uniqueVersions.Count -ne 1) {
        Write-Host "Version parity check failed:"
        foreach ($result in $results) {
            Write-Host " - $($result.Name): $($result.Version) ($($result.Url))"
        }
        throw "Deployment drift detected across blog/aisle-pilot/public endpoints."
    }

    Write-Host "Version parity check passed: $($uniqueVersions[0])"
}

Write-Host "Environment: $EnvironmentName"
Write-Host "Blog service: $effectiveBlogService"
Write-Host "AislePilot service: $effectiveAislePilotService"
Write-Host "Shared image digest: $blogImage"
Write-Host "Shared APP_VERSION: $blogVersion"
