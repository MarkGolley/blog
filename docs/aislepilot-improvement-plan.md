# AislePilot Improvement Plan

Last updated: 22 August 2026  
Owner: Unassigned  
Overall status: In progress

This is the working delivery checklist for improving AislePilot. Update the checkboxes and phase status table as work lands. Each completed implementation item must include the narrowest appropriate automated coverage and pass the repository checks.

## Status legend

- `[ ]` Not started
- `[~]` In progress or partially delivered
- `[x]` Complete and verified
- `[!]` Blocked; add the reason beside the item

## Target outcomes

- Initial page usable within 1.5 seconds on the agreed mobile test profile.
- Warm plan generation completes within 2 seconds.
- Cold plan generation returns a useful fallback within 5 seconds.
- Interactive requests do not wait for OpenAI or supermarket research.
- Default setup requires no more than four core decisions.
- Mobile touch targets are at least 44 by 44 CSS pixels with at least 8px spacing.
- WCAG 2.2 AA automated checks pass with no serious or critical violations.
- CLS remains below 0.1 and mobile LCP below 2.5 seconds.
- Initial AislePilot JavaScript is below 200 KB uncompressed.
- AislePilot CSS is below 150 KB uncompressed.
- External AI, Firestore, image, and export failures degrade independently.

## Current baseline

- AislePilot CSS: approximately 342 KB uncompressed.
- AislePilot JavaScript: approximately 404 KB uncompressed.
- Main `aisle-pilot.js`: approximately 238 KB uncompressed.
- CSS manifest uses seven runtime `@import` requests plus additional stylesheets.
- Six AislePilot scripts are loaded alongside validation and shared scripts.
- Strong service, integration, desktop, mobile, and interaction coverage already exists.
- Fast interactive plan mode, background replenishment, and proactive cache hydration are implemented; the Release/Production HTTP path passes the committed local load gate, with live-traffic validation retained for the controlled-release phase.

## Phase status

| Phase | Status | Exit condition |
|---|---|---|
| 1. Measurement and budgets | In progress | Baselines, dashboards, and CI budgets exist |
| 2. Fast backend architecture | Complete | Managed background work and production-like verification complete |
| 3. Setup simplification | Not started | First-time default plan requires four core decisions |
| 4. Results simplification | Not started | Results have one clear navigation hierarchy |
| 5. Frontend performance | In progress | Asset and Core Web Vitals budgets pass |
| 6. Accessibility | Not started | WCAG 2.2 AA checks and keyboard journeys pass |
| 7. Reliability, privacy, security | Not started | Failure, retention, privacy, and abuse controls verified |
| 8. Test and release strategy | Not started | Production-like smoke, visual, and staged rollout gates pass |

---

## Phase 1 — Measurement and performance budgets

### Instrumentation

- [x] Record initial-page TTFB.
- [x] Record LCP, INP, and CLS from real user sessions.
- [x] Record setup-to-submit duration.
- [x] Record submit-to-plan-visible duration.
- [x] Record plan source using bounded categories for memory pool, template, AI, mixed, and existing-plan paths.
- [x] Record image-placeholder-to-image duration when each generated image replaces its placeholder.
- [x] Record swap, pantry suggestion, save, and export latency.
- [x] Capture client-side JavaScript errors with route and correlation ID. The AislePilot route is implicit, events contain no free-form error data, and warning logs include the server request ID.
- [~] Measure abandonment before generation and during generation. Setup-started, submitted, and abandoned events are recorded; abandonment during server generation requires a separate beacon/timeout definition.
- [x] Add p50, p95, and p99 dashboards for the primary journey, with browser p75 included for Core Web Vitals comparison.

### Server stage timings

- [x] Log total interactive plan latency and context latency.
- [x] Add structured timing for memory-pool lookup.
- [x] Add structured timing for template selection.
- [x] Add structured timing for validation and repair.
- [x] Add structured timing for plan assembly.
- [x] Add structured timing for dessert resolution.
- [x] Add structured timing for image lookup and polling, including server image resolution and client placeholder-to-image duration.
- [x] Correlate background replenishment with bounded dietary-complexity, meal-slot-count, plan-length, quick-meal, and special-treat tags without logging user-entered food data.

