[CmdletBinding()]
param(
    [int]$Port = 5098,
    [int]$VirtualUsers = 8,
    [int]$WarmRequests = 32
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$baseUrl = "http://127.0.0.1:$Port"
$serverProcess = $null
$taskTempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("aislepilot-load-" + [Guid]::NewGuid().ToString("N"))
$previousEnvironment = @{
    ASPNETCORE_ENVIRONMENT = $env:ASPNETCORE_ENVIRONMENT
    ASPNETCORE_URLS = $env:ASPNETCORE_URLS
    PORT = $env:PORT
    AislePilot__EnableAiGeneration = $env:AislePilot__EnableAiGeneration
    AislePilot__EnableInteractiveAiGeneration = $env:AislePilot__EnableInteractiveAiGeneration
    DailyCapsule__EnableAiGeneration = $env:DailyCapsule__EnableAiGeneration
    DailyCapsule__EnableInteractiveAiGeneration = $env:DailyCapsule__EnableInteractiveAiGeneration
    Firestore__AllowInMemoryFallback = $env:Firestore__AllowInMemoryFallback
    GOOGLE_CLOUD_PROJECT = $env:GOOGLE_CLOUD_PROJECT
}

try {
    New-Item -ItemType Directory -Path $taskTempRoot | Out-Null
    $env:ASPNETCORE_ENVIRONMENT = "Production"
    $env:ASPNETCORE_URLS = $baseUrl
    $env:PORT = $Port.ToString()
    $env:AislePilot__EnableAiGeneration = "false"
    $env:AislePilot__EnableInteractiveAiGeneration = "false"
    $env:DailyCapsule__EnableAiGeneration = "false"
    $env:DailyCapsule__EnableInteractiveAiGeneration = "false"
    $env:Firestore__AllowInMemoryFallback = "true"
    $env:GOOGLE_CLOUD_PROJECT = " "
    $serverProcess = Start-Process -FilePath "dotnet" -WindowStyle Hidden -PassThru -ArgumentList @(
        "run",
        "--project", (Join-Path $repoRoot "MyBlog/MyBlog.csproj"),
        "--configuration", "Release",
        "--no-launch-profile",
        "--urls", $baseUrl
    ) -RedirectStandardOutput (Join-Path $taskTempRoot "server.stdout.log") `
      -RedirectStandardError (Join-Path $taskTempRoot "server.stderr.log")

    $ready = $false
    for ($attempt = 1; $attempt -le 60; $attempt++) {
        if ($serverProcess.HasExited) {
            throw "AislePilot load-test server exited before becoming ready."
        }
        try {
            $response = Invoke-WebRequest -Uri "$baseUrl/projects/aisle-pilot" -Headers @{ "X-Forwarded-Proto" = "https" } -TimeoutSec 2 -UseBasicParsing
            if ($response.StatusCode -eq 200) {
                $ready = $true
                break
            }
        }
        catch {
            Start-Sleep -Milliseconds 500
        }
    }
    if (-not $ready) {
        throw "AislePilot load-test server was not ready within 30 seconds."
    }

    $bundledNode = Join-Path $repoRoot "node_modules/node/bin/node.exe"
    $nodePath = if (Test-Path -LiteralPath $bundledNode) { $bundledNode } else { "node" }
    $env:AISLEPILOT_LOAD_BASE_URL = $baseUrl
    $env:AISLEPILOT_LOAD_VUS = $VirtualUsers.ToString()
    $env:AISLEPILOT_LOAD_WARM_REQUESTS = $WarmRequests.ToString()
    & $nodePath (Join-Path $PSScriptRoot "aislepilot-load-test.mjs")
    if ($LASTEXITCODE -ne 0) {
        throw "AislePilot load thresholds failed with exit code $LASTEXITCODE."
    }
}
finally {
    if ($serverProcess -and -not $serverProcess.HasExited) {
        Stop-Process -Id $serverProcess.Id -Force -ErrorAction SilentlyContinue
        $null = $serverProcess.WaitForExit(5000)
    }
    $env:ASPNETCORE_ENVIRONMENT = $previousEnvironment.ASPNETCORE_ENVIRONMENT
    $env:ASPNETCORE_URLS = $previousEnvironment.ASPNETCORE_URLS
    $env:PORT = $previousEnvironment.PORT
    $env:AislePilot__EnableAiGeneration = $previousEnvironment.AislePilot__EnableAiGeneration
    $env:AislePilot__EnableInteractiveAiGeneration = $previousEnvironment.AislePilot__EnableInteractiveAiGeneration
    $env:DailyCapsule__EnableAiGeneration = $previousEnvironment.DailyCapsule__EnableAiGeneration
    $env:DailyCapsule__EnableInteractiveAiGeneration = $previousEnvironment.DailyCapsule__EnableInteractiveAiGeneration
    $env:Firestore__AllowInMemoryFallback = $previousEnvironment.Firestore__AllowInMemoryFallback
    $env:GOOGLE_CLOUD_PROJECT = $previousEnvironment.GOOGLE_CLOUD_PROJECT

    $resolvedTempRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
    $resolvedTaskRoot = [System.IO.Path]::GetFullPath($taskTempRoot)
    if ($resolvedTaskRoot.StartsWith($resolvedTempRoot, [StringComparison]::OrdinalIgnoreCase) -and
        (Split-Path -Leaf $resolvedTaskRoot).StartsWith("aislepilot-load-")) {
        Remove-Item -LiteralPath $resolvedTaskRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
