[CmdletBinding()]
param(
    [string]$BaseUrl = "http://localhost:5207",
    [string]$DailyCapsuleWarmupKey = "local-daily-capsule-warmup",
    [string]$AislePilotWarmupKey = "local-aislepilot-warmup",
    [int]$Iterations = 1,
    [int]$PauseMilliseconds = 600
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Invoke-DemoRequest {
    param(
        [Parameter(Mandatory = $true)][string]$Method,
        [Parameter(Mandatory = $true)][string]$Uri,
        [hashtable]$Headers = @{},
        [hashtable]$Form = @{}
    )

    $correlationId = [Guid]::NewGuid().ToString("N")
    $requestHeaders = @{}
    foreach ($pair in $Headers.GetEnumerator()) {
        $requestHeaders[$pair.Key] = $pair.Value
    }
    $requestHeaders["X-Correlation-ID"] = $correlationId

    $started = Get-Date
    try {
        if ($Method -eq "GET") {
            $response = Invoke-WebRequest -Method Get -Uri $Uri -Headers $requestHeaders -UseBasicParsing
        } else {
            $response = Invoke-WebRequest -Method Post -Uri $Uri -Headers $requestHeaders -Body $Form -UseBasicParsing
        }

        $durationMs = [math]::Round(((Get-Date) - $started).TotalMilliseconds, 2)
        return [PSCustomObject]@{
            Uri = $Uri
            Method = $Method
            StatusCode = [int]$response.StatusCode
            DurationMs = $durationMs
            CorrelationId = $correlationId
            Success = $true
            Body = $response.Content
        }
    } catch {
        $durationMs = [math]::Round(((Get-Date) - $started).TotalMilliseconds, 2)
        $statusCode = 0
        $responseProperty = $_.Exception.PSObject.Properties["Response"]
        if ($null -ne $responseProperty -and $null -ne $responseProperty.Value) {
            $statusCodeProperty = $responseProperty.Value.PSObject.Properties["StatusCode"]
            if ($null -ne $statusCodeProperty -and $null -ne $statusCodeProperty.Value) {
                $statusCode = [int]$statusCodeProperty.Value
            }
        }

        return [PSCustomObject]@{
            Uri = $Uri
            Method = $Method
            StatusCode = $statusCode
            DurationMs = $durationMs
            CorrelationId = $correlationId
            Success = $false
            Body = $_.Exception.Message
        }
    }
}

Write-Host "Running diagnosable scenario against $BaseUrl"
Write-Host "If OBSERVABILITY_OPENAI_API_KEY is left unset, AI calls will fail intentionally and become observable."
Write-Host ""

$results = New-Object System.Collections.Generic.List[object]

$results.Add((Invoke-DemoRequest -Method "GET" -Uri "$BaseUrl/health"))

for ($i = 1; $i -le $Iterations; $i++) {
    $results.Add((Invoke-DemoRequest -Method "POST" -Uri "$BaseUrl/admin/daily-capsule/warmup" -Headers @{ "X-Admin-Key" = $DailyCapsuleWarmupKey }))
    $results.Add((Invoke-DemoRequest -Method "POST" -Uri "$BaseUrl/admin/aisle-pilot/warmup" -Headers @{ "X-Admin-Key" = $AislePilotWarmupKey } -Form @{
            minPerSingleMode = 8
            minPerKeyPair = 6
            maxMealsToGenerate = 1
        }))
    $results.Add((Invoke-DemoRequest -Method "GET" -Uri "$BaseUrl/projects/aisle-pilot"))

    Start-Sleep -Milliseconds $PauseMilliseconds
}

$results.Add((Invoke-DemoRequest -Method "GET" -Uri "$BaseUrl/health"))

Write-Host ""
Write-Host "Scenario complete. Request summary:"
$results |
    Select-Object Method, Uri, StatusCode, DurationMs, CorrelationId, Success |
    Format-Table -AutoSize

$errors = $results | Where-Object { $_.Success -eq $false -or $_.StatusCode -ge 400 }
Write-Host ""
Write-Host ("Total requests: {0}" -f $results.Count)
Write-Host ("Error/non-2xx responses: {0}" -f $errors.Count)
Write-Host ""
Write-Host "Next steps:"
Write-Host "1) Grafana -> MyBlog Operational Overview"
Write-Host "2) Grafana -> MyBlog AI / LLM Observability"
Write-Host "3) Open a failed log line and pivot to Tempo trace via TraceId derived field."
