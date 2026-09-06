$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

. (Join-Path (Split-Path -Parent $PSScriptRoot) "app-version-probe.ps1")

function Assert-Equal {
    param([object]$Expected, [object]$Actual, [string]$Message)
    if ($Expected -ne $Actual) {
        throw "$Message Expected '$Expected', received '$Actual'."
    }
}

$attempt = 0
$eventualHeaders = {
    param([string]$Url)
    $script:attempt++
    if ($script:attempt -eq 1) {
        return @("HTTP/1.1 503 Service Unavailable", "server: Google Frontend")
    }

    return @("HTTP/1.1 200 OK", "x-app-version: production-test")
}

$version = Get-AppVersionFromUrl -Url "https://example.test" -MaxAttempts 2 -RetryDelaySeconds 0 -HeaderProvider $eventualHeaders
Assert-Equal "production-test" $version "The probe should retry a response without the version header."
Assert-Equal 2 $attempt "The probe should stop after the first successful retry."

$redirectHeaders = {
    param([string]$Url)
    return @(
        "HTTP/1.1 302 Found",
        "x-app-version: old-version",
        "HTTP/1.1 200 OK",
        "X-App-Version: current-version"
    )
}
Assert-Equal "current-version" (Get-AppVersionFromUrl -Url "https://example.test" -MaxAttempts 1 -RetryDelaySeconds 0 -HeaderProvider $redirectHeaders) "The final redirect response version should win."

$failureMessage = ""
try {
    Get-AppVersionFromUrl -Url "https://example.test" -MaxAttempts 2 -RetryDelaySeconds 0 -HeaderProvider { @("HTTP/1.1 503 Service Unavailable") }
}
catch {
    $failureMessage = $_.Exception.Message
}
if ($failureMessage -notmatch "after 2 attempt\(s\)") {
    throw "The exhausted retry error should include its attempt count. Actual: '$failureMessage'"
}

Write-Host "App-version probe tests passed."
