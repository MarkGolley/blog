[CmdletBinding()]
param(
    [string]$ProjectId = "my-blog-website-470819",
    [string]$Region = "europe-west2",
    [string]$BlogService = "myblog-app",
    [string]$AislePilotService = "myblog-aislepilot"
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

Invoke-External `
    -Label "Disabling OTEL export on $BlogService and removing dashboard URL" `
    -Command "gcloud" `
    -Arguments @(
        "run", "services", "update", $BlogService,
        "--project", $ProjectId,
        "--region", $Region,
        "--update-env-vars", "OBSERVABILITY__ENABLE_OTLP=false",
        "--remove-env-vars", "OTEL_EXPORTER_OTLP_ENDPOINT,OTEL_EXPORTER_OTLP_HEADERS,Observability__PublicDashboardUrl"
    )

Invoke-External `
    -Label "Disabling OTEL export on $AislePilotService" `
    -Command "gcloud" `
    -Arguments @(
        "run", "services", "update", $AislePilotService,
        "--project", $ProjectId,
        "--region", $Region,
        "--update-env-vars", "OBSERVABILITY__ENABLE_OTLP=false",
        "--remove-env-vars", "OTEL_EXPORTER_OTLP_ENDPOINT,OTEL_EXPORTER_OTLP_HEADERS"
    )

Write-Host "Done. OTEL export is disabled and the public dashboard link is removed from the Projects page."