### CI budgets

- [x] Add an asset-size report to CI.
- [ ] Fail CI when initial AislePilot JavaScript exceeds 200 KB uncompressed.
- [ ] Fail CI when AislePilot CSS exceeds 150 KB uncompressed.
- [ ] Fail CI when an individual application script exceeds 80 KB without an allowlist.
- [x] Add Lighthouse CI for mobile and desktop, runnable independently and in the pre-deploy gate.
- [x] Add a budget that rejects unexpected third-party hosts and caps Google Font requests at five.
- [x] Add a service-level template-plan test with a one-second limit.
- [x] Add a production-like integration POST test with a two-second warm-request limit.
- [~] Store baseline Lighthouse reports and screenshots in the documented performance workflow. Mobile and desktop reports are now generated under ignored CI artifacts and the measured summary is committed; durable report retention remains open.

### Phase 1 acceptance

- [ ] Dashboard shows p50, p95, and p99 latency for plan generation.
- [x] Plan source and dependency timings are searchable through the operational dashboard.
- [~] CI rejects transitional asset and Lighthouse regressions; final 200 KB JavaScript and 150 KB CSS limits remain open.
- [x] A written mobile and desktop Lighthouse baseline is committed.

---

## Phase 2 — Fast and resilient backend architecture

### Interactive request path

- [x] Add configuration for disabling interactive AI generation.
- [x] Default production and development configuration to fast interactive mode.
- [x] Serve a plan from the in-memory AI pool when suitable meals exist.
- [x] Return a local template plan without waiting for fresh AI when the pool misses.
- [x] Avoid cold Firestore meal-pool hydration on the interactive request.
- [x] Avoid supermarket research and cold layout hydration on the interactive request.
- [x] Resolve built-in or cached desserts without external work on the interactive request.
- [x] Add regression tests proving a slow external AI response does not delay the returned plan.
- [x] Run production-like load tests with fast interactive mode enabled. The committed Release/Production runner exercises eight isolated concurrent sessions and 32 warm requests over the real HTTP endpoint with external AI disabled.
- [x] Confirm warm p95 below two seconds and cold p95 below five seconds. The 22 August 2026 baseline measured 119 ms warm p95 and 414 ms cold-session p95; see `docs/aislepilot-load-test-baseline.md`.

### Managed background work

- [x] Queue request-shaped plan-pool replenishment in the background.
- [x] Deduplicate replenishment by request profile.
- [x] Proactively hydrate meal, dessert, and layout caches on a ten-minute schedule with bounded multi-instance jitter.
- [x] Replace raw fire-and-forget tasks with a bounded channel and hosted worker. Plan replenishment, special treats, desserts, meal images, AI-meal persistence, and supermarket hydration/refresh all use the managed queue.
- [x] Define maximum queue capacity and rejection/coalescing behavior. Capacity is configurable and bounded from 1 to 256 (default 32); full queues reject new work, while existing per-job in-flight keys coalesce duplicates.
- [x] Support graceful shutdown and cancellation for managed queue work.
- [x] Add a configurable retry policy with 1–4 attempts, bounded exponential backoff capped at five seconds, shutdown cancellation, retry telemetry, and finalization after the last attempt.
- [x] Add managed-queue depth, wait-age, accepted/rejected/dequeued/completed/cancelled/faulted throughput, and background-job success/failure metrics.
- [x] Add last-success and last-failure freshness tracking for AI meals, desserts, supermarket layouts, and meal images, exposed as bounded refresh outcome counts and age gauges.
- [x] Add configurable startup and recurring refresh jitter for multi-instance deployments.
- [x] Add admin-key-protected runtime-cache refresh and bounded queue-inspection endpoints.
- [x] Decide whether queued work must survive application restarts. Current jobs are reconstructible and remain in-memory; each has a documented recovery trigger and new job types require an explicit durability policy before enqueueing.

### Image delivery

