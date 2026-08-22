[CmdletBinding()]
param([switch]$SkipInstall)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repoRoot
$browserProcess = $null
$browserProfile = $null
try {
    if (-not $SkipInstall) {
        npm ci
        if ($LASTEXITCODE -ne 0) { throw "npm ci failed with exit code $LASTEXITCODE." }
    }
    $playwrightChrome = Get-ChildItem -LiteralPath "$env:LOCALAPPDATA\ms-playwright" `
        -Filter "chrome.exe" -Recurse -ErrorAction SilentlyContinue |
        Select-Object -First 1 -ExpandProperty FullName
    $chromeCandidates = @(
        "$env:ProgramFiles\Google\Chrome\Application\chrome.exe",
        "${env:ProgramFiles(x86)}\Google\Chrome\Application\chrome.exe",
        "$env:LOCALAPPDATA\Google\Chrome\Application\chrome.exe",
        $playwrightChrome,
        "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe",
        "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe"
    )
    $chromePath = $chromeCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if (-not $chromePath) { throw "Chrome or Edge is required for Lighthouse CI." }
    $env:CHROME_PATH = $chromePath
    $env:ASPNETCORE_ENVIRONMENT = "Development"
    $env:AislePilot__EnableAiGeneration = "false"
    $env:PORT = "5087"
    $browserProfile = Join-Path ([System.IO.Path]::GetTempPath()) ("aislepilot-lighthouse-" + [Guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $browserProfile | Out-Null
    $browserProcess = Start-Process -FilePath $chromePath -WindowStyle Hidden -PassThru -ArgumentList @(
        "--headless=new",
        "--remote-debugging-port=9222",
        "--no-first-run",
        "--user-data-dir=$browserProfile",
        "about:blank"
    )
    npm run lighthouse:mobile
    if ($LASTEXITCODE -ne 0) { throw "Mobile Lighthouse CI failed with exit code $LASTEXITCODE." }
    npm run lighthouse:desktop
    if ($LASTEXITCODE -ne 0) { throw "Desktop Lighthouse CI failed with exit code $LASTEXITCODE." }
    & "$PSScriptRoot\check-lighthouse-third-parties.ps1"
}
finally {
    if ($browserProcess -and -not $browserProcess.HasExited) {
        Stop-Process -Id $browserProcess.Id -Force -ErrorAction SilentlyContinue
        $browserProcess.WaitForExit(5000)
    }
    if ($browserProfile) {
        $resolvedProfile = [System.IO.Path]::GetFullPath($browserProfile)
        $resolvedTemp = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
        if ($resolvedProfile.StartsWith($resolvedTemp, [StringComparison]::OrdinalIgnoreCase) -and
            (Split-Path -Leaf $resolvedProfile).StartsWith("aislepilot-lighthouse-")) {
            Remove-Item -LiteralPath $resolvedProfile -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
    Pop-Location
}
