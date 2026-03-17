# Operations Runbook

## Health Check

- Endpoint: `GET /health`
- Success response: `200` with `{"status":"ok", ...}`
- Degraded response: `503` with `{"status":"degraded", ...}`
- Checks currently included:
- `blogStorage` (post files folder exists and post count)
- `firestore` (quick read check, or `development-fallback` in local Development mode)
- `dailyCapsule` (AI generation/persistence status, last failures, last served source/date)

Example:

```powershell
Invoke-RestMethod http://localhost:5207/health
```

If `checks.dailyCapsule.status` is `degraded`, treat it as an alert condition even when the site still serves fallback capsule content.

## Pre-Deploy Checks

Run from repo root:

```powershell
.\run_checks.ps1 -Mode PreDeploy
```

Deployment now runs these checks automatically before image build/push when you use:

```powershell
.\Deployment\deploy.ps1
```

Optional deploy flags:

- `-SkipPreDeployChecks` skips all pre-deploy checks.
- `-SkipBrowserInstall` keeps checks enabled but skips Playwright browser install.
- `-SkipProductionSmokeCheck` skips post-deploy production auth/comment smoke checks.

What it does:

- Runs the existing test suite.
- Installs Playwright Chromium for browser-based checks.
- Runs mobile + desktop E2E checks that exercise comment moderation banner visibility and admin login/logout flows.

If `OPENAI_API_KEY` is not set in your environment, the live moderation test is automatically skipped.

Additional modes:

```powershell
.\run_checks.ps1 -Mode Tests
.\run_checks.ps1 -Mode E2E
.\run_checks.ps1 -Mode PreDeploy -SkipBrowserInstall
```

Batch wrappers are available if you want double-click launch:

- `run_tests.bat` -> runs `run_checks.ps1 -Mode Tests`
- `run_predeploy_checks.bat` -> runs `run_checks.ps1 -Mode PreDeploy`

## Deployment Verification

After deploy, verify that the latest revision is serving:

```powershell
curl.exe -sSI https://markgolley.dev/admin/login
```

Check these headers:

- `X-App-Version` should match the deploy timestamp tag printed by `Deployment/deploy.ps1`.
- `CF-Cache-Status` should be `DYNAMIC`.
- `Cache-Control` should include `no-store`.

### Auth + Comment Smoke Check

This is run automatically at the end of `.\Deployment\deploy.ps1`.
You can still run it manually after each deploy:

```powershell
.\Deployment\verify-production-auth.ps1
```

What it verifies:

- Admin login works on direct Cloud Run URL.
- Admin login works on your public domain.
- Moderated comment flow redirects with `commentStatus=moderated` on both direct and public URLs.
- Admin login may complete via either `302 -> /admin` or a direct `200` dashboard render, both treated as valid.

If direct passes but public fails, the issue is edge/proxy behavior (not app code), usually cookie stripping on `GET /admin`.

Quick signal for cookie stripping on public edge:

- Repeat `GET /blog` twice with the same cookie jar.
- If `myblog.antiforgery.v2` changes on every GET for public edge but stays stable on direct Cloud Run, the edge is not forwarding cookies on GET.

Cloudflare guidance in that case:

- Ensure cache is bypassed for dynamic routes (`/admin*`, `/blog*`, `/subscribe*`, `/health`).
- Ensure no Transform Rule / Worker removes the `Cookie` request header on those routes.
- Ensure no rule strips auth cookies (for this app: `myblog.auth.v1`) before origin fetch.

Convenience wrapper:

```powershell
.\Deployment\verify-production-auth.bat
```

## Backup Routine

Script: `Deployment/backup.ps1`

What it backs up:

- Blog post files from `MyBlog/wwwroot/BlogStorage` into a timestamped local zip
- Firestore export into your GCS bucket with `gcloud firestore export`

Prerequisites:

- Google Cloud CLI (`gcloud`) installed and in `PATH`
- Authenticated account with Firestore export permissions
- A destination GCS bucket URI (for example `gs://my-blog-backups`)

Run from repo root:

```powershell
.\Deployment\backup.ps1 -ProjectId "your-gcp-project-id" -BucketUri "gs://your-backup-bucket"
```

Optional parameters:

- `-DatabaseId "(default)"` (or your Firestore database ID)
- `-OutputRoot "C:\path\to\local\backups"`
- `-PostsPath "C:\path\to\BlogStorage"`

Suggested cadence:

- Daily for Firestore export
- Weekly for local zip retention cleanup, or archive to long-term storage

## Subscriber Notifications

Feature summary:

- Users subscribe via `POST /subscribe`
- Email confirmation via `GET /subscribe/confirm?token=...`
- One-click unsubscribe via `GET /subscribe/unsubscribe?token=...`
- Admin-triggered new-post notify endpoint: `POST /admin/subscribers/notify-post`

Required secrets:

- `ICLOUD_EMAIL`
- `ICLOUD_APP_PASSWORD`
- `SUBSCRIBER_NOTIFY_KEY` (or `Subscriptions:NotifyAdminKey` in config)
- `PUBLIC_BASE_URL` (or `Site:PublicBaseUrl`, for example `https://markgolley.dev`) so email links use your domain root (no `/blog` suffix)

Trigger a new-post email blast:

```powershell
Invoke-RestMethod `
  -Method Post `
  -Uri "https://your-domain/admin/subscribers/notify-post" `
  -Headers @{ "X-Admin-Key" = "your-notify-key" } `
  -Body @{ "PostSlug" = "your_post_slug" }
```

Script helper:

```powershell
$env:SUBSCRIBER_NOTIFY_KEY = "your-notify-key"
.\Deployment\notify-subscribers.ps1 `
  -PostSlug "your_post_slug"
```

Optional:

- Pass `-BaseUrl` only if you need to override the default `https://markgolley.dev`.
- Omit `-PostSlug` to get an interactive numbered list from `MyBlog/wwwroot/BlogStorage`.
- Pass `-BlogStoragePath` if your local posts directory differs from the default.

## Daily Capsule Warm-Up

Purpose:

- Pre-generate and persist today’s capsule shortly after UK midnight so first visitors do not trigger generation.

Endpoint:

- `POST /admin/daily-capsule/warmup`
- Header: `X-Admin-Key`

Required secret:

- `DAILY_CAPSULE_WARMUP_KEY` (or `DailyCapsule:WarmupAdminKey` in config)

Manual trigger:

```powershell
$env:DAILY_CAPSULE_WARMUP_KEY = "your-warmup-key"
.\Deployment\warm-daily-capsule.ps1
```

Suggested scheduler setup:

1. Use Cloud Scheduler (or equivalent) with cron `5 0 * * *` and timezone `Europe/London`.
2. Call `POST https://your-domain/admin/daily-capsule/warmup`.
3. Include header `X-Admin-Key: <warmup-key>`.
4. Alert on non-2xx responses.