- [x] Generate compressed JPEG meal images.
- [x] Hydrate image metadata concurrently with bounded parallelism.
- [x] Poll meal images in bounded browser batches.
- [x] Replace newly generated mutable meal-name image URLs with deterministic SHA-256 content-versioned URLs while preserving legacy URL reads.
- [x] Apply one-year immutable caching only to versioned meal-image URLs; retain a one-day policy for legacy mutable paths.
- [x] Add width, height, `srcset`, and `sizes` where images have responsive display sizes. Meal and pantry cards now reserve a square intrinsic area, advertise their rendered-size hints, and keep responsive candidates synchronized when polling replaces a fallback; fixed-size reorder thumbnails and the brand logo also declare intrinsic dimensions.
- [x] Evaluate WebP/AVIF delivery while retaining a compatible fallback. Keep the content-versioned JPEG canonical and defer modern-format transcoding to a future CDN/edge layer; app-side encoding and duplicate OpenAI generation would add deployment weight, CPU/cost, and failure paths. The benchmark gate, negotiation rules, rollback, and JPEG fallback are documented in `docs/aislepilot-modern-image-format-decision.md`.
- [x] Add retention and cleanup rules for disk and Firestore image records. Daily bounded maintenance protects active and catalogue meals, removes unreferenced superseded content-versioned disk files after 30 days while retaining each family's newest version, and removes unprotected Firestore records after 365 days together with their backup chunks. Limits are configurable and deterministic policy tests cover age, protection, format, newest-version, and batch boundaries.

### Fallback quality

- [x] Verify template plans enforce the same dietary and allergen rules as generated plans. Template, pooled, and generated candidates now share ingredient-level dietary contradiction checks and expanded UK allergen-group matching; optional desserts use the same gate. The catalogue's vegan egg-noodle mismatch was corrected, and matrix regressions plus the safety boundary are documented in `docs/aislepilot-dietary-allergen-baseline.md`.
- [x] Verify template plans provide required meal-slot coverage. An exhaustive regression matrix now covers all 15 supported dietary-mode combinations across Breakfast, Lunch, and Dinner (45 catalogue cells), and separately proves that the compatibility pre-check and real template fallback planner both deliver all three requested slots. No catalogue gaps were found.
- [x] Verify uniqueness and budget behavior under every supported dietary combination. Seven-day template regressions now cover all 15 supported combinations, enforcing unique dinners whenever catalogue capacity permits, bounded breakfast/lunch repetition, non-increasing low-budget rebalance, and internally consistent budget totals and status.
- [x] Replace technical UI wording such as “Template fallback” with user-facing product language. The UI-facing plan-source contract now translates every known internal source into concise product language such as “AislePilot recipe plan”, “Personalised meal plan”, and “Fresh meal suggestion”; unknown sources degrade safely to “Your meal plan”. Internal orchestration and telemetry categories remain intact.
- [x] Add failure-matrix tests for OpenAI, Firestore, images, layouts, desserts, and exports. The executable matrix proves optional dependency failures retain a valid core plan or local enrichment, while export formatter failures return controlled, non-leaking Problem Details responses. The contracts and their named regression tests are indexed in `docs/aislepilot-external-failure-matrix.md`.

### Phase 2 acceptance

- [x] Interactive requests make no OpenAI calls in fast mode. Production and development now explicitly set `AislePilot:EnableInteractiveAiGeneration` to `false`; direct request-path tests count zero outbound calls for core, dessert, and special-treat plans while retaining background generation capability.
- [x] Background queue is bounded, observable, and shuts down cleanly.
- [x] Pool replenishment is proven under concurrency and external failure. An end-to-end managed-queue regression launches 12 concurrent fast-mode requests, verifies their replenishment work is coalesced to one OpenAI generation sequence, injects an initial HTTP 429, proves bounded retry recovery warms the pool, and confirms the queue remains healthy and releases the in-flight key.
- [x] Generated images can change without stale browser content because changed bytes produce a new content-versioned URL.
- [x] Every optional external dependency failure returns a valid core plan. OpenAI, Firestore, image, layout, and dessert degradation paths now share a strict validity assertion covering requested meal count, slot types, names, ingredients, recipe steps, image URLs, positive costs, shopping items, aisle order, and budget arithmetic. Export formatters are the documented boundary: if the requested file cannot be produced, they return a controlled non-leaking retry response while leaving the browser plan intact.

