# Debugging Walkthrough

## Scenario

This workflow intentionally creates a diagnosable incident:

1. a warmup/admin API call triggers AI-backed work
2. upstream AI call fails (default local stack uses invalid API key)
3. failures are visible in traces, logs, and metrics
4. impact is quantifiable (error rate, latency, cost/tokens, job outcomes)

## Setup

1. Start the local stack:

```powershell
docker compose -f .\Deployment\observability\docker-compose.yml up --build -d
```

2. Run the scenario script:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Deployment\observability\scripts\run-diagnostic-scenario.ps1
```

3. Open the dashboard directly (no login required in demo mode):

`http://localhost:3000/d/myblog-ops-overview/myblog-operational-overview?orgId=1`

## Investigative Path

### 1) Confirm incident at system level

In Grafana dashboard **MyBlog Operational Overview**:

- check `Error Rate (%)`
- check `P95 Latency (ms)`
- identify spikes in `5xx Rate By Endpoint`
- check `Comment Moderation Outcomes (15m)` for safety-flow impact
- check `Auth Events (15m)` for admin/auth path anomalies
- check `AislePilot Cache Hit Ratio (15m)` for cache effectiveness shifts
- check `Background Job P95 Duration (s)` for async work slowdown
- check `SLO Success (30d, target 99.5%)` and `Error Budget Remaining (30d %)` for current reliability posture
- check `Error Budget Burn Rate (5m)` to classify incident urgency

### 2) Identify failing operation

In `Recent Failures` logs panel:

- filter by recent timestamps
- inspect log lines containing `ExceptionType=...` and AI failure messages
- capture `TraceId` and `CorrelationId`

### 3) Pivot logs -> trace

- click derived `TraceId` link
- open trace in Tempo
- inspect failing span tags:
  - `ai.operation`
  - `ai.model`
  - `ai.error_type`
  - HTTP status code metadata

### 4) Quantify AI impact

In **MyBlog AI / LLM Observability**:

- `AI Failure Rate (%)`
- `AI P95 Duration (s)`
- `AI Token Throughput`
- `AI Cost Last Hour (USD)`

### 5) Validate downstream effects

- `Background Job Failures (15m)` in operations dashboard
- `AislePilot Queue Depth` in AI dashboard
- `AislePilot User Journey Funnel (30m)` to estimate user-facing impact across the flow

## Expected Outcome For Interview Narrative

You can demonstrate:

- traceable request-to-provider failure chain
- deterministic correlation between logs and traces
- measurable blast radius in service metrics
- AI observability with cost/token awareness
- maintainable runbook-driven diagnostics rather than ad-hoc debugging
