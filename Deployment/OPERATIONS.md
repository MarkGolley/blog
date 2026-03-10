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
