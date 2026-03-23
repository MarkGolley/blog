[CmdletBinding()]
param(
    [ValidateSet("Tests", "E2E", "PreDeploy", "ModerationEval")]
    [string]$Mode = "PreDeploy",
    [switch]$SkipBrowserInstall,
    [switch]$UseChangeBasedSkipping,
    [string]$ModerationDataset = "docs/ai-moderation-v2/datasets/smoke-v1.json",
    [string]$ModerationLabel = "smoke-v1"
)

$ErrorActionPreference = "Stop"

function Invoke-Step {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,
        [Parameter(Mandatory = $true)]
        [scriptblock]$Action
    )

    Write-Host ""
    Write-Host "==> $Name"
    & $Action
    if ($LASTEXITCODE -ne 0) {
        throw "Step failed: $Name"
    }
}

function Test-PlaywrightChromiumInstalled {
    $candidateRoots = @()

    if (-not [string]::IsNullOrWhiteSpace($env:PLAYWRIGHT_BROWSERS_PATH) -and $env:PLAYWRIGHT_BROWSERS_PATH -ne "0") {
        $candidateRoots += $env:PLAYWRIGHT_BROWSERS_PATH
    }

    if (-not [string]::IsNullOrWhiteSpace($env:LOCALAPPDATA)) {
        $candidateRoots += (Join-Path $env:LOCALAPPDATA "ms-playwright")
    }

    foreach ($root in ($candidateRoots | Select-Object -Unique)) {
        if ([string]::IsNullOrWhiteSpace($root) -or -not (Test-Path $root)) {
            continue
        }

        $chromiumDir = Get-ChildItem -Path $root -Directory -Filter "chromium-*" -ErrorAction SilentlyContinue |
            Select-Object -First 1
        if ($null -ne $chromiumDir) {
            return $true
        }
    }

    return $false
}

function Get-ChangedPaths {
    $gitCommand = Get-Command git -ErrorAction SilentlyContinue
    if ($null -eq $gitCommand) {
        return @()
    }

    $previousNativeErrorPreference = $null
    $hasNativeErrorPreference = Test-Path Variable:PSNativeCommandUseErrorActionPreference
    if ($hasNativeErrorPreference) {
        $previousNativeErrorPreference = $PSNativeCommandUseErrorActionPreference
        $PSNativeCommandUseErrorActionPreference = $false
    }

    try {
        & git -c core.safecrlf=false rev-parse --is-inside-work-tree *> $null
        if ($LASTEXITCODE -ne 0) {
            return @()
        }

        $paths = New-Object System.Collections.Generic.List[string]

        $appendPaths = {
            param([string[]]$values)
            foreach ($value in $values) {
                if ([string]::IsNullOrWhiteSpace($value)) {
                    continue
                }

                $trimmed = $value.Trim()
                if ($trimmed.Length -gt 0) {
                    [void]$paths.Add($trimmed)
                }
            }
        }

        & $appendPaths (& git -c core.safecrlf=false diff --name-only --cached 2>$null)
        & $appendPaths (& git -c core.safecrlf=false diff --name-only 2>$null)

        if ($paths.Count -eq 0) {
            & git -c core.safecrlf=false rev-parse --verify HEAD~1 *> $null
            if ($LASTEXITCODE -eq 0) {
                & $appendPaths (& git -c core.safecrlf=false diff --name-only HEAD~1..HEAD 2>$null)
            }
        }

        return $paths |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
            Sort-Object -Unique
    }
    finally {
        if ($hasNativeErrorPreference) {
            $PSNativeCommandUseErrorActionPreference = $previousNativeErrorPreference
        }
    }
}

function Test-OnlyDocsOrOpsChanges {
    param([string[]]$Paths)

    if ($null -eq $Paths -or $Paths.Count -eq 0) {
        return $false
    }

    foreach ($path in $Paths) {
        $normalized = $path.Replace('\', '/').Trim()
        if ([string]::IsNullOrWhiteSpace($normalized)) {
            continue
        }

        if ($normalized.StartsWith("docs/", [System.StringComparison]::OrdinalIgnoreCase)) {
            continue
        }

        if ($normalized.StartsWith("Deployment/", [System.StringComparison]::OrdinalIgnoreCase)) {
            continue
        }

        if ($normalized.StartsWith(".github/", [System.StringComparison]::OrdinalIgnoreCase)) {
            continue
        }

        if ($normalized.EndsWith(".md", [System.StringComparison]::OrdinalIgnoreCase)) {
            continue
        }

        if ($normalized.EndsWith(".txt", [System.StringComparison]::OrdinalIgnoreCase)) {
            continue
        }

        return $false
    }

    return $true
}

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Push-Location $root

try {
    $testProject = "MyBlog.Tests\MyBlog.Tests.csproj"
    $hasOpenAiKey = -not [string]::IsNullOrWhiteSpace($env:OPENAI_API_KEY)
    $skipCodeChecks = $false

    if ($UseChangeBasedSkipping -and ($Mode -eq "Tests" -or $Mode -eq "E2E" -or $Mode -eq "PreDeploy")) {
        $changedPaths = Get-ChangedPaths
        if (Test-OnlyDocsOrOpsChanges -Paths $changedPaths) {
            $skipCodeChecks = $true
            Write-Host "Change-based skipping is active. Only docs/ops files changed, so test and E2E checks are skipped."
        }
        elseif ($changedPaths.Count -gt 0) {
            Write-Host "Change-based skipping is active. App/code changes detected, running checks."
        }
        else {
            Write-Host "Change-based skipping is active, but changed files could not be resolved. Running checks."
        }
    }

    if (($Mode -eq "Tests" -or $Mode -eq "PreDeploy") -and -not $skipCodeChecks) {
        $testArgs = @("test", $testProject)
        if (-not $hasOpenAiKey) {
            Write-Host "OPENAI_API_KEY is not set. Skipping live moderation test (AIModerationServiceLiveTests)."
            $testArgs += @("--filter", "FullyQualifiedName!~AIModerationServiceLiveTests")
        } else {
            Write-Host "OPENAI_API_KEY detected. Live moderation test is enabled."
        }

        Invoke-Step -Name "Running automated tests" -Action { dotnet @testArgs }
    }

    if (($Mode -eq "E2E" -or $Mode -eq "PreDeploy") -and -not $skipCodeChecks) {
        Invoke-Step -Name "Building test project for Playwright tooling" -Action {
            dotnet build $testProject
        }

        if (-not $SkipBrowserInstall) {
            if (Test-PlaywrightChromiumInstalled) {
                Write-Host "Playwright Chromium is already installed. Skipping browser install."
            } else {
                Invoke-Step -Name "Installing Playwright Chromium browser" -Action {
                    powershell -NoProfile -ExecutionPolicy Bypass -File ".\MyBlog.Tests\bin\Debug\net10.0\playwright.ps1" install chromium
                }
            }
        } else {
            Write-Host "Skipping Playwright browser install because -SkipBrowserInstall was provided."
        }

        Invoke-Step -Name "Running browser E2E checks (desktop + mobile)" -Action {
            $env:RUN_PLAYWRIGHT_E2E = "1"
            dotnet test $testProject --no-build --filter "Category=E2E"
        }
    }

    if ($Mode -eq "ModerationEval") {
        Invoke-Step -Name "Running AI moderation smoke diagnostics" -Action {
            dotnet run --project "MyBlog.ModerationEval" -- --dataset $ModerationDataset --label $ModerationLabel
        }
    }

    Write-Host ""
    Write-Host "All requested checks passed."
}
finally {
    Pop-Location
}
