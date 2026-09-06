# AislePilot Load-Test Baseline

Measured: 22 August 2026

## Result

The fast interactive path passed both Phase 2 latency gates:

| Cohort | Samples | p50 | p95 | Gate |
|---|---:|---:|---:|---:|
| Cold isolated sessions | 8 | 408 ms | 414 ms | below 5,000 ms |
| Warm requests | 32 | 91 ms | 119 ms | below 2,000 ms |

## Profile

- ASP.NET Core runs from the Release build with the Production environment.
- Eight virtual users submit concurrently, each with an isolated antiforgery token, cookie, and rate-limit identity.
- Every request asks for a seven-day plan with Breakfast, Lunch, and Dinner for two people.
- The cold cohort is the first plan POST from each newly established session after host startup.
- The warm cohort reuses those sessions for 32 further requests in batches of eight.
- AislePilot and Daily Capsule external AI generation are disabled. Firestore uses an explicitly opted-in in-memory substitute so the offline benchmark measures the interactive application path rather than local credential discovery or network availability.
- The host uses the normal forwarded-HTTPS contract so Production secure-cookie and antiforgery behavior remain active.
- Test machine: AMD Ryzen 7 5700X, 15.9 GB RAM, Windows, .NET SDK 10.0.103.

This is a repeatable local production-like gate, not a substitute for observing p95 under staged live traffic. Live comparison and rollback thresholds remain part of Phase 8.

## Run it

From the repository root:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\run-aislepilot-load-test.ps1
```

The runner exits non-zero when cold p95 is at least 5,000 ms or warm p95 is at least 2,000 ms. Override virtual-user and warm-request counts with `-VirtualUsers` and `-WarmRequests` when exploring higher concurrency; the committed baseline uses the defaults.
