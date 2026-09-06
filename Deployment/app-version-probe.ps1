function Get-AppVersionFromUrl {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Url,
        [ValidateRange(1, 30)]
        [int]$MaxAttempts = 12,
        [ValidateRange(0, 60)]
        [int]$RetryDelaySeconds = 5,
        [scriptblock]$HeaderProvider
    )

    if ($null -eq $HeaderProvider) {
        $HeaderProvider = {
            param([string]$TargetUrl)
            $responseHeaders = & curl.exe -sS -D - -o NUL -L --connect-timeout 10 --max-time 30 $TargetUrl
            if ($LASTEXITCODE -ne 0) {
                throw "Header request failed with exit code $LASTEXITCODE."
            }

            return $responseHeaders
        }
    }

    $lastFailure = "x-app-version header was not present."
    for ($attempt = 1; $attempt -le $MaxAttempts; $attempt++) {
        try {
            $headers = & $HeaderProvider $Url
            $versions = @(
                foreach ($line in $headers) {
                    if ($line -match '^[xX]-app-version:\s*(.+)\s*$') {
                        $Matches[1].Trim()
                    }
                }
            )

            if ($versions.Count -gt 0) {
                return $versions[-1]
            }

            $lastFailure = "x-app-version header was not present."
        }
        catch {
            $lastFailure = $_.Exception.Message
        }

        if ($attempt -lt $MaxAttempts) {
            Write-Warning "Version probe attempt $attempt/$MaxAttempts failed for '$Url': $lastFailure Retrying in $RetryDelaySeconds second(s)."
            if ($RetryDelaySeconds -gt 0) {
                Start-Sleep -Seconds $RetryDelaySeconds
            }
        }
    }

    throw "x-app-version header was not found for '$Url' after $MaxAttempts attempt(s). Last failure: $lastFailure"
}
