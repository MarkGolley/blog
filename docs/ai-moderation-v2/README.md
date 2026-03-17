# AI Moderation v2 Diagnostics

This folder tracks a lightweight moderation diagnostics loop for a low-volume comment system.

## Run the evaluator

From repository root:

```powershell
dotnet run --project MyBlog.ModerationEval -- --dataset docs/ai-moderation-v2/datasets/smoke-v1.json --label smoke-v1
```

Or use the helper script:

```powershell
./run_checks.ps1 -Mode ModerationEval
```

## Tracked assets

- Ticket tracker: `docs/ai-moderation-v2/TICKETS.md`
- Smoke dataset: `docs/ai-moderation-v2/datasets/smoke-v1.json`
- Snapshot report example: `docs/ai-moderation-v2/reports/baseline-v1-20260316-194812.md`

## Generated run artifacts

Each run writes machine-readable output to:

- `artifacts/moderation-eval/reports/<label>-yyyyMMdd-HHmmss/report.json`
- `artifacts/moderation-eval/reports/<label>-yyyyMMdd-HHmmss/report.md`

`artifacts/` is intentionally gitignored, so commit snapshots you want to keep under `docs/ai-moderation-v2/reports/`.
