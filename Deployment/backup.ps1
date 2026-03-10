param(
    [Parameter(Mandatory = $true)]
    [string]$ProjectId,

    [Parameter(Mandatory = $true)]
    [string]$BucketUri,

    [string]$DatabaseId = "(default)",
    [string]$OutputRoot,
    [string]$PostsPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $PSCommandPath
$repoRoot = (Resolve-Path (Join-Path $scriptDir "..")).Path

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $scriptDir "backups"
}

if ([string]::IsNullOrWhiteSpace($PostsPath)) {
    $PostsPath = Join-Path $repoRoot "MyBlog\wwwroot\BlogStorage"
}

if (-not $BucketUri.StartsWith("gs://")) {
    throw "BucketUri must start with gs://"
}

$gcloud = Get-Command gcloud -ErrorAction SilentlyContinue
if ($null -eq $gcloud) {
    throw "gcloud CLI was not found in PATH. Install Google Cloud CLI and authenticate first."
}

$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$backupDir = Join-Path $OutputRoot $timestamp
New-Item -Path $backupDir -ItemType Directory -Force | Out-Null

if (-not (Test-Path $PostsPath)) {
    throw "Posts path not found: $PostsPath"
}

$postsArchive = Join-Path $backupDir "blog-posts-$timestamp.zip"
Compress-Archive -Path (Join-Path $PostsPath "*") -DestinationPath $postsArchive -Force

$firestoreExportPath = "$BucketUri/firestore-backups/$timestamp"
Write-Host "Exporting Firestore to $firestoreExportPath ..."
& $gcloud.Path firestore export $firestoreExportPath `
  --project=$ProjectId `
  --database=$DatabaseId `
  --quiet

Write-Host ""
Write-Host "Backup complete."
Write-Host "Local posts archive: $postsArchive"
Write-Host "Firestore export path: $firestoreExportPath"