---

## Phase 3 — Simplify the setup journey

The UI direction is an accessible, friendly flat design using the existing Plus Jakarta Sans identity, semantic colour tokens, minimal decorative effects, and 150–200ms state transitions.

### Default journey

- [ ] Reduce the default setup to household size, number of days, meals required, and weekly budget.
- [ ] Choose safe defaults for supermarket, portion size, meal types, and cooking preference.
- [ ] Keep one visually dominant primary action per setup mode.
- [ ] Place a compact “What you will get” summary immediately above the primary action.
- [ ] Ensure the primary action remains visible and unobstructed on small mobile screens.

### Progressive disclosure

- [ ] Move portion size into a single “Personalise” section.
- [ ] Move quick-meal preference into “Personalise.”
- [ ] Move dietary style and foods to avoid into “Personalise,” while keeping safety-critical allergen entry clearly labelled.
- [ ] Move saved-meal repetition into “Personalise.”
- [ ] Move supermarket layout, special treat, and dessert options into “Personalise.”
- [ ] Preserve all advanced values when the section is collapsed or the mode changes.

### Planner and pantry modes

- [ ] Give “Plan my week” a concise purpose statement and dedicated defaults.
- [ ] Give “Use my ingredients” a concise purpose statement and dedicated defaults.
- [ ] Hide irrelevant controls in each mode rather than disabling them without explanation.
- [ ] Use independent summaries and primary actions for both modes.
- [ ] Preserve the selected mode without creating surprising state on a fresh visit.

### Language and validation

- [ ] Review “strict core,” “repeat strength,” “cook days,” “plan days,” “special treat,” and “budget rebalance” with users.
- [ ] Replace implementation-oriented copy with task-oriented language.
- [ ] Associate every validation message with its field.
- [ ] Focus the first invalid field after submission.
- [ ] Add an accessible error summary with links when multiple errors exist.
- [ ] Validate on blur rather than while the user is typing where practical.
- [ ] Preserve every entered value after validation failure.
- [ ] Stop classifying failures by parsing display-message text; use typed error categories.

### Phase 3 acceptance

- [ ] A first-time user can generate a default plan without opening advanced settings.
- [ ] Default setup contains no more than four required decisions.
- [ ] Both setup modes are understandable without documentation.
- [ ] Validation recovery passes keyboard and screen-reader tests.
- [ ] All setup touch targets are at least 44 by 44 pixels with at least 8px separation.

---

## Phase 4 — Simplify plan results

### Information hierarchy

- [ ] Establish the order: weekly status, selected day, meal details, shopping, exports.
- [ ] Keep one primary results navigation model across desktop and mobile.
- [ ] Keep one day-selection model and avoid competing carousel, tab, and quick-jump semantics.
- [ ] Remove nested scroll regions where normal page scrolling is sufficient.
- [ ] Ensure returning from an action restores the selected day, meal slot, panel, and scroll position.

### Meal interactions

- [ ] Make “View recipe,” “Swap,” and “Save” the visible primary meal actions.
- [ ] Move remove, reorder, and infrequent controls to a clearly labelled overflow menu.
- [ ] Provide explicit move-earlier and move-later controls as the accessible alternative to drag-and-drop.
- [ ] Treat drag and swipe as progressive enhancements, never the sole route to an action.
- [ ] Ensure every action provides visible feedback within 100ms.
- [ ] Keep touch targets at least 44 by 44 pixels.
- [ ] Prevent gesture conflicts between image taps, meal-slot selection, day swipes, and page navigation.

### States and recovery

- [ ] Add a clear state for images still generating.
- [ ] Add a helpful empty state for pantry suggestions.
- [ ] Add a background-generation-pending state without blocking the existing plan.
- [ ] Add recoverable export-failure UI.
- [ ] Add recoverable saved-state failure UI.
- [ ] Add supermarket-refresh status without exposing technical details.
- [ ] Ensure partial failures never replace the full plan with a generic error.

### Phase 4 acceptance

