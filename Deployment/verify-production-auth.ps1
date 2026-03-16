[CmdletBinding()]
param(
    [string]$PublicBaseUrl = "https://markgolley.dev",
    [string]$DirectBaseUrl = "",
    [string]$ProjectId = "my-blog-website-470819",
    [string]$Service = "myblog-app",
    [string]$Region = "europe-west2",
    [string]$AdminUsername = "",
    [string]$AdminPassword = "",
    [switch]$SkipCommentCheck
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Invoke-Curl {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    $output = & curl.exe @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "curl failed with exit code $LASTEXITCODE. Args: $($Arguments -join ' ')"
    }

    if ($null -eq $output) {
        return ""
    }

    if ($output -is [System.Array]) {
        return ($output -join "`n")
    }

    return [string]$output
}

function Get-HeaderStatusCode {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Headers
    )

    $matches = [regex]::Matches($Headers, 'HTTP/\S+\s+(\d{3})')
    if ($matches.Count -eq 0) {
        throw "Could not parse HTTP status code from headers."
    }

    return [int]$matches[$matches.Count - 1].Groups[1].Value
}

function Get-HeaderValue {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Headers,
        [Parameter(Mandatory = $true)]
        [string]$HeaderName
    )

    $pattern = "(?im)^$([regex]::Escape($HeaderName)):\s*(.+?)\s*$"
    $match = [regex]::Match($Headers, $pattern)
    if (-not $match.Success) {
        return ""
    }

    return $match.Groups[1].Value.Trim()
}

function Get-AntiForgeryToken {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Html,
        [switch]$AllowMissing
    )

    $match = [regex]::Match($Html, 'name="__RequestVerificationToken"[^>]*value="([^"]+)"')
    if (-not $match.Success) {
        $match = [regex]::Match($Html, 'value="([^"]+)"[^>]*name="__RequestVerificationToken"')
    }

    if (-not $match.Success) {
        if ($AllowMissing) {
            return ""
        }

        throw "Anti-forgery token was not found in HTML."
    }

    return $match.Groups[1].Value
}

function Get-FirstBlogSlug {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Html
    )

    $match = [regex]::Match($Html, 'href="/blog/([^"#?]+)"')
    if (-not $match.Success) {
        throw "Could not find a blog post link on /blog."
    }

    return [Uri]::UnescapeDataString($match.Groups[1].Value)
}

function Get-PendingCommentId {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Html,
        [Parameter(Mandatory = $true)]
        [string]$Author,
        [Parameter(Mandatory = $true)]
        [string]$ContentMarker
    )

    $escapedAuthor = [regex]::Escape($Author)
    $escapedContentMarker = [regex]::Escape($ContentMarker)
    $pattern = "(?is)<li>\s*<strong>\s*$escapedAuthor\s*</strong>.*?$escapedContentMarker.*?name=""commentId""\s+value=""(\d+)"""
    $match = [regex]::Match($Html, $pattern)
    if (-not $match.Success) {
        return ""
    }

    return $match.Groups[1].Value
}

function Resolve-AdminCredential {
    param(
        [Parameter(Mandatory = $false)]
        [string]$Provided,
        [Parameter(Mandatory = $true)]
        [string]$EnvironmentVariableName,
        [Parameter(Mandatory = $true)]
        [string]$SecretName
    )

    if (-not [string]::IsNullOrWhiteSpace($Provided)) {
        return $Provided.Trim()
    }

    $fromEnv = [Environment]::GetEnvironmentVariable($EnvironmentVariableName)
    if (-not [string]::IsNullOrWhiteSpace($fromEnv)) {
        return $fromEnv.Trim()
    }

    $fromSecret = & gcloud secrets versions access latest --secret=$SecretName --project=$ProjectId
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($fromSecret)) {
        throw "Unable to resolve '$EnvironmentVariableName'. Provide -$EnvironmentVariableName or configure env/secret access."
    }

    return $fromSecret.Trim()
}

