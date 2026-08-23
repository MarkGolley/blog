# Public UI/UX Readiness Plan

Last updated: 23 August 2026  
Owner: Unassigned  
Overall status: In progress  
Scope: Public portfolio, articles, contact journey, and AislePilot  
Related technical plan: `docs/aislepilot-improvement-plan.md`

This is the working checklist for taking the public site from a strong portfolio build to a coherent, trustworthy, production-ready experience. It does not replace the AislePilot performance and reliability plan; it adds the cross-site visual, interaction, content, accessibility, and trust work needed for a public launch.

Update the phase table, tick completed tasks, and add a dated progress-log entry whenever a verified slice lands.

## Status legend

- `[ ]` Not started
- `[~]` In progress or partially delivered
- `[x]` Complete and verified
- `[!]` Blocked; add the reason beside the item

## Review basis

The 23 August 2026 review covered:

- Homepage, Projects, Articles, About, Contact, and AislePilot setup.
- A generated AislePilot plan, weekly status, meal navigation, and meal card.
- Desktop at 1440 × 1100 and mobile at 390 × 844.
- Light and dark presentation, with the detailed screenshot review primarily performed in light mode.
- DOM geometry checks for overflow, heading count, visible controls, and touch-target size.
- Source review of layouts, metadata, navigation, forms, error handling, sitemap, and robots output.
- The `ui-ux-pro-max` accessibility, interaction, responsive, typography, loading, and public-readiness guidelines.

### Evidence summary

- All audited routes rendered one `h1` and showed no horizontal page overflow at 390px.
- No browser JavaScript errors appeared on the six initial public routes.
- The generated AislePilot page exposed roughly 124 visible interactive elements on mobile. A geometry scan flagged many 30–40px controls; custom inputs create some false positives, but manually visible examples such as theme/menu buttons, result controls, article likes, and carousel controls are genuinely below 44px.
- AislePilot mobile users must pass the full weekly-status and supermarket-research block before reaching the first meal.
- The public shell has good responsive stacking, but AislePilot uses a separate shell and metadata path that presents it as “MyBlog” rather than a finished product.
- Repeated rapid plan generation produced `429 Too Many Requests`; public-facing recovery copy and retry behavior must be deliberately designed even when the limit itself is correct.

## What already works

- [x] A recognisable teal, navy, amber, and cream visual identity exists.
- [x] Fraunces and Space Grotesk provide a distinctive editorial/product pairing.
- [x] Light and dark themes exist and initialise before page rendering.
- [x] Public pages have clear headings, readable body text, rounded surfaces, and consistent content gutters.
- [x] The mobile layout stacks without horizontal page scrolling at the audited width.
- [x] A skip link, semantic main region, canonical metadata on the public shell, sitemap, robots, and RSS routes exist.
- [x] AislePilot setup uses progressive disclosure, sensible defaults, visible labels, summaries, validation recovery, and loading feedback.
- [x] AislePilot results have clear Meals, Shop, and Export destinations plus visible Swap and Save actions.

## Main findings

