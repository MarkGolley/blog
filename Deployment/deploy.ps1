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

$project = "my-blog-website-470819"
$service = "myblog-app"
$region = "europe-west2"
$tag = Get-Date -Format "yyyyMMdd-HHmmss"
$image = "gcr.io/$project/${service}:$tag"
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")

Push-Location $repoRoot
try {
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

    Write-Host "Deployed image: $image"
    Write-Host "APP_VERSION set to: $tag"
}
finally {
    Pop-Location
}
