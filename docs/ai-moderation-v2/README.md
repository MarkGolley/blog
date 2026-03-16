# AI Moderation v2 Milestone 1

Milestone 1 delivers a repeatable moderation benchmark flow and baseline report.

## Run the evaluator

From repository root:

```powershell
dotnet run --project MyBlog.ModerationEval -- --dataset docs/ai-moderation-v2/datasets/baseline-v1.json --label baseline-v1
```

Or use the helper script:

```powershell
./run_checks.ps1 -Mode ModerationEval
```

## Tracked assets

- Ticket tracker: `docs/ai-moderation-v2/TICKETS.md`
- Dataset: `docs/ai-moderation-v2/datasets/baseline-v1.json`
- Baseline report snapshot: `docs/ai-moderation-v2/reports/baseline-v1-20260316-194812.md`

## Generated run artifacts

Each run writes machine-readable output to:

- `artifacts/moderation-eval/reports/<label>-yyyyMMdd-HHmmss/report.json`
- `artifacts/moderation-eval/reports/<label>-yyyyMMdd-HHmmss/report.md`

`artifacts/` is intentionally gitignored, so commit snapshots you want to keep under `docs/ai-moderation-v2/reports/`.
