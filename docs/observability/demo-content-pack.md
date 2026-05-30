# Demo Content Pack

## Technical Blog Post Outline

Working title:

`From Side Project to Operable System: OpenTelemetry + LGTM for a .NET AI App`

Suggested sections:

1. Problem framing: feature-complete is not operationally complete
2. Instrumentation decisions and semantic conventions
3. Correlated logs/traces/metrics design
4. AI observability: latency, tokens, and cost estimation
5. Pipeline architecture with OTel Collector
6. Failure simulation and diagnosis walkthrough
7. Tradeoffs and next improvements

Key artifacts to embed:

- telemetry architecture diagram
- Grafana operational dashboard screenshot
- Grafana AI/cost dashboard screenshot
- one sample trace showing failure span
- one sample log line correlated by trace ID

## LinkedIn / Short Demo Video Script

Length target: 2-4 minutes.

1. Opening (15s): "I turned my personal app into an operational maturity demo."
2. Architecture (30s): app -> OTel Collector -> Prometheus/Loki/Tempo -> Grafana.
3. Incident trigger (20s): run warmup scenario with intentional AI dependency failure.
4. Diagnose (60s): move from error-rate spike -> logs -> trace -> failing span.
5. AI observability (40s): show tokens/cost/failure-rate panels and queue/job impact.
6. Close (15s): summarize why this reflects production engineering capability.

## GitHub README Additions (Suggested)

Add a dedicated section:

- "Operational Maturity Demo"
- quick-start commands for local stack
- screenshots
- one-page troubleshooting workflow
- link to `docs/observability/*`

## Interview Talking Points

- Why OTel Collector was used as control-plane for telemetry routing.
- Why structured logs include hashed user/session fields (privacy-aware diagnosability).
- How to reason about AI reliability beyond raw HTTP success.
- How to balance cardinality vs diagnostic value.
- What would change for Cloud Run production rollout (authn, retention, SLOs, alerting).
