# AI Moderation v2 Ticket Set

Status date: 2026-03-16  
Owner: Mark Golley  
Goal: Keep moderation behavior explicit and testable with low operational overhead.

## Milestone 1: Decision Model + Smoke Diagnostics

### Done
- [x] `MOD-001` Define milestone scope and acceptance criteria.
- [x] `MOD-002` Add versioned smoke dataset (`smoke-v1`) for quick local checks.
- [x] `MOD-003` Build command-line moderation evaluator that writes machine-readable and markdown reports.
- [x] `MOD-004` Add explicit moderation decisions (`Allow`, `Block`, `ManualReview`) with reason codes.
- [x] `MOD-005` Add runbook docs for local reruns and report capture.

### In progress
- [ ] `MOD-006` Add CI smoke run that validates evaluator execution and report generation path.

### Next
- [ ] `MOD-013` Attach reason-code counts to evaluator summary output.
- [ ] `MOD-014` Capture real anonymized examples once enough comments exist.

## Milestone 2: Reliability + Fallback Behavior
- [ ] `MOD-007` Add timeout and retry policy for moderation HTTP calls.
- [ ] `MOD-008` Expand deterministic fallback classification behavior (`ManualReview`) with explicit reason codes.
- [ ] `MOD-009` Add telemetry events for moderation latency, failures, and fallback usage.

## Milestone 3: Quality Improvements + Controls
- [ ] `MOD-010` Add category-level metrics (harassment, violence, sexual, self-harm, etc.) where available.
- [ ] `MOD-011` Tune thresholding and policy rules using benchmark feedback.
- [ ] `MOD-012` Add regression dataset for known difficult edge cases.

## Definition of Done for Milestone 1
- Eval command can run locally with one command.
- Smoke dataset is versioned in source control.
- Report contains: accuracy, precision/recall/F1 for unsafe detection, confusion matrix, latency summary, and misclassification list.
- Moderation service returns explicit decision + reason code for each evaluation.
