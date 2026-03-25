[CmdletBinding()]
param(
    [ValidateSet("Production", "Staging")]
    [string]$EnvironmentName = "Production",
    [string]$ProjectId = "my-blog-website-470819",
    [string]$Region = "europe-west2",
    [string]$Service = "",
    [string]$PublicBaseUrl = "",
    [string]$AislePilotPublicBaseUrl = "",
    [switch]$SkipPreDeployChecks,
    [switch]$SkipBrowserInstall,
    [switch]$SkipProductionSmokeCheck,
    [int]$DockerStartupTimeoutSeconds = 120
)

$ErrorActionPreference = "Stop"
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")

$args = @(
    "-NoProfile",
    "-ExecutionPolicy", "Bypass",
    "-File", (Join-Path $repoRoot "Deployment\deploy.ps1"),
    "-EnvironmentName", $EnvironmentName,
    "-ProjectId", $ProjectId,
    "-Region", $Region,
    "-AppMode", "BlogOnly",
    "-Memory", "256Mi",
    "-Cpu", "0.25",
    "-Concurrency", "10",
    "-MinInstances", "0",
    "-MaxInstances", "2",
    "-RequestTimeout", "10m",
    "-DockerStartupTimeoutSeconds", "$DockerStartupTimeoutSeconds"
)

if (-not [string]::IsNullOrWhiteSpace($Service)) {
    $args += @("-Service", $Service.Trim())
}

if (-not [string]::IsNullOrWhiteSpace($PublicBaseUrl)) {
    $args += @("-PublicBaseUrl", $PublicBaseUrl.Trim())
}

if (-not [string]::IsNullOrWhiteSpace($AislePilotPublicBaseUrl)) {
    $args += @("-AislePilotPublicBaseUrl", $AislePilotPublicBaseUrl.Trim())
}

if ($SkipPreDeployChecks) {
    $args += "-SkipPreDeployChecks"
}

if ($SkipBrowserInstall) {
    $args += "-SkipBrowserInstall"
}

if ($SkipProductionSmokeCheck) {
    $args += "-SkipProductionSmokeCheck"
}

& powershell @args
if ($LASTEXITCODE -ne 0) {
    throw "Blog deployment failed with exit code $LASTEXITCODE."
}
