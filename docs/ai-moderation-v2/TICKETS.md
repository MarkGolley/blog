# AI Moderation v2 Ticket Set

Status date: 2026-03-16  
Owner: Mark Golley  
Goal: Move moderation from simple pass/fail to an evaluation-driven pipeline with repeatable quality metrics.

## Milestone 1: Benchmark Harness + Baseline Report

### Done
- [x] `MOD-001` Define milestone scope and acceptance criteria.
- [x] `MOD-002` Add benchmark dataset schema and seed dataset (`baseline-v1`).
- [x] `MOD-003` Build command-line moderation evaluator that runs against the dataset and writes machine-readable + recruiter-readable reports.
- [x] `MOD-004` Publish first baseline metrics report with confusion matrix and error breakdown.
- [x] `MOD-005` Add runbook documentation for how to rerun evals and compare results.
- [x] Baseline report snapshot committed at `docs/ai-moderation-v2/reports/baseline-v1-20260316-194812.md`.

### In progress
- [ ] `MOD-006` Add CI-safe smoke check that validates dataset schema and report generation path.

### Next
- [ ] `MOD-013` Expand dataset coverage for borderline policy cases to reduce benchmark overfitting risk.
- [ ] `MOD-014` Add second baseline run with `--delay-ms` and larger sample count for latency stability checks.

## Milestone 2: Reliability + Fallback Behavior
- [ ] `MOD-007` Add timeout and retry policy for moderation HTTP calls.
- [ ] `MOD-008` Add deterministic fallback classification state (`NeedsManualReview`) with explicit reason codes.
- [ ] `MOD-009` Add telemetry events for moderation latency, failures, and fallback usage.

## Milestone 3: Quality Improvements + Controls
- [ ] `MOD-010` Add category-level metrics (harassment, violence, sexual, self-harm, etc.) where available.
- [ ] `MOD-011` Tune thresholding and policy rules using benchmark feedback.
- [ ] `MOD-012` Add regression dataset for known difficult edge cases.

## Definition of Done for Milestone 1
- Eval command can run locally with one command.
- Dataset is versioned in source control.
- Report contains: accuracy, precision/recall/F1 for unsafe detection, confusion matrix, latency summary, and misclassification list.
- Baseline report snapshot is committed under `docs/ai-moderation-v2/reports/`.