function Remove-PendingSmokeComment {
    param(
        [Parameter(Mandatory = $true)]
        [string]$BaseUrl,
        [Parameter(Mandatory = $true)]
        [string]$Username,
        [Parameter(Mandatory = $true)]
        [string]$Password,
        [Parameter(Mandatory = $true)]
        [string]$Author,
        [Parameter(Mandatory = $true)]
        [string]$ContentMarker
    )

    $cookieFile = [System.IO.Path]::GetTempFileName()
    $attempts = 0

    try {
        $loginHtml = Invoke-Curl @("-sS", "-c", $cookieFile, "$BaseUrl/admin/login")
        $antiForgeryToken = Get-AntiForgeryToken -Html $loginHtml -AllowMissing

        $formParts = @(
            "username=$([Uri]::EscapeDataString($Username))",
            "password=$([Uri]::EscapeDataString($Password))"
        )

        if (-not [string]::IsNullOrWhiteSpace($antiForgeryToken)) {
            $formParts += "__RequestVerificationToken=$([Uri]::EscapeDataString($antiForgeryToken))"
        }

        $form = [string]::Join("&", $formParts)
        $loginHeaders = Invoke-Curl @(
            "-sS", "-D", "-", "-o", "NUL",
            "-b", $cookieFile, "-c", $cookieFile,
            "-X", "POST", "$BaseUrl/admin/login",
            "-H", "Content-Type: application/x-www-form-urlencoded",
            "--data", $form
        )

        $loginStatus = Get-HeaderStatusCode -Headers $loginHeaders
        if ($loginStatus -ne 200 -and $loginStatus -ne 302) {
            return [PSCustomObject]@{
                Attempted  = $true
                Success    = $false
                CommentId  = ""
                DeleteStatus = 0
                Attempts   = $attempts
                Message    = "Admin login failed for cleanup with status $loginStatus."
            }
        }

        $timeoutAt = [DateTime]::UtcNow.AddSeconds(20)
        while ([DateTime]::UtcNow -lt $timeoutAt) {
            $attempts++
            $adminHtml = Invoke-Curl @("-sS", "-b", $cookieFile, "-c", $cookieFile, "$BaseUrl/admin")
            $commentId = Get-PendingCommentId -Html $adminHtml -Author $Author -ContentMarker $ContentMarker

            if (-not [string]::IsNullOrWhiteSpace($commentId)) {
                $token = Get-AntiForgeryToken -Html $adminHtml
                $deleteForm = "commentId=$([Uri]::EscapeDataString($commentId))&__RequestVerificationToken=$([Uri]::EscapeDataString($token))"
                $deleteHeaders = Invoke-Curl @(
                    "-sS", "-D", "-", "-o", "NUL",
                    "-b", $cookieFile, "-c", $cookieFile,
                    "-X", "POST", "$BaseUrl/admin/delete",
                    "-H", "Content-Type: application/x-www-form-urlencoded",
                    "--data", $deleteForm
                )

                $deleteStatus = Get-HeaderStatusCode -Headers $deleteHeaders
                $cleanupSuccess = ($deleteStatus -eq 200 -or $deleteStatus -eq 302)

                return [PSCustomObject]@{
                    Attempted  = $true
                    Success    = $cleanupSuccess
                    CommentId  = $commentId
                    DeleteStatus = $deleteStatus
                    Attempts   = $attempts
                    Message    = if ($cleanupSuccess) { "Deleted pending smoke comment." } else { "Delete returned status $deleteStatus." }
                }
            }

            Start-Sleep -Milliseconds 750
        }

        return [PSCustomObject]@{
            Attempted  = $true
            Success    = $false
            CommentId  = ""
            DeleteStatus = 0
            Attempts   = $attempts
            Message    = "Pending smoke comment was not found in admin queue for cleanup."
        }
    }
    finally {
        Remove-Item -Path $cookieFile -ErrorAction SilentlyContinue
    }
}