| Priority | Area | Finding | Public impact |
|---|---|---|---|
| P0 | Touch and keyboard access | Several visible controls are 30–41px, including global theme/menu buttons, article likes, carousel controls, and AislePilot result controls. | Missed taps, weaker accessibility, and an unfinished mobile feel. |
| P0 | Production recovery | The generic error view includes development-environment instructions, and there is no designed 404/429 experience. | Low trust at the exact moment a public visitor needs reassurance. |
| P0 | Privacy and data use | The site stores visitor, theme, plan, saved-week, and saved-meal state but offers no visible privacy/data-use explanation. | Visitors cannot understand what is stored, why, or for how long. |
| P0 | AislePilot product identity | The standalone layout uses “AislePilot - MyBlog,” lacks the public shell's canonical/social metadata, and has no product footer or trust links. | The product feels like an embedded prototype instead of a launch-ready service. |
| P1 | Cross-site consistency | Portfolio pages and AislePilot use related colours but different surface, hierarchy, icon, and navigation rules. | The experience feels like two sites rather than one intentional product family. |
| P1 | Homepage hierarchy | The hero has four competing calls to action, a very large statement, and novelty content before strong delivery proof. | Visitors must decide what the site is for instead of being guided to the best next step. |
| P1 | Project proof | Project cards lead with status and internal “Next” work; the strongest scope, screenshots, outcomes, and proof remain hidden behind disclosure. | Work looks in progress rather than clearly demonstrated. |
| P1 | Article discovery | Twenty similar article rows, dense tags/meta, and repeated like controls create a long, low-differentiation mobile feed. | Scanning and choosing an article takes too much effort. |
| P1 | Contact conversion | Contact methods are repeated, field errors are not visibly rendered beside inputs, submission has no dedicated pending state, and no privacy reassurance is shown. | Visitors can lose confidence or fail to recover from errors. |
| P1 | AislePilot setup length | The mobile hero, benefit pills, start card, instructions, steps, form, and live summary repeat similar information. | First-time setup feels longer than the four real decisions it contains. |
| P1 | AislePilot result hierarchy | Budget cards and technical supermarket provenance appear before the user's meals; mobile reaches the first meal only after a long status section. | The product delays its main reward and feels operationally dense. |
| P1 | Visual density | Nested cards, pills, borders, shadows, and all-caps micro-labels are used at nearly every hierarchy level. | Important content has insufficient contrast against supporting detail. |
| P2 | Social proof and reassurance | AislePilot has benefit copy but no concise beta status, estimate disclaimer, data-use summary, example output, or user proof. | New visitors have little evidence that the product is safe, useful, and predictable. |
| P2 | Release evidence | There is no committed cross-site visual matrix or axe-based public-page gate. | Visual and accessibility regressions can reach production unnoticed. |

## Target design direction

Do not replace the existing brand. Refine it into one calm, credible product family.

- Keep Fraunces for editorial display headings and Space Grotesk for body and interface copy.
- Keep teal as the main action colour, amber as a restrained accent, navy as the high-contrast text colour, and warm off-white surfaces.
- Use semantic tokens rather than page-specific colours: background, surface, raised surface, text, muted text, border, primary, accent, success, warning, danger, and focus.
- Limit the elevation system to three levels: page, card, and overlay. Avoid nested shadows.
- Use the existing 4/8px rhythm with named spacing tokens and consistent responsive gutters.
- Use one primary action per section or task state; visually subordinate secondary and tertiary actions.
- Use one coherent SVG icon family and consistent 16/20/24px icon sizes. Do not use emoji as interface icons.
- Keep motion functional, 150–250ms, transform/opacity based, interruptible, and reduced-motion aware.
- Treat AislePilot as a sub-brand that inherits the shared tokens, focus rules, button hierarchy, footer trust links, and theme behavior.

## Target outcomes

- Every public page communicates its purpose and primary next action within the first viewport.
- Every visible pointer target is at least 44 × 44 CSS pixels with at least 8px spacing where targets are adjacent.
- The first AislePilot meal is visible substantially earlier on mobile, immediately after a compact weekly summary and result navigation.
- Public errors, rate limits, empty states, and dependency failures provide a plain-language cause, recovery action, and safe route onward.
- All public and AislePilot pages share one token system, interaction hierarchy, icon language, and trust footer.
- WCAG 2.2 AA automated checks report no serious or critical violations in either theme.
- Core journeys complete at 200% zoom, keyboard-only, reduced motion, and 390px mobile without loss of content or control.
- Public metadata, social cards, privacy/data-use content, and branded 404/500 pages are verified before launch.

## Phase status

| Phase | Status | Exit condition |
|---|---|---|
| 0. Baseline and guardrails | In progress | Repeatable screenshot, axe, and geometry baselines exist |
| 1. Shared design foundation | Not started | One documented token/component system serves both shells |
| 2. Public shell, navigation, and trust | Not started | Navigation, footer, metadata, legal, 404, and 500 surfaces are launch-ready |
| 3. Homepage and positioning | Not started | Hero has one clear journey and proof appears before secondary content |
| 4. Projects and case studies | Not started | Each flagship project has outcome-led, shareable evidence |
| 5. Articles and reading journey | Not started | Mobile discovery is concise and article reading is distraction-free |
| 6. Contact and conversion | Not started | Form feedback, privacy, error recovery, and touch targets pass |
| 7. AislePilot entry and setup | Not started | Setup communicates trust and reaches its first decision quickly |
| 8. AislePilot results | Not started | Meals lead, supporting detail is disclosed progressively, and controls pass |
| 9. Accessibility and inclusive QA | Not started | WCAG 2.2 AA gates and manual journeys pass |
| 10. Public release validation | Not started | Cross-browser, visual, metadata, performance, and user-review gates pass |

