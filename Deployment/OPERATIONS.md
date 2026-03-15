# Operations Runbook

## Health Check

- Endpoint: `GET /health`
- Success response: `200` with `{"status":"ok", ...}`
- Degraded response: `503` with `{"status":"degraded", ...}`
- Checks currently included:
- `blogStorage` (post files folder exists and post count)
- `firestore` (quick read check, or `development-fallback` in local Development mode)

Example:

```powershell
Invoke-RestMethod http://localhost:5207/health
```

## Pre-Deploy Checks

Run from repo root:

```powershell
.\run_predeploy_checks.bat
```

What it does:

- Runs the existing test suite.
- Installs Playwright Chromium for browser-based checks.
- Runs mobile + desktop E2E checks that exercise comment moderation banner visibility and admin login/logout flows.

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
