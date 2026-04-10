[CmdletBinding()]
param(
    [ValidateSet("Tests", "E2E", "PreDeploy", "ModerationEval")]
    [string]$Mode = "PreDeploy",
    [switch]$SkipBrowserInstall,
    [switch]$FullPreDeployE2E,
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

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Push-Location $root

try {
    $testProject = "MyBlog.Tests\MyBlog.Tests.csproj"
    $hasOpenAiKey = -not [string]::IsNullOrWhiteSpace($env:OPENAI_API_KEY)

    Invoke-Step -Name "Checking source file sizes" -Action {
        powershell -NoProfile -ExecutionPolicy Bypass -File ".\scripts\check-oversized-files.ps1" `
            -RepoRoot $root `
            -AllowListPath ".\scripts\oversized-files-allowlist.txt"
    }

    if ($Mode -eq "Tests" -or $Mode -eq "PreDeploy") {
        $testArgs = @("test", $testProject)
        if (-not $hasOpenAiKey) {
            Write-Host "OPENAI_API_KEY is not set. Skipping live moderation test (AIModerationServiceLiveTests)."
            $testArgs += @("--filter", "FullyQualifiedName!~AIModerationServiceLiveTests")
        } else {
            Write-Host "OPENAI_API_KEY detected. Live moderation test is enabled."
        }

        Invoke-Step -Name "Running automated tests" -Action { dotnet @testArgs }
    }

    if ($Mode -eq "E2E" -or $Mode -eq "PreDeploy") {
        Invoke-Step -Name "Building test project for Playwright tooling" -Action {
            dotnet build $testProject
        }

        if (-not $SkipBrowserInstall) {
            Invoke-Step -Name "Installing Playwright Chromium browser" -Action {
                powershell -NoProfile -ExecutionPolicy Bypass -File ".\MyBlog.Tests\bin\Debug\net10.0\playwright.ps1" install chromium
            }
        } else {
            Write-Host "Skipping Playwright browser install because -SkipBrowserInstall was provided."
        }

        Invoke-Step -Name "Running browser E2E checks (desktop + mobile)" -Action {
            $env:RUN_PLAYWRIGHT_E2E = "1"
            $e2eFilter = "Category=E2E"
            if ($Mode -eq "PreDeploy" -and -not $FullPreDeployE2E) {
                $e2eFilter = "Category=E2ESmoke"
                Write-Host "Running Playwright smoke subset for pre-deploy checks. Use -FullPreDeployE2E for the full E2E suite."
            }

            dotnet test $testProject --filter $e2eFilter
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
