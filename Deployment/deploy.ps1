[CmdletBinding()]
param(
    [switch]$SkipPreDeployChecks,
    [switch]$SkipBrowserInstall,
    [switch]$SkipProductionSmokeCheck,
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

function Test-DockerReady {
    & docker info *> $null
    return $LASTEXITCODE -eq 0
}

function Ensure-DockerRunning {
    param(
        [Parameter(Mandatory = $true)]
        [int]$TimeoutSeconds
    )

    if (Test-DockerReady) {
        return
    }

    Write-Host "Docker daemon is not ready. Attempting to start Docker Desktop..."

    $dockerDesktopProcess = Get-Process -Name "Docker Desktop" -ErrorAction SilentlyContinue
    if (-not $dockerDesktopProcess) {
        $candidatePaths = @(
            "$env:ProgramFiles\Docker\Docker\Docker Desktop.exe",
            "$env:ProgramFiles(x86)\Docker\Docker\Docker Desktop.exe",
            "$env:LocalAppData\Docker\Docker Desktop.exe"
        ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) -and (Test-Path $_) }

        if ($candidatePaths.Count -eq 0) {
            throw "Docker is not ready and Docker Desktop was not found. Start Docker Desktop manually, then rerun deploy."
        }

        Start-Process -FilePath $candidatePaths[0] | Out-Null
    }

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Seconds 3
        if (Test-DockerReady) {
            Write-Host "Docker is ready."
            return
        }
    }

    throw "Docker did not become ready within $TimeoutSeconds seconds. Check Docker Desktop and rerun deploy."
}

$project = "my-blog-website-470819"
$service = "myblog-app"
$region = "europe-west2"
$tag = Get-Date -Format "yyyyMMdd-HHmmss"
$image = "gcr.io/$project/${service}:$tag"
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")

Push-Location $repoRoot
try {
    if (-not $SkipPreDeployChecks) {
        $checkArgs = @(
            "-NoProfile",
            "-ExecutionPolicy", "Bypass",
            "-File", (Join-Path $repoRoot "run_checks.ps1"),
            "-Mode", "PreDeploy"
        )

        if ($SkipBrowserInstall) {
            $checkArgs += "-SkipBrowserInstall"
        }

        Invoke-External `
            -Label "Running pre-deploy checks" `
            -Command "powershell" `
            -Arguments $checkArgs
    }
    else {
        Write-Host "Skipping pre-deploy checks because -SkipPreDeployChecks was provided."
    }

    Ensure-DockerRunning -TimeoutSeconds $DockerStartupTimeoutSeconds

    $previousBuildkit = $env:DOCKER_BUILDKIT
    $env:DOCKER_BUILDKIT = "0"
    Invoke-External `
        -Label "Building Docker image" `
        -Command "docker" `
        -Arguments @("build", "-t", $image, ".")

    if ($null -eq $previousBuildkit) {
        Remove-Item Env:DOCKER_BUILDKIT -ErrorAction SilentlyContinue
    }
    else {
        $env:DOCKER_BUILDKIT = $previousBuildkit
    }

    Invoke-External `
        -Label "Pushing Docker image to GCR" `
        -Command "docker" `
        -Arguments @("push", $image)

    Invoke-External `
        -Label "Deploying to Cloud Run" `
        -Command "gcloud" `
        -Arguments @(
            "run", "deploy", $service,
            "--image", $image,
            "--region", $region,
            "--platform", "managed",
            "--allow-unauthenticated",
            "--set-env-vars", "APP_VERSION=$tag",
            "--memory=256Mi",
            "--cpu=0.25",
            "--concurrency=1",
            "--min-instances=0",
            "--max-instances=1",
            "--timeout", "10m"
        )

    if (-not $SkipProductionSmokeCheck) {
        Invoke-External `
            -Label "Running production auth smoke checks" `
            -Command "powershell" `
            -Arguments @(
                "-NoProfile",
                "-ExecutionPolicy", "Bypass",
                "-File", (Join-Path $repoRoot "Deployment\verify-production-auth.ps1"),
                "-ProjectId", $project,
                "-Service", $service,
                "-Region", $region
            )
    }
    else {
        Write-Host "Skipping production auth smoke checks because -SkipProductionSmokeCheck was provided."
    }

    Write-Host "Deployed image: $image"
    Write-Host "APP_VERSION set to: $tag"
}
finally {
    Pop-Location
}