---

## Phase 0 — Baseline and guardrails

### Audit harness

- [x] Add a Playwright public-page screenshot suite for Home, Projects, Articles, one article, About, Contact, 404, and 500. The suite captures each route with a stable artifact name and preserves the current 404/500 structural gaps as explicit exceptions until Phase 2 replaces those pages.
- [~] Add AislePilot screenshots for setup, validation, loading, results, shopping, exports, empty data, rate limit, and dependency failure. Setup, server validation, the production loading shell, generated results, shopping, exports, saved-data empty states, and rate limiting now have mobile/desktop evidence; dependency failure remains.
- [x] Cover 360 × 800, 390 × 844, 768 × 1024, 1440 × 900, and 1440 × 1100.
- [x] Capture light, dark, and reduced-motion variants. Every viewport now runs light and dark with both normal and reduced-motion preferences.
- [~] Add a geometry audit for horizontal overflow, fixed-element overlap, 44px targets, and 8px adjacent-target spacing. Horizontal overflow and small-target regression ceilings are enforced; close-target pairs and fixed/sticky elements are recorded in JSON for the next enforcement slice.
- [x] Add axe-core to the browser test project and fail on serious or critical violations. The public-route gate runs mobile and desktop in both themes.
- [x] Record existing intentional exceptions with owners and expiry dates; do not silently exclude large page regions.

### Temporary axe exceptions

| Route | Axe rule | Owner | Expires | Reason |
|---|---|---|---|---|
| Framework 404 | `document-title` | Public shell | 2026-09-30 | The unbranded framework response has no document title; Phase 2 replaces it with the shared public error shell. |
| Framework 404 | `html-has-lang` | Public shell | 2026-09-30 | The unbranded framework response has no language attribute; Phase 2 replaces it with the shared public error shell. |

The test fails if one of these exceptions remains after its expiry date. No page region is excluded from axe scanning.

### Baseline documentation

- [ ] Store a small approved baseline set under a documented visual-test artifact path.
- [ ] Record public-page Lighthouse accessibility and performance scores.
- [ ] Record the first-meal vertical position on 390px AislePilot results.
- [ ] Record the number of visible actions before the first meal and before the first article.
- [ ] Record light/dark contrast measurements for every semantic token pair.

### Phase 0 acceptance

- [x] The audit suite can be run locally with one documented command.
- [x] Failures identify the route, viewport, theme, element, and measured value.
- [ ] Baselines are reviewed rather than automatically overwritten.

### Running the Phase 0 public audit

```powershell
$env:RUN_PLAYWRIGHT_E2E = "1"
dotnet test MyBlog.Tests/MyBlog.Tests.csproj --filter "FullyQualifiedName~PublicUiRoutes_CaptureThemeBaselinesAndRejectGeometryRegressions"
```

If a development site is already running, preserve its process and point the suite at it:

```powershell
$env:PLAYWRIGHT_EXTERNAL_BASE_URL = "http://localhost:8080"
```

Screenshots and matching JSON diagnostics are written to `MyBlog.Tests/artifacts/public-ui/`. Set `PUBLIC_UI_ARTIFACT_ROOT` to override that location. Artifacts are evidence for review; they are not automatically accepted as new baselines.

AislePilot workflow screenshots are written below that root in `aislepilot-states/`. Run them with:

```powershell
$env:RUN_PLAYWRIGHT_E2E = "1"
dotnet test MyBlog.Tests/MyBlog.Tests.csproj --filter "FullyQualifiedName~AislePilot_CapturesCoreWorkflowStateBaselines"
```

---

## Phase 1 — Shared design foundation

### Tokens

- [ ] Inventory colours, spacing, radii, shadows, font sizes, line heights, z-indexes, transitions, and control heights across all CSS files.
- [ ] Define shared semantic light/dark tokens in `site.css` and map AislePilot tokens to them.
- [ ] Define a 4/8px spacing scale and responsive page gutters.
- [ ] Define a restrained radius scale and three-level elevation scale.
- [ ] Define type roles for display, page title, section title, card title, body, supporting text, label, and metadata.
- [ ] Define shared focus-ring, disabled, hover, pressed, selected, success, warning, danger, and loading states.
- [ ] Remove or alias duplicate raw values only after rendered-output and visual regressions exist.