- [ ] Core meal actions require at most one additional interaction.
- [ ] All results actions work by keyboard and touch.
- [ ] JavaScript-disabled output still exposes meals, shopping, and exports.
- [ ] Focus returns to the initiating control after menus and sheets close.
- [ ] Results remain usable at 200% and 400% zoom.

---

## Phase 5 — Frontend performance and design-system cleanup

### CSS

- [ ] Audit `aisle-pilot-refresh.css` for obsolete and duplicated rules.
- [ ] Define semantic tokens for surface, text, border, focus, success, warning, and destructive states.
- [ ] Consolidate spacing into a 4px/8px rhythm.
- [ ] Consolidate icon size, elevation, radius, z-index, and motion scales.
- [ ] Build one minified and fingerprinted AislePilot CSS bundle.
- [x] Remove runtime CSS `@import` chains; component stylesheets are now discovered directly from the document in stable cascade order.
- [ ] Inline or prioritize only genuinely critical above-the-fold CSS.
- [ ] Verify light and dark themes independently.

### JavaScript

- [ ] Map duplicate responsibilities between `core.js` and `aisle-pilot.js`.
- [ ] Split setup, results, meal interactions, shopping, images, and saved/export behavior into bounded modules.
- [ ] Load results-only code only when results exist.
- [ ] Use `defer` or ES modules for non-critical scripts.
- [ ] Remove obsolete code paths and repeated DOM queries.
- [ ] Batch layout reads and writes in carousel and responsive handlers.
- [ ] Debounce or throttle resize, scroll, pointer, and input handlers where appropriate.
- [ ] Remove jQuery validation from the critical path or replace it with native validation.

### Delivery

- [x] Enable Brotli and Gzip response compression for compressible responses, including static CSS, JavaScript, and SVG assets.
- [ ] Apply one-year immutable caching to fingerprinted CSS and JavaScript.
- [ ] Revalidate HTML rather than serving stale application state.
- [ ] Keep polling responses `no-store`.
- [x] Validate a system-font alternative for AislePilot and remove its external font requests.
- [x] Use `font-display: optional` for the remaining blog font request.
- [ ] Reserve image and loading-shell dimensions to keep CLS below 0.1.

### Phase 5 acceptance

- [ ] AislePilot CSS is at least 50% smaller and below 150 KB uncompressed.
- [ ] Initial JavaScript is at least 40% smaller and below 200 KB uncompressed.
- [ ] No CSS `@import` waterfall remains.
- [ ] No render-blocking external font dependency remains.
- [ ] Mobile LCP is below 2.5 seconds and CLS below 0.1.
- [ ] Input feedback remains below 100ms under the agreed test profile.

---

## Phase 6 — Accessibility and inclusive interaction

### Automated and keyboard coverage

- [ ] Add axe-core Playwright audits for initial setup, validation, loading, results, shopping, and export views.
- [ ] Test keyboard-only setup and generation.
- [ ] Test keyboard navigation across result panels and day selection.
- [ ] Test keyboard operation of meal details, action menus, shopping, and exports.
- [ ] Test Escape and focus restoration for menus, dialogs, and mobile sheets.
- [ ] Verify hidden carousel cards and panels cannot receive focus.

### Semantics and focus

- [ ] Audit every `tablist`, `tab`, and `tabpanel` against the complete keyboard pattern.
- [ ] Remove tab semantics from controls that behave as ordinary navigation or view buttons.
- [ ] Verify one logical heading hierarchy from the page title through results.
- [ ] Move focus to the plan heading after successful generation.
- [ ] Move focus to the first invalid field after validation failure.
- [ ] Preserve focus and announce the changed meal after a swap.
- [ ] Ensure live regions announce changes once without repetitive noise.

### Visual and responsive accessibility

- [ ] Verify normal-text contrast of at least 4.5:1 in light mode.
- [ ] Verify normal-text contrast of at least 4.5:1 in dark mode.
- [ ] Verify UI component and focus-indicator contrast of at least 3:1.
- [ ] Ensure colour is never the only budget, error, success, or selection indicator.
- [ ] Test forced-colours mode.
- [ ] Test reduced motion.
- [ ] Test 200% and 400% zoom.
- [ ] Test 375px portrait and narrow landscape without horizontal page scrolling.
- [ ] Test screen-reader order on mobile and desktop.

