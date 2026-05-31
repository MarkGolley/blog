[CmdletBinding()]
param(
    [string]$ProjectId = "my-blog-website-470819",
    [string]$Region = "europe-west2",
    [string]$BlogService = "myblog-app",
    [string]$AislePilotService = "myblog-aislepilot",
    [Parameter(Mandatory = $true)]
    [string]$DashboardUrl,
    [Parameter(Mandatory = $true)]
    [string]$OtlpEndpoint,
    [Parameter(Mandatory = $true)]
    [string]$OtlpHeaders,
    [string]$OtlpProtocol = "http/protobuf"
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

if (-not [Uri]::TryCreate($DashboardUrl, [UriKind]::Absolute, [ref]$null)) {
    throw "DashboardUrl must be an absolute URL."
}
if (-not [Uri]::TryCreate($OtlpEndpoint, [UriKind]::Absolute, [ref]$null)) {
    throw "OtlpEndpoint must be an absolute URL."
}

$normalizedDashboardUrl = $DashboardUrl.Trim()
$normalizedOtlpEndpoint = $OtlpEndpoint.Trim()
$normalizedOtlpHeaders = $OtlpHeaders.Trim()
$normalizedOtlpProtocol = $OtlpProtocol.Trim()

if ([string]::IsNullOrWhiteSpace($normalizedOtlpHeaders)) {
    throw "OtlpHeaders is required."
}

# Use a custom delimiter because OTLP headers can contain commas.
$blogEnvVars = @(
    "OBSERVABILITY__ENABLE_OTLP=true",
    "OBSERVABILITY__EXPORT_TRACES=true",
    "OBSERVABILITY__EXPORT_METRICS=true",
    "OBSERVABILITY__EXPORT_LOGS=true",
    "OTEL_EXPORTER_OTLP_PROTOCOL=$normalizedOtlpProtocol",
    "OTEL_EXPORTER_OTLP_ENDPOINT=$normalizedOtlpEndpoint",
    "OTEL_EXPORTER_OTLP_HEADERS=$normalizedOtlpHeaders",
    "Observability__PublicDashboardUrl=$normalizedDashboardUrl"
)

$aislePilotEnvVars = @(
    "OBSERVABILITY__ENABLE_OTLP=true",
    "OBSERVABILITY__EXPORT_TRACES=true",
    "OBSERVABILITY__EXPORT_METRICS=true",
    "OBSERVABILITY__EXPORT_LOGS=true",
    "OTEL_EXPORTER_OTLP_PROTOCOL=$normalizedOtlpProtocol",
    "OTEL_EXPORTER_OTLP_ENDPOINT=$normalizedOtlpEndpoint",
    "OTEL_EXPORTER_OTLP_HEADERS=$normalizedOtlpHeaders"
)

$blogEnvVarsArgument = "^##^" + ([string]::Join("##", $blogEnvVars))
$aislePilotEnvVarsArgument = "^##^" + ([string]::Join("##", $aislePilotEnvVars))

Invoke-External `
    -Label "Updating $BlogService observability settings" `
    -Command "gcloud" `
    -Arguments @(
        "run", "services", "update", $BlogService,
        "--project", $ProjectId,
        "--region", $Region,
        "--update-env-vars", $blogEnvVarsArgument
    )

Invoke-External `
    -Label "Updating $AislePilotService observability settings" `
    -Command "gcloud" `
    -Arguments @(
        "run", "services", "update", $AislePilotService,
        "--project", $ProjectId,
        "--region", $Region,
        "--update-env-vars", $aislePilotEnvVarsArgument
    )

Write-Host "Done. Cloud Run now exports OTEL to Grafana Cloud and the Projects page dashboard link is set."