### Components

- [ ] Standardise primary, secondary, tertiary, danger, icon-only, and loading buttons.
- [ ] Standardise cards, badges, chips, alerts, empty states, form fields, disclosures, tabs, menus, and toasts.
- [ ] Ensure nested surfaces use borders or spacing rather than another shadow wherever possible.
- [ ] Choose one SVG icon family and document stroke width, sizes, alignment, and accessible-label rules.
- [ ] Replace emoji or font-dependent interface glyphs with the chosen SVG components.
- [ ] Create a development-only component showcase or documented reference page for both themes.

### Phase 1 acceptance

- [ ] Public pages and AislePilot consume the same semantic token contract.
- [ ] No new component introduces raw colour, arbitrary radius, arbitrary shadow, or one-off transition values without documented justification.
- [ ] Light and dark component states pass contrast checks.

---

## Phase 2 — Public shell, navigation, and trust

### Navigation

- [ ] Increase desktop nav, theme, and mobile-menu targets to at least 44px without making the header visually bulky.
- [ ] Make the complete mobile menu keyboard-operable, Escape-dismissable, focus-contained while open, and focus-restoring on close.
- [ ] Ensure active navigation is conveyed by text/shape as well as colour.
- [ ] Reposition “Today in code” as optional secondary content so it does not compete with primary navigation on small screens.
- [ ] Verify sticky/fixed shell behavior never obscures focused content or anchor destinations.

### Footer and trust routes

- [ ] Replace the minimal footer with a compact shared footer containing primary navigation, contact, RSS, privacy/data use, accessibility, and product status links.
- [ ] Add a plain-language privacy/data-use page covering visitor IDs, theme storage, AislePilot setup/plan/saved-state cookies, telemetry, contact messages, subscriptions, and retention.
- [ ] Add an accessibility statement with known limitations and a contact route for reporting issues.
- [ ] Decide whether terms are needed for AislePilot beta use and document the decision; if needed, add concise terms and estimate disclaimers.
- [ ] Add visible links to these routes from both the public and AislePilot shells.

### Metadata and system pages

- [ ] Give AislePilot its own professional title, canonical URL, Open Graph fields, Twitter card, product image, favicon, and site name.
- [ ] Add AislePilot and all new trust routes to the sitemap with intentional index/noindex decisions.
- [ ] Build branded 404, 429, and 500 pages with a clear explanation, retry or navigation action, and no development instructions.
- [ ] Ensure production errors never expose environment guidance, stack information, request internals, or dead-end copy.
- [ ] Verify browser icons, social preview images, and theme colours on common platforms.

### Phase 2 acceptance

- [ ] A new visitor can reach every trust route from either shell.
- [ ] AislePilot no longer presents itself as “MyBlog.”
- [ ] 404, 429, and 500 screenshots look intentional in both themes and provide a recovery path.
- [ ] Global shell targets and focus behavior pass browser tests.

---

## Phase 3 — Homepage and positioning

### Message hierarchy

- [ ] Choose the homepage's single primary goal: reviewing selected work. Make that the only primary hero button.
- [ ] Reduce the hero action set to one primary and no more than two visibly secondary routes.
- [ ] Rewrite the headline to lead with the outcome Mark delivers, then support it with the stack and reliability specialism.
- [ ] Replace generic claims with one compact proof row: shipped products, tested workflows, production operations, or another verifiable outcome.
- [ ] Keep personal warmth, but move implementation detail lower into proof sections.

### Layout and proof

- [ ] Reduce the mobile hero height so the primary action and first proof point fit comfortably in the first viewport.
- [ ] Decide whether the portrait or delivery proof should appear first on mobile based on the primary audience; test both orders before locking the layout.
- [ ] Replace the rotating delivery-highlight carousel with static current-focus content or make its controls, pause behavior, and status fully accessible.
- [ ] Consolidate “How I work” and “Where to start” where their card content overlaps.
- [ ] Make Featured Writing cards fully clickable with clear hover/focus states and consistent metadata.
- [ ] Use explicit outcome labels instead of ambiguous counters such as a bare “20.”

### Phase 3 acceptance

- [ ] Five-second user review consistently identifies who the site is for, what Mark delivers, and the primary next action.
- [ ] The first mobile viewport contains the value proposition, primary CTA, and at least one proof point.
- [ ] The page has one visual primary action per section.

---