### Phase 6 acceptance

- [ ] No serious or critical axe violations.
- [ ] Every primary workflow completes without a pointer.
- [ ] Focus is always visible and follows visual order.
- [ ] Reduced-motion mode removes non-essential movement.
- [ ] Both themes meet the agreed contrast thresholds.

---

## Phase 7 — Reliability, privacy, and security

- [ ] Add circuit-breaker behavior around OpenAI and Firestore.
- [ ] Define separate cost-aware rate-limit policies for generation, swaps, polling, and exports.
- [ ] Add request idempotency for generation and mutation actions.
- [ ] Prevent duplicate form submissions on slow connections.
- [ ] Measure saved-week and saved-meal cookie size and growth.
- [ ] Move saved state server-side if cookie limits can be approached.
- [ ] Document local-storage, session-storage, and cookie use in the privacy notice.
- [ ] Define retention for generated meals, layouts, images, and telemetry.
- [ ] Ensure logs never contain allergens, pantry contents, dislikes, or other user-entered food text.
- [ ] Add operational alerts for queue backlog, cache staleness, fallback rate, image failures, and elevated latency.
- [ ] Test deployment restart, partial outage, and dependency recovery behavior.

### Phase 7 acceptance

- [ ] Duplicate submission cannot create duplicate expensive work.
- [ ] External outages have tested and documented fallback behavior.
- [ ] Cookie and request sizes stay below explicit limits.
- [ ] No user-entered food or health-adjacent data appears in logs.
- [ ] Retention and cleanup jobs are observable.

---

## Phase 8 — Test strategy and controlled release

### Test architecture

- [ ] Retain behavior-focused integration coverage while reducing exact CSS/DOM coupling.
- [ ] Add focused tests for setup, result navigation, shopping, and meal cards.
- [ ] Add managed-queue contract tests.
- [ ] Add cache-readiness and stale-cache tests.
- [ ] Add independent performance and accessibility test categories.
- [ ] Add production-like smoke coverage with compression, cache headers, and fast interactive mode enabled.

### Visual regression matrix

- [ ] 360×800 small phone.
- [ ] 390×844 large phone.
- [ ] Tablet portrait.
- [ ] Tablet landscape.
- [ ] 1440×900 desktop.
- [ ] Light and dark themes.
- [ ] Validation errors.
- [ ] Loading shell.
- [ ] Partial image state.
- [ ] Empty and external-failure states.
- [ ] Reduced motion and forced colours.

### Release

- [ ] Define feature flags for setup redesign, results redesign, and managed background queue.
- [ ] Release backend performance changes before major UI restructuring.
- [ ] Compare baseline and candidate telemetry under equivalent traffic.
- [ ] Run a small staged rollout and define automatic rollback thresholds.
- [ ] Collect qualitative feedback from first-time and returning users.
- [ ] Remove old UI and feature flags only after the new path meets all gates.

### Phase 8 acceptance

- [ ] Full repository checks pass.
- [ ] Performance, accessibility, and visual suites pass independently.
- [ ] Production metrics meet target outcomes for the agreed observation period.
- [ ] Rollback is documented and tested.
- [ ] Obsolete code and flags are removed after rollout.

---

## Definition of done for every checklist item

- [ ] The smallest responsible module was changed.
- [ ] Behavior changes are documented.
- [ ] The narrowest appropriate automated regression test was added or updated.
- [ ] Accessibility impact was reviewed.
- [ ] Mobile, desktop, light mode, and dark mode impact was considered where relevant.
- [ ] Performance impact was measured where relevant.
- [ ] `run_checks.ps1 -Mode Tests` passes.
- [ ] `scripts/check-oversized-files.ps1` passes.
- [ ] E2E checks pass for interaction or layout work.
- [ ] This plan and its phase status table are updated.

## Progress log

- 2026-08-22 — Completed the Phase 2 production-like performance gate. A repeatable Release/Production Kestrel profile now sends full seven-day, three-meal plans through the antiforgery-protected HTTP endpoint at eight-way concurrency. With external AI disabled, cold-session p95 measured 414 ms and warm p95 119 ms, inside the 5,000 ms and 2,000 ms gates.

