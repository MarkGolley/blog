[CmdletBinding()]
param(
    [string]$RepoRoot = (Split-Path -Parent $PSScriptRoot),
    [int]$MaxLines = 1400,
    [string]$AllowListPath = ""
)

$ErrorActionPreference = "Stop"

function Get-RelativePath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$BasePath,
        [Parameter(Mandatory = $true)]
        [string]$TargetPath
    )

    $resolvedBase = [System.IO.Path]::GetFullPath($BasePath).TrimEnd('\', '/')
    $resolvedTarget = [System.IO.Path]::GetFullPath($TargetPath)
    $basePrefix = "$resolvedBase\"

    if ($resolvedTarget.StartsWith($basePrefix, [StringComparison]::OrdinalIgnoreCase)) {
        return $resolvedTarget.Substring($basePrefix.Length)
    }

    return $resolvedTarget
}

function Is-ExcludedPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RelativePath
    )

    $normalized = $RelativePath.Replace('/', '\')
    return $normalized -match "(^|\\)(\.git|\.idea|\.artifacts|artifacts|bin|obj|\.playwright|wwwroot\\lib)(\\|$)"
}

function Load-AllowPatterns {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        return @()
    }

    return Get-Content -LiteralPath $Path |
        ForEach-Object { $_.Trim() } |
        Where-Object { $_ -and -not $_.StartsWith("#") }
}

function Is-AllowListed {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RelativePath,
        [string[]]$Patterns = @()
    )

    if (-not $Patterns) {
        return $false
    }

    $normalizedPath = $RelativePath.Replace('/', '\').Trim()
    foreach ($pattern in $Patterns) {
        $normalizedPattern = $pattern.Replace('/', '\').Trim()
        if ($normalizedPath -like $normalizedPattern) {
            return $true
        }
    }

    return $false
}

$extensions = @(".cs", ".cshtml", ".ps1")
if ([string]::IsNullOrWhiteSpace($AllowListPath)) {
    $AllowListPath = Join-Path -Path $PSScriptRoot -ChildPath "oversized-files-allowlist.txt"
}
$allowPatterns = Load-AllowPatterns -Path $AllowListPath
if ($null -eq $allowPatterns) {
    $allowPatterns = @()
}
$oversizedFiles = New-Object System.Collections.Generic.List[object]

$files = Get-ChildItem -Path $RepoRoot -Recurse -File -ErrorAction SilentlyContinue
foreach ($file in $files) {
    if (-not (Test-Path -LiteralPath $file.FullName)) {
        continue
    }

    $relativePath = Get-RelativePath -BasePath $RepoRoot -TargetPath $file.FullName
    if (Is-ExcludedPath -RelativePath $relativePath) {
        continue
    }

    if ($extensions -notcontains $file.Extension) {
        continue
    }

    $lineCount = [System.IO.File]::ReadAllLines($file.FullName).Length
    if ($lineCount -le $MaxLines) {
        continue
    }

    if (Is-AllowListed -RelativePath $relativePath -Patterns $allowPatterns) {
        continue
    }

    $oversizedFiles.Add([pscustomobject]@{
        Path = $relativePath
        Lines = $lineCount
        Limit = $MaxLines
    })
}

if ($oversizedFiles.Count -gt 0) {
    Write-Host "Oversized source files found (not allowlisted):"
    $oversizedFiles |
        Sort-Object -Property Lines -Descending |
        Format-Table -AutoSize
    exit 1
}

Write-Host "Source file size check passed. No non-allowlisted files exceed $MaxLines lines."