## Phase 4 — Projects and case studies

### Project index

- [ ] Lead each project card with problem, shipped outcome, and proof rather than implementation status.
- [ ] Replace “Working build” with a public-facing state such as “Live beta,” “Case study,” or “Shipped,” backed by a consistent definition.
- [ ] Move internal “Next” milestones out of the primary card surface or reframe them as a restrained roadmap note inside detail pages.
- [ ] Keep observability links as technical proof inside a case study instead of a competing card CTA.
- [ ] Add a strong screenshot or product visual to each flagship card.
- [ ] Standardise card height and CTA placement without adding empty filler space.

### Dedicated case studies

- [ ] Give each flagship project a stable, shareable detail URL instead of relying only on inline disclosure.
- [ ] Use a consistent sequence: context, problem, role, constraints, approach, shipped experience, evidence, tradeoffs, results, next step.
- [ ] Include annotated desktop/mobile screenshots and concise captions.
- [ ] Separate product outcomes from engineering implementation details.
- [ ] Add measurable evidence where available: latency, test coverage, reliability behavior, user-flow completion, or launch status.
- [ ] Provide a clear next route at the end: try product, read related article, view code/proof, or contact.

### Phase 4 acceptance

- [ ] Every project communicates a concrete outcome before technical scope.
- [ ] Each flagship case study can be linked directly and understood without expanding hidden content.
- [ ] Screenshots have useful alt text and remain legible at mobile widths.

---

## Phase 5 — Articles and reading journey

### Index and discovery

- [ ] Add a visible label for search and topic filtering; do not rely on placeholder or `aria-label` alone.
- [ ] Make filter application, reset, and empty-result recovery at least 44px and visually obvious.
- [ ] Keep Featured Articles visually distinct, then paginate or progressively reveal the remaining feed.
- [ ] Reduce repeated tags and metadata on mobile to the information needed to choose an article.
- [ ] Make the full article card clickable while preserving semantic title links and like-button separation.
- [ ] Increase like controls to 44px, clarify their selected state without colour alone, and avoid making them the dominant repeated control.
- [ ] Decide whether public like counts add useful trust; hide zero counts if they create noise without value.
- [ ] Add result count and active-filter chips that can be removed directly.

### Article page

- [ ] Verify a 60–75 character desktop reading measure and comfortable mobile line length.
- [ ] Add a concise article header with date, reading time, topic, and optional updated date.
- [ ] Add a table of contents only for genuinely long posts.
- [ ] Standardise code blocks, callouts, tables, images, captions, and heading spacing.
- [ ] Add previous/next or related-article navigation after the article.
- [ ] Keep subscribe and comments subordinate to the article conclusion.
- [ ] Verify copy, code, tables, and embeds at 200% and 400% zoom.

### Phase 5 acceptance

- [ ] A mobile user can identify and open a relevant article without scanning the entire archive.
- [ ] Search, filter, like, pagination/reveal, and empty-result journeys pass keyboard and browser tests.
- [ ] Article pages meet the agreed reading measure and zoom requirements.

---

## Phase 6 — Contact and conversion

### Contact hierarchy

- [ ] Keep one primary contact method at the top and present LinkedIn/GitHub as secondary profile links.
- [ ] Remove the duplicated contact-link block or use it only when it adds information not already present in the CTA row.
- [ ] Add expected response time and the types of enquiry that are welcome.
- [ ] Add a short privacy note beside the form submission action.

### Form behavior

- [ ] Render validation messages directly below Name, Email, and Message with stable IDs and `aria-describedby`.
- [ ] Add a linked error summary when multiple fields are invalid.
- [ ] Preserve entered values after server, SMTP, timeout, or rate-limit failure.
- [ ] Add immediate button pending state, prevent duplicate submission, and announce success/failure through an appropriate live region.
- [ ] Give every input and the submit button a minimum 44px height.
- [ ] Add autocomplete attributes and appropriate input modes.
- [ ] Provide a direct-email fallback when form delivery is unavailable.
- [ ] Move focus to the confirmation heading after success and to the first invalid field after validation failure.

### Phase 6 acceptance

- [ ] Success, validation, SMTP failure, timeout, and 429 journeys have browser coverage.
- [ ] No form failure clears valid user input.
- [ ] The complete form works keyboard-only and with a screen reader.

---

## Phase 7 — AislePilot entry and setup

### Product trust and positioning