- 2026-08-22 — Completed the final Phase 2 functional acceptance contract for external failures. OpenAI rate limiting, unreachable Firestore, unavailable image generation, rejected layout research, and deferred dessert work must now pass one shared core-plan validator covering meal content, slot/count integrity, usable image paths, shopping output, aisle order, costs, and budget arithmetic. Export failure remains an explicit requested-deliverable boundary with controlled Problem Details handling.

- 2026-08-22 — Proved pool replenishment under concurrency and transient provider failure. Twelve simultaneous fast-mode plans now exercise the real managed queue and static coalescing key; a simulated OpenAI 429 is recovered on the bounded second attempt, the warmed pool serves a personalised plan, no work is rejected or faulted, and finalization clears the in-flight marker. The focused scenario passed three consecutive runs.

- 2026-08-22 — Enforced the fast-mode deployment contract. The earlier setting applied only to Daily Capsule, leaving AislePilot's missing-value default able to enable blocking interactive AI in production and development. Both AislePilot configuration sections now explicitly disable interactive AI, and five regressions verify the deployed settings plus zero request-path OpenAI calls for standard, dessert, and special-treat plans.

- 2026-08-22 — Completed the external failure matrix for OpenAI, Firestore, meal images, supermarket layouts, desserts, and exports. Existing degradation tests are now indexed against explicit contracts, and new plan-pack/checklist fault-injection tests prove unexpected formatter exceptions return retryable 500 Problem Details without leaking exception text. The matrix distinguishes optional enrichment failures, which preserve the core plan, from export failures, where the requested file cannot be produced safely.

- 2026-08-22 — Replaced technical plan-source wording at the UI contract boundary. Template, pool, provider, mixed-generation, swap, current-plan, and budget paths now return plain customer language without exposing implementation terms such as fallback, template, OpenAI, or AI pool. Added a 14-case mapping regression and retained stable low-cardinality telemetry classification for both legacy and new labels.

- 2026-08-22 — Verified template uniqueness and budget behaviour across every supported dietary combination. The 15-combination matrix now checks 315 weekly meal slots for capacity-aware dinner uniqueness and breakfast/lunch repeat caps, then checks each combination under a constrained four-person budget for non-increasing rebalance cost, exact budget-delta arithmetic, coherent over-budget status, and an actionable rebalance outcome. No planner defect was found.

- 2026-08-22 — Verified required template meal-slot coverage. Automated tests now exercise 45 dietary-combination/meal-slot catalogue cells and run every supported combination through both `HasCompatibleMeals` and real three-slot fallback generation. All combinations provide Breakfast, Lunch, and Dinner without relying on AI pool content; no catalogue repair was required.

- 2026-08-22 — Enforced fallback dietary/allergen parity. Template, pooled, and AI candidates now share ingredient-level checks for Vegetarian, Vegan, Pescatarian, Gluten-Free, and common UK allergen-group aliases; incompatible optional desserts are omitted. Corrected the vegan tofu bowl from egg noodles to rice noodles and added an eight-case fallback compatibility regression matrix.

- 2026-08-22 — Added generated meal-image retention. Scheduled cache maintenance now runs cleanup at most every 24 hours, protects active/catalogue images, keeps the newest disk version for every meal, removes only superseded unreferenced versioned files older than 30 days, and expires unprotected Firestore image records after 365 days with bounded batches. Legacy bundled filenames are never selected by the disk cleanup policy.

- 2026-08-22 — Completed the WebP/AVIF delivery evaluation. AislePilot will retain its content-versioned JPEG as the durable fallback and introduce negotiated derivatives at a future image CDN/edge layer only after a representative benchmark proves at least 15% median byte savings without latency regression. App-side transcoders and duplicate OpenAI requests were rejected because they increase deployment weight, CPU/cost, and operational failure surface.

- 2026-08-22 — Completed responsive image markup. Full meal and pantry images now declare 1024×1024 intrinsic dimensions plus `srcset`/`sizes`; the 512×512 fallback is identified accurately, polling updates `src` and `srcset` atomically, fixed reorder thumbnails reserve 32×32 pixels, and the brand SVG declares its native 900×220 dimensions.

