# AislePilot Background Work Restart Decision

Date: 22 August 2026  
Status: Accepted

## Decision

AislePilot background jobs do not require a durable queue. The bounded in-memory queue may cancel active work and discard queued work when an application instance stops.

This is intentional because none of the current jobs is the system of record for a user-visible mutation. The plan returned to the user is assembled before these jobs run, and every queued operation is either an optimisation, cache refresh, derived image, or best-effort persistence step with an existing recovery trigger.

## Recovery by job

| Job | Restart recovery |
|---|---|
| Plan-pool replenishment | A later plan request or scheduled cache warmup replenishes the pool. |
| Special-treat generation | A later eligible plan request queues another treat. |
| Dessert recovery | A later dessert request or cache warmup retries recovery. |
| Meal-image generation | A later image lookup detects the missing image and queues generation again. |
| AI-meal persistence | The delivered plan remains valid; later generation can repopulate persistent meals. |
| Supermarket-layout hydration | A later layout request or scheduled cache warmup hydrates the cache. |
| Supermarket-layout refresh | A stale-cache request or scheduled cache warmup queues another refresh. |

## Guardrail

Every job name must be registered in `AislePilotBackgroundJobCatalog` with an explicit restart-recovery policy before the queue accepts it. A future job that represents an irreplaceable user mutation, paid operation requiring exactly-once handling, or non-reconstructible state must set a durable requirement and move to persistent queue infrastructure before release.

## Consequences

- Deployments can shut down promptly without attempting to serialize delegates or user request state.
- A restart can delay optional images, persistence, or cache enrichment until the documented recovery trigger occurs.
- Queue depth is instance-local; operational dashboards must aggregate across service instances.
- Durable queue infrastructure remains unnecessary until a non-reconstructible job is introduced.