function Test-AdminFlow {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Label,
        [Parameter(Mandatory = $true)]
        [string]$BaseUrl,
        [Parameter(Mandatory = $true)]
        [string]$Username,
        [Parameter(Mandatory = $true)]
        [string]$Password
    )

    $cookieFile = [System.IO.Path]::GetTempFileName()

    try {
        $loginHtml = Invoke-Curl @("-sS", "-c", $cookieFile, "$BaseUrl/admin/login")
        $antiForgeryToken = Get-AntiForgeryToken -Html $loginHtml -AllowMissing

        $formParts = @(
            "username=$([Uri]::EscapeDataString($Username))",
            "password=$([Uri]::EscapeDataString($Password))"
        )

        if (-not [string]::IsNullOrWhiteSpace($antiForgeryToken)) {
            $formParts += "__RequestVerificationToken=$([Uri]::EscapeDataString($antiForgeryToken))"
        }

        $form = [string]::Join("&", $formParts)
        $loginHeaders = Invoke-Curl @(
            "-sS", "-D", "-", "-o", "NUL",
            "-b", $cookieFile, "-c", $cookieFile,
            "-X", "POST", "$BaseUrl/admin/login",
            "-H", "Content-Type: application/x-www-form-urlencoded",
            "--data", $form
        )

        $loginStatus = Get-HeaderStatusCode -Headers $loginHeaders
        $loginLocation = Get-HeaderValue -Headers $loginHeaders -HeaderName "location"

        $adminHeaders = Invoke-Curl @("-sS", "-D", "-", "-o", "NUL", "-b", $cookieFile, "$BaseUrl/admin")
        $adminStatus = Get-HeaderStatusCode -Headers $adminHeaders
        $adminLocation = Get-HeaderValue -Headers $adminHeaders -HeaderName "location"
        $adminBody = Invoke-Curl @("-sS", "-b", $cookieFile, "$BaseUrl/admin")
        $adminBodyContainsPendingComments = $adminBody.IndexOf("Pending Comments", [StringComparison]::OrdinalIgnoreCase) -ge 0

        [PSCustomObject]@{
            Label                          = $Label
            BaseUrl                        = $BaseUrl
            LoginStatus                    = $loginStatus
            LoginLocation                  = $loginLocation
            AdminStatus                    = $adminStatus
            AdminLocation                  = $adminLocation
            AdminBodyContainsPendingComments = $adminBodyContainsPendingComments
            Success                        = (($loginStatus -eq 302 -or $loginStatus -eq 200) -and $adminStatus -eq 200 -and $adminBodyContainsPendingComments)
        }
    }
    finally {
        Remove-Item -Path $cookieFile -ErrorAction SilentlyContinue
    }
}