- [ ] Replace prototype-oriented copy with a concise product promise and public beta/status label.
- [ ] Add a compact “How it works” explanation: choose preferences, review meals, shop by aisle.
- [ ] Explain that prices are estimates, dietary selections do not replace label checking, and generated suggestions should be reviewed.
- [ ] Explain where setup, saved meals, and saved weeks are stored and provide a clear/reset-data action.
- [ ] Add an example plan or product screenshot before asking an uncertain visitor to begin.
- [ ] Ensure the logo, icon, favicon, and app naming are consistent across header, social cards, export files, and browser title.

### Setup hierarchy

- [ ] Reduce repeated hero pills and setup instructions; keep one benefit line and one primary “Start planning” action.
- [ ] Make the three-step indicator describe real progress and current position, or restyle it as a non-interactive overview if it is not navigable.
- [ ] Bring the first essential choice closer to the top on mobile.
- [ ] Keep the four core decisions expanded and optional choices collapsed by default.
- [ ] Collapse or reposition the live “Ready to generate” summary on mobile so it supports rather than doubles the form.
- [ ] Keep one sticky or consistently placed Generate action only if it does not obscure content or duplicate an equally prominent action.
- [ ] Make “Plan my week” and “Use my ingredients” selection states obvious by icon, border, label, and checked state—not colour alone.
- [ ] Clarify slider endpoints and current values without relying on the thumb label alone.

### First-run and returning experience

- [ ] Add a first-run example/default explanation without requiring a tutorial modal.
- [ ] Make restored preferences explicit and provide “Use defaults” and “Clear saved settings” actions.
- [ ] Preserve setup state when moving back from results.
- [ ] Ensure validation opens only the relevant disclosures and keeps the user's context.

### Phase 7 acceptance

- [ ] A first-time mobile user reaches the first core decision without scrolling through repeated value copy.
- [ ] The four-decision default journey remains executable without opening optional sections.
- [ ] Trust, estimate, dietary, and local-storage explanations are reachable without leaving the setup journey unexpectedly.

---

## Phase 8 — AislePilot results

### Information hierarchy

- [ ] Move the compact plan summary and Meals/Shop/Export navigation directly beneath the result hero.
- [ ] Show the first meal before detailed supermarket research and pricing provenance on mobile.
- [ ] Reduce Weekly Status to the few facts needed for a decision: spend estimate, budget difference, days, meals, and main recovery action.
- [ ] Move layout source, pricing source, confidence, and checked date into a “How estimates work” disclosure.
- [ ] Keep the over-budget message clear but visually proportionate; do not let it overpower the successful plan state.
- [ ] Replace “7 day(s)” with grammatically correct singular/plural output.
- [ ] Consolidate repeated budget figures so the same value is not presented in hero chips, status cards, and sticky strips without a distinct purpose.

### Meal experience

- [ ] Ensure day and meal-type controls are at least 44px and follow complete tab keyboard behavior where tab semantics are used.
- [ ] Make the active day and meal type clear without colour alone.
- [ ] Keep View recipe, Swap, and Save as the primary meal actions with consistent loading, success, and error feedback.
- [ ] Increase More and secondary action targets to 44px while keeping their visual icon compact.
- [ ] Announce swapped meal name, saved state, and ignored state once through a polite live region.
- [ ] Restore focus to the changed meal/action after AJAX replacement.
- [ ] Review the faded adjacent desktop carousel cards; use either a crisp intentional peek or a clear multi-card layout rather than disabled-looking content.
- [ ] Keep day navigation close to the active card and avoid controls that appear visually detached from their content.

### Shopping and export

- [ ] Make Shop the clearest task transition after meals are reviewed.
- [ ] Provide useful empty, completed, copied, exported, and failed states.
- [ ] Keep aisle group headings sticky only if they do not hide items or focused controls.
- [ ] Explain export formats in user terms and show file preparation progress for waits over 300ms.
- [ ] Preserve selected panel, day, scroll, and focus after recoverable actions.

### Failure and rate-limit UX

- [ ] Design inline recovery for no replacement meal, generation failure, image failure, export failure, stale plan, and 429 responses.
- [ ] State what happened, what data remains safe, when retry is sensible, and the best alternative action.
- [ ] Never silently fall back to navigation or reload without explaining the outcome.
- [ ] Ensure repeated action buttons remain disabled only while work is genuinely pending.

