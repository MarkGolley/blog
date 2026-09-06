# AislePilot Performance Baseline

Baseline date: 22 August 2026  
Branch state: includes the fast interactive plan-generation work  
Status: instrumentation and local Lighthouse baseline established; real-user percentiles pending deployment traffic

## Asset baseline

| Asset group | Current bytes | Transitional CI budget | Final target |
|---|---:|---:|---:|
| AislePilot CSS | 342,181 | 350,000 | 150,000 |
| AislePilot JavaScript | 409,907 | 410,000 | 200,000 |
| Largest JavaScript file | 238,345 | 245,000 | 80,000 |

The transitional budgets prevent further growth. Ratchet them downward whenever an asset-reduction change lands. Do not raise a budget without documenting why the increase is necessary and what follow-up will remove it.

## Browser measurements

The AislePilot page reports only allowlisted numeric measurements and bounded categorical tags. It does not send URLs, pantry contents, allergens, dislikes, meal names, or other free-form user input.

| Metric | Meaning | Target |
|---|---|---:|
| `ttfb` | Navigation response start | Establish from production traffic |
| `lcp` | Largest Contentful Paint | < 2,500 ms |
| `inp` | Interaction to Next Paint | < 200 ms |
| `cls` | Cumulative Layout Shift | < 0.1 |
| `page_usable` | Navigation start until instrumentation runs after the page scripts load | < 1,500 ms |
| `setup_to_submit` | First setup change until submission | Observe; use for UX comparison |
| `submit_to_plan_visible` | Plan submission until results document is visible | Warm < 2,000 ms; cold < 5,000 ms |

Journey counters record `setup_started`, `setup_submitted`, and `setup_abandoned`. Client reliability counters record `client_error` and `unhandled_rejection`. Error payloads deliberately exclude messages, stack traces, URLs, form values, and other free-form data.

Allowed dimensions are navigation type and whether the page contains a plan result. Avoid high-cardinality tags.

## Server measurements

Already present:

- ASP.NET request duration and status.
- outbound HTTP duration.
- AI request duration, outcome, model, tokens, and estimated cost.
- background job duration and outcome.
- plan-pool replenishment outcomes by bounded dietary complexity, meal-slot count, plan-length band, quick-meal preference, and special-treat flag.
- cache hit and miss counters.
- per-cache refresh success/failure counts and seconds since the last success or failure for AI meals, desserts, supermarket layouts, and meal images.
- queue depth gauges.
- managed-queue accepted, rejected, dequeued, retry-scheduled, completed, cancelled, and faulted event counts plus per-job queue-wait duration.
- total interactive plan duration and context duration in structured logs.
- plan source in structured logs.

Now emitted as `myblog.aislepilot.plan.stage.duration`:

- context resolution;
- pool lookup;
- template selection and assembly;
- AI generation and validation;
- plan assembly;
- dessert resolution;
- server image resolution.

Client polling, action latency, plan-source, percentile dashboard, and setup-abandonment measurements are now instrumented. Generation abandonment still needs an aggregate production definition that does not misclassify successful form navigation.

## Lighthouse baseline

Captured locally on 22 August 2026 using pinned Lighthouse CI, Chromium, simulated mobile throttling, and the development template-plan configuration.

| Profile | Performance | Accessibility | FCP | LCP | CLS | TBT |
|---|---:|---:|---:|---:|---:|---:|
| Mobile, 375 x 812 | 0.63 | 1.00 | 4,682 ms | 7,837 ms | 0 | 98 ms |
| Desktop | 0.94 | 1.00 | 946 ms | 1,421 ms | 0 | 0 ms |

The mobile LCP result is intentionally recorded as a poor baseline rather than presented as acceptable. The transitional CI ceiling is 8,500 ms and must be ratcheted toward the 2,500 ms target as CSS, fonts, and initial JavaScript are reduced. Desktop CLS initially measured 0.189; changing the three Google Font requests from `swap` to `optional` removed the measured shift without delaying content display.

### Current optimized candidate

Re-measured on the same pinned profile after removing the CSS import waterfall, deferring result-only setup styles, eliminating AislePilot font requests, and enabling Brotli/Gzip response compression:

| Profile | Performance | FCP | LCP | CLS | TBT |
|---|---:|---:|---:|---:|---:|
| Mobile, 375 x 812 | 0.92 | 1,656 ms | 3,008 ms | 0 | 115 ms |
| Desktop | 1.00 | 366 ms | 610 ms | 0 | 0 ms |

The mobile regression gate is now 0.80 performance and 5,000 ms LCP. This is a verified improvement, but the 2,500 ms target is still open; remaining work is concentrated in render-blocking CSS.

Both profiles made five allowlisted Google Font requests and no unexpected third-party requests. The CI check rejects new third-party hosts.

## Verification commands

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\check-aislepilot-asset-budgets.ps1 -RepoRoot (Get-Location).Path
powershell -NoProfile -ExecutionPolicy Bypass -File .\run_checks.ps1 -Mode Lighthouse
dotnet test MyBlog.Tests\MyBlog.Tests.csproj --filter "FullyQualifiedName~AislePilotTelemetryTests|FullyQualifiedName~AislePilotIntegrationTests.PerformanceTelemetry"
```

The asset check is also part of `run_checks.ps1`.

## Deployment baseline procedure

1. Deploy with browser performance telemetry enabled.
2. Allow representative mobile and desktop traffic to accumulate.
3. Record p50, p75, p95, and p99 for every duration metric.
4. Record CLS p50, p75, and p95 separately.
5. Segment by `has_result` and navigation type only.
6. Capture Lighthouse mobile and desktop reports under the agreed network and CPU profile.
7. Paste the measured values and report locations into this document.
8. Treat this commit as the comparison point for subsequent phases.

## Measured production values

Pending deployment traffic.