function Test-CommentModerationFlow {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Label,
        [Parameter(Mandatory = $true)]
        [string]$BaseUrl,
        [Parameter(Mandatory = $true)]
        [string]$Username,
        [Parameter(Mandatory = $true)]
        [string]$Password
    )

    $cookieFile = [System.IO.Path]::GetTempFileName()
    $author = "Smoke Test $([Guid]::NewGuid().ToString('N'))"
    $contentMarker = "smoke-$([Guid]::NewGuid().ToString('N'))"
    $status = 0
    $location = ""
    $submitted = $false
    $cleanup = [PSCustomObject]@{
        Attempted = $false
        Success = $false
        CommentId = ""
        DeleteStatus = 0
        Attempts = 0
        Message = "Cleanup not attempted."
    }

    try {
        $blogHtml = Invoke-Curl @("-sS", "-c", $cookieFile, "$BaseUrl/blog")
        $slug = Get-FirstBlogSlug -Html $blogHtml

        $postHtml = Invoke-Curl @("-sS", "-b", $cookieFile, "-c", $cookieFile, "$BaseUrl/blog/$([Uri]::EscapeDataString($slug))")
        $token = Get-AntiForgeryToken -Html $postHtml

        $form = "PostId=$([Uri]::EscapeDataString($slug))&Author=$([Uri]::EscapeDataString($author))&Content=$([Uri]::EscapeDataString("kill yourself you deserve to die $contentMarker"))&ParentCommentId=&__hp=&__RequestVerificationToken=$([Uri]::EscapeDataString($token))"

        $headers = Invoke-Curl @(
            "-sS", "-D", "-", "-o", "NUL",
            "-b", $cookieFile, "-c", $cookieFile,
            "-X", "POST", "$BaseUrl/blog/addcomment",
            "-H", "Content-Type: application/x-www-form-urlencoded",
            "--data", $form
        )

        $submitted = $true
        $status = Get-HeaderStatusCode -Headers $headers
        $location = Get-HeaderValue -Headers $headers -HeaderName "location"
    }
    finally {
        Remove-Item -Path $cookieFile -ErrorAction SilentlyContinue

        if ($submitted) {
            try {
                $cleanup = Remove-PendingSmokeComment `
                    -BaseUrl $BaseUrl `
                    -Username $Username `
                    -Password $Password `
                    -Author $author `
                    -ContentMarker $contentMarker
            }
            catch {
                $cleanup = [PSCustomObject]@{
                    Attempted  = $true
                    Success    = $false
                    CommentId  = ""
                    DeleteStatus = 0
                    Attempts   = 0
                    Message    = "Cleanup threw: $($_.Exception.Message)"
                }
            }
        }
    }

    $moderationSucceeded = ($status -eq 302 -and $location.IndexOf("commentStatus=moderated", [StringComparison]::OrdinalIgnoreCase) -ge 0)
    $cleanupExpected = $moderationSucceeded

    [PSCustomObject]@{
        Label           = $Label
        BaseUrl         = $BaseUrl
        Status          = $status
        Location        = $location
        Success         = $moderationSucceeded
        CleanupExpected = $cleanupExpected
        CleanupSuccess  = $cleanup.Success
        CleanupMessage  = $cleanup.Message
        CleanupCommentId = $cleanup.CommentId
    }
}

if ([string]::IsNullOrWhiteSpace($DirectBaseUrl)) {
    $resolvedDirectBaseUrl = & gcloud run services describe $Service --region $Region --project $ProjectId --format "value(status.url)"
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($resolvedDirectBaseUrl)) {
        throw "Unable to resolve direct Cloud Run URL. Provide -DirectBaseUrl or check gcloud access."
    }

    $DirectBaseUrl = $resolvedDirectBaseUrl.TrimEnd("/")
}
else {
    $DirectBaseUrl = $DirectBaseUrl.TrimEnd("/")
}

$PublicBaseUrl = $PublicBaseUrl.TrimEnd("/")

$resolvedAdminUsername = Resolve-AdminCredential -Provided $AdminUsername -EnvironmentVariableName "ADMIN_USERNAME" -SecretName "ADMIN_USERNAME"
$resolvedAdminPassword = Resolve-AdminCredential -Provided $AdminPassword -EnvironmentVariableName "ADMIN_PASSWORD" -SecretName "ADMIN_PASSWORD"

Write-Host "Public base URL:  $PublicBaseUrl"
Write-Host "Direct base URL:  $DirectBaseUrl"
Write-Host "Admin username:   $resolvedAdminUsername"
Write-Host ""

$directAdmin = Test-AdminFlow -Label "Direct Cloud Run" -BaseUrl $DirectBaseUrl -Username $resolvedAdminUsername -Password $resolvedAdminPassword
$publicAdmin = Test-AdminFlow -Label "Public Edge" -BaseUrl $PublicBaseUrl -Username $resolvedAdminUsername -Password $resolvedAdminPassword

$directComment = $null
$publicComment = $null
if (-not $SkipCommentCheck) {
    $directComment = Test-CommentModerationFlow `
        -Label "Direct Cloud Run" `
        -BaseUrl $DirectBaseUrl `
        -Username $resolvedAdminUsername `
        -Password $resolvedAdminPassword
    $publicComment = Test-CommentModerationFlow `
        -Label "Public Edge" `
        -BaseUrl $PublicBaseUrl `
        -Username $resolvedAdminUsername `
        -Password $resolvedAdminPassword
}

Write-Host "Admin flow results:"
Write-Host " - $($directAdmin.Label): login=$($directAdmin.LoginStatus), admin=$($directAdmin.AdminStatus), success=$($directAdmin.Success)"
Write-Host " - $($publicAdmin.Label): login=$($publicAdmin.LoginStatus), admin=$($publicAdmin.AdminStatus), success=$($publicAdmin.Success)"

if (-not $SkipCommentCheck) {
    Write-Host "Comment moderation flow results:"
    Write-Host " - $($directComment.Label): status=$($directComment.Status), success=$($directComment.Success), cleanup=$($directComment.CleanupSuccess)"
    Write-Host " - $($publicComment.Label): status=$($publicComment.Status), success=$($publicComment.Success), cleanup=$($publicComment.CleanupSuccess)"
}

if ($directAdmin.Success -and -not $publicAdmin.Success) {
    throw @"
Direct Cloud Run login works but public edge login fails.
This indicates edge/proxy behavior (typically cookie forwarding/stripping on GET /admin), not an app-level auth bug.
Check Cloudflare/Fastly rules for /admin*:
 - Do not strip the Cookie request header.
 - Bypass cache.
 - Disable any worker/transform rule that normalizes or removes auth cookies.
"@
}

if (-not $directAdmin.Success -or -not $publicAdmin.Success) {
    throw "Admin smoke test failed."
}

if (-not $SkipCommentCheck -and (-not $directComment.Success -or -not $publicComment.Success)) {
    throw "Comment moderation smoke test failed."
}

if (-not $SkipCommentCheck -and (($directComment.CleanupExpected -and -not $directComment.CleanupSuccess) -or ($publicComment.CleanupExpected -and -not $publicComment.CleanupSuccess))) {
    throw @"
Comment smoke check submitted moderated comments but cleanup failed.
 - $($directComment.Label): cleanupExpected=$($directComment.CleanupExpected), cleanupSuccess=$($directComment.CleanupSuccess), details=$($directComment.CleanupMessage)
 - $($publicComment.Label): cleanupExpected=$($publicComment.CleanupExpected), cleanupSuccess=$($publicComment.CleanupSuccess), details=$($publicComment.CleanupMessage)
"@
}

Write-Host ""
Write-Host "Production smoke checks passed."