### Phase 8 acceptance

- [ ] On 390px mobile, the first meal appears immediately after compact summary/navigation and before provenance detail.
- [ ] All visible result controls meet target size and spacing requirements.
- [ ] Meals, shopping, exports, swap, save, ignore, reorder, over-budget, and failure journeys pass browser coverage.

---

## Phase 9 — Accessibility and inclusive QA

### Automated

- [ ] Run axe on every route/state in the Phase 0 matrix in light and dark themes.
- [ ] Enforce 4.5:1 normal-text contrast, 3:1 large-text/component contrast, and visible focus contrast.
- [ ] Verify one logical heading structure and one main landmark per page.
- [ ] Verify every meaningful image has useful alt text and decorative images use empty alt text.
- [ ] Verify labels, names, roles, values, expanded states, selected states, busy states, and live regions.

### Manual journeys

- [ ] Complete global navigation, project discovery, article filtering/reading, contact, and AislePilot keyboard-only.
- [ ] Test screen-reader order and announcements on desktop and mobile.
- [ ] Test 200% and 400% zoom without clipped or overlapping content.
- [ ] Test Windows High Contrast/forced-colours mode.
- [ ] Test reduced motion and confirm no essential status relies on animation.
- [ ] Test 360px portrait and narrow landscape.
- [ ] Test touch spacing around dense article and AislePilot control groups.
- [ ] Verify modals, menus, disclosures, and sheets have predictable Escape and focus behavior.

### Phase 9 acceptance

- [ ] No serious or critical axe violations remain.
- [ ] Every primary journey completes without a pointer.
- [ ] Focus is always visible and returns logically after dynamic UI changes.
- [ ] Both themes and forced-colours mode communicate state without colour alone.

---

## Phase 10 — Public release validation

### Quality matrix

- [ ] Verify latest stable Chrome, Edge, Firefox, and Safari/WebKit.
- [ ] Verify iPhone-size Safari/WebKit and Android-size Chromium emulation.
- [ ] Verify light, dark, reduced-motion, forced-colours, 200% zoom, and slow-network states.
- [ ] Run full tests, public visual tests, AislePilot E2E, axe, Lighthouse, source-size, and asset-budget checks.
- [ ] Verify canonical URLs, social cards, sitemap, robots, RSS, favicon, 404, 429, and 500 on the deployed host.
- [ ] Verify contact email, subscription confirmation, comments, likes, AislePilot save state, exports, and data-reset flows in production-like configuration.

### User review

- [ ] Run five first-impression reviews: identify purpose, credibility, and next action within five seconds.
- [ ] Run three portfolio journeys: find strongest project evidence and a relevant article.
- [ ] Run five AislePilot first-time journeys from setup through shopping or export.
- [ ] Record confusion points, task completion, time to first plan, and confidence comments.
- [ ] Fix repeated severity-one/two findings and re-run affected journeys.

### Release decision

- [ ] Publish a concise beta/support statement for any known limitation that remains.
- [ ] Define visual, accessibility, error-rate, and task-completion rollback thresholds.
- [ ] Confirm privacy, retention, telemetry, and third-party-service claims match actual production behavior.
- [ ] Remove obsolete styles, scripts, components, flags, and temporary audit exclusions.
- [ ] Mark this plan complete only after deployed-host verification.

### Phase 10 acceptance

- [ ] All automated and manual release gates pass.
- [ ] No P0 or P1 finding remains open without an explicit owner, public disclosure, and approved follow-up date.
- [ ] The deployed site and AislePilot feel like one intentional product family.

---

## Definition of done for every implementation item

- [ ] The smallest responsible view, stylesheet, script, or controller module was changed.
- [ ] Existing unrelated worktree changes were preserved.
- [ ] Behavior and content changes were documented.
- [ ] The narrowest appropriate integration or Playwright regression was added or updated.
- [ ] Light, dark, mobile, desktop, keyboard, and reduced-motion impact was reviewed where relevant.
- [ ] A before/after screenshot or objective measurement was captured for visual work.
- [ ] `powershell -NoProfile -ExecutionPolicy Bypass -File .\run_checks.ps1 -Mode Tests` passes.
- [ ] Relevant E2E, axe, visual, Lighthouse, source-size, and asset-budget checks pass.
- [ ] The relevant checkbox, phase status, and progress log in this file were updated.

## Agent execution order