Add dated entries here when a meaningful slice is completed.

- 2026-08-22 — Added content-versioned meal images. New JPEG filenames include a deterministic 16-hex SHA-256 content version, changed bytes produce a new browser URL, and only verified versioned paths receive one-year immutable caching. Existing mutable Firestore URLs remain readable with a one-day cache policy.

- 2026-08-22 — Accepted and enforced the background-work restart decision. Current jobs are optional or reconstructible, so durable queue infrastructure is not required. Each job now has a registered recovery trigger, and the queue rejects unclassified future job types. See `docs/aislepilot-background-work-decision.md`.

- 2026-08-22 — Added authenticated operational controls. Administrators can trigger the existing runtime-cache refresh path and inspect bounded queue capacity, concurrency, depth, active work, lifecycle totals, and queued counts by fixed job category. Endpoints reuse the existing admin key and expose no work payloads or user-entered data.

- 2026-08-22 — Completed proactive cache scheduling and multi-instance jitter. Runtime cache warmup now starts within a configurable jitter window and repeats on a bounded interval with symmetric jitter, preventing replicas from refreshing Firestore-backed caches in lockstep.

- 2026-08-22 — Added cache freshness tracking. AI-meal, dessert, supermarket-layout, and meal-image refreshes now expose bounded success/failure counts and seconds since each last outcome, with dedicated Grafana panels and no cache keys or meal/user data in labels.

- 2026-08-22 — Added bounded managed-job retries. Escaped job failures now retry up to the configured attempt limit with cancellable exponential backoff capped at five seconds. Coalescing keys remain held across retries, retry scheduling is observable, and cleanup runs exactly once after the final outcome.

- 2026-08-22 — Completed managed-queue observability. Added depth, per-job wait time, lifecycle throughput and outcome metrics plus Grafana panels for queue throughput, rejection, depth, and p95 wait. Queue health can now be separated from background-job execution duration and failures.

- 2026-08-22 — Completed the raw fire-and-forget migration. AI-meal persistence, supermarket hydration/refresh, and meal-image generation now join plan replenishment, treats, and desserts on the bounded hosted queue. The queue runs three workers by default, preserves the existing image concurrency ceiling, coalesces duplicate jobs, rejects work when full, and propagates shutdown cancellation.

- 2026-08-22 — Migrated special-treat generation and dessert recovery to the bounded hosted queue. Both now receive host shutdown cancellation and release their duplicate-coalescing keys on queue rejection. Four raw background paths remain.

- 2026-08-22 — Began the managed-background-work migration. Added a configurable bounded channel and hosted worker, migrated plan-pool replenishment off raw `Task.Run`, defined full-queue rejection and duplicate coalescing behavior, and added shutdown-cancellation tests. Six job paths remain to migrate before the fire-and-forget item is complete.

- 2026-08-22 — Completed privacy-safe replenishment correlation. Background plan-pool jobs now carry bounded request-profile dimensions only, and the operational dashboard exposes replenishment outcomes alongside bounded plan-source counts. No supermarket, allergen, dislike, pantry, or meal-name text is recorded.

- 2026-08-22 — Began Phase 5. Removed the runtime CSS import waterfall, deferred result-only setup-page styles, removed AislePilot font requests, and enabled Brotli/Gzip response compression. On the pinned local profile, mobile Lighthouse improved from 0.63/7,837 ms LCP to 0.92/3,008 ms, while desktop reached 1.00/610 ms LCP. The mobile CI floor is now 0.80 with a 5,000 ms transitional LCP ceiling.

- 2026-08-22 — Created the trackable improvement plan. Marked the fast interactive generation, local layout/dessert resolution, compressed JPEG generation, bounded image work, and initial latency regression tests as delivered in the current branch. Managed background execution and production validation remain open.
- 2026-08-22 — Began Phase 1. Added privacy-safe browser telemetry for TTFB, LCP, INP, CLS, page usability, setup duration, and plan-visible duration; added a rate-limited ingestion endpoint; documented the baseline; and added transitional asset budgets to `run_checks.ps1`.