1. Complete Phase 0 before changing shared visual foundations.
2. Complete Phase 1 before independently restyling pages.
3. Deliver Phase 2 P0 trust and recovery work before public launch.
4. Work Phases 3–6 as separate page-level slices.
5. Work Phase 7 before Phase 8 so setup/result transitions share the final shell and tokens.
6. Run Phase 9 continuously, then complete the full matrix after Phase 8.
7. Use Phase 10 as the deployed-host release gate.

For each slice, update tests and this checklist in the same change. Do not tick an item based on visual inspection alone when an automated contract is practical.

## Recommended code ownership

- Shared shell, tokens, navigation, footer, controls: `Views/Shared/_Layout.cshtml`, `wwwroot/css/site.css`, `wwwroot/js/site.js`.
- AislePilot shell and metadata: `Views/Shared/_AislePilotLayout.cshtml` and AislePilot head partials.
- Homepage: `Views/Home/Index.cshtml` plus shared site CSS.
- Projects/case studies: `Views/Projects/Index.cshtml`, `Views/Shared/_CaseStudyTemplate.cshtml`, and project view models.
- Articles: `Views/Blog/Index.cshtml`, `Views/Blog/Post.cshtml`, `wwwroot/css/blogs.css`, and blog interaction scripts.
- Contact: `Views/Contact/Index.cshtml`, `Controllers/ContactController.cs`, contact model, and scoped CSS.
- AislePilot setup/results: split Razor partials plus the matching `aisle-pilot-*.css` and `aisle-pilot/*.js` responsibility files.
- System/trust routes: shared error views, status-code routing, SEO controller, and new focused public controllers/views.
- Browser evidence: `MyBlog.Tests/PlaywrightE2ETests*.cs`; rendered contracts: `AislePilotIntegrationTests*.cs` or the matching public integration tests.

## Progress log

- 2026-08-23 — Started Phase 0 with a public-route Playwright baseline suite. Home, Projects, Articles, one live article, About, Contact, AislePilot setup, 404, and 500 are captured at 390 × 844 and 1440 × 900 in light/dark with reduced motion. Each screenshot has a JSON audit containing H1/main counts, viewport/document width, visible interactive count, sub-44px targets, close-target pairs, and fixed/sticky elements. Normal pages now reject horizontal overflow, missing primary structure, and increases above the explicitly recorded small-target debt.
- 2026-08-23 — Completed the planned public-route viewport and motion matrix. The audit now covers 360 × 800, 390 × 844, 768 × 1024, 1440 × 900, and 1440 × 1100; every size runs light and dark themes with normal and reduced-motion preferences. The full five-case browser run passed with no geometry or structural regressions.
- 2026-08-23 — Added Deque axe-core 4.13.0 browser scanning and a release gate for serious/critical findings at mobile and desktop widths in both themes. The initial scan exposed invalid homepage carousel ARIA semantics and non-keyboard-accessible overflowing article code blocks; both were fixed. The gate passes with two narrowly scoped framework-404 exceptions owned by Phase 2 and expiring 2026-09-30.
- 2026-08-23 — Added 24 deterministic AislePilot workflow captures covering setup, server validation, the production loading shell, generated results, shopping, and exports at mobile/desktop widths in light/dark. The journey uses a real AI-disabled fallback plan, isolates validation cookies from the normal results path, and passed both viewport cases. Empty-data, rate-limit, and dependency-failure captures remain in Phase 0.
- 2026-08-23 — Added saved-week/saved-meal empty-state captures and real rate-limit captures. Visual review exposed that browser rate limiting replaced AislePilot with raw plain text, so HTML form rejections now redirect to a branded, actionable AislePilot alert while preserving a final 429 status; AJAX callers retain the JSON 429 contract. The rate threshold is configurable for deterministic verification. Also changed the Playwright local host to run the compiled app with explicit content/web roots, allowing isolated-output E2E runs to launch reliably. Four combined workflow/rate-limit cases pass.
- 2026-08-23 — Created the public UI/UX readiness plan after desktop/mobile review of six public routes plus generated AislePilot results. Confirmed the existing visual foundation, responsive stacking, headings, metadata on the public shell, and progressive AislePilot setup. Prioritised touch targets, production recovery, privacy/data-use transparency, AislePilot identity, results hierarchy, article scanability, and cross-site design consistency for the public-launch sequence.
