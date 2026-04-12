# Agent-Friendly Architecture Map

## Module map

### Web app (`MyBlog/`)
- `Controllers/`: HTTP entrypoints and request/response shaping.
  - `AislePilotController.cs`: plan-generation, swap, save/open week, and other main request flows.
  - `AislePilotController.Validation.cs`: page-model assembly plus shared request validation.
  - `AislePilotController.Exports.cs`: export endpoints and meal-image URL shaping.
  - `AislePilotController.Pantry.cs`: pantry suggestion flow, suggestion history, saved-meal state helpers.
  - `AislePilotController.State.SavedWeeks.cs`: saved-week snapshots, summary shaping, and saved-week label/request normalization.
  - `AislePilotController.State.RequestState.cs`: request normalization, meal-slot selection normalization, swap/ignore state parsing, and return-url/current-plan resolution.
  - `AislePilotController.State.Cookies.cs`: setup-plan/current-plan cookie serialization and hydration.
  - `AislePilotController.RequestComparison.cs`: logic that decides whether current meals can be recalculated instead of fully regenerated.
  - `AislePilotController.CookieModels.cs`: cookie DTOs only; do not add controller behavior here.
- `Models/`: view models and request models for MVC pages.
- `Services/`: blog/comment/subscription/moderation support services.
- `Startup/`: startup extension modules for service registration, rate limiting, and middleware pipeline.
- `Views/`: Razor UI.
- `wwwroot/`: static assets (app assets in `css/`, `js/`, `images/`; vendor libs in `lib/`).
- `Program.cs`: composition root and middleware pipeline.

### AislePilot domain (`MyBlog.AislePilot/`)
- `Services/AislePilotService*.cs`: planning engine split by responsibility:
  - `AislePilotService.cs`: composition root, static reference data, caches, and service wiring.
  - `AislePilotService.CatalogAndPantry.cs`: supported-option accessors, compatibility checks, dessert/warmup hydration, pantry suggestion orchestration, and request normalization helpers.
  - `AislePilotService.PlanGeneration.cs`: primary plan-generation fallback and AI/pool orchestration.
  - `AislePilotService.PlanGeneration.Assembly.cs`: dessert add-on resolution, plan context creation, cloning, and final plan assembly.
  - `AislePilotService.MealSelection.cs`: core meal selection, filtering, AI-pool compatibility, and Firestore meal mapping.
  - `AislePilotService.MealSelection.Shopping.cs`: daily-card shaping plus shopping-list/dessert shopping helpers.
  - `AislePilotService.MealSelection.TypesAndNormalization.cs`: plan day/cook day/meal type normalization and slot suitability helpers.
  - `AislePilotService.AiPrompting.cs`: AI prompt construction only.
  - `AislePilotService.AiPrompting.Validation.cs`: AI meal/ingredient/nutrition validation and mapping.
  - `AislePilotService.AiPrompting.JsonRecovery.cs`: JSON parsing, malformed-payload recovery, and normalization helpers.
  - `AislePilotService.AiPrompting.Retry.cs`: HTTP retry and low-level request helpers.
  - `AislePilotService.Images.cs`: image lookup/cache hydration and cached/bundled image resolution.
  - `AislePilotService.Images.Persistence.cs`: disk restore/read, Firestore chunk persistence, and image URL normalization.
  - `AislePilotService.Images.BackgroundGeneration.cs`: background image generation, special-treat generation, and dessert recovery queues.
  - `AislePilotService.SupermarketLayouts.cs`: supermarket aisle-order discovery/caching.
  - `AislePilotService.Leftovers.cs`: leftover/cook-day distribution helpers.
  - `AislePilotService.Types.cs`: nested records/data contracts/firestore DTOs.
  - `AislePilotService.PlanGeneration.Budget.cs`: targeted lower-cost rebalance attempts and shared cost helpers.
  - `AislePilotService.PlanGeneration.Budget.Floor.cs`: lowest-cost fallback plan selection plus rebalance status/rebase helpers.
- `Services/AislePilotSlotSelectionEngine.cs`, `AislePilotPantryRankingEngine.cs`, `AislePilotNutritionRecipeFallbackEngine.cs`: DI seams around `AislePilotService` logic. Touch these only when you need a separate injectable boundary; most behavior changes still belong in the matching `AislePilotService.*` partial.
- `Services/AislePilotPlanGenerationOrchestrator.cs`: injected orchestration for top-level plan generation flow.
- `Services/AislePilotPlanComparisonService.cs`: injected meal-sequence comparison and changed-day analysis.
- `Services/AislePilotExportService.cs`: export formats (plan pack/checklist).
- `Models/`: AislePilot-specific request/view models.

### Test project (`MyBlog.Tests/`)
- Service tests: `AislePilotServiceTests*.cs`, `*ServiceTests.cs`.
- Integration tests: `AislePilotIntegrationTests*.cs`.
- Browser E2E tests: `PlaywrightE2ETests*.cs`.
- Shared host fixture: `TestWebApplicationFactory.cs`.

## Dependency boundaries
- Controllers depend on service interfaces (`IAislePilotService`, `IAislePilotExportService`) and models; avoid embedding domain selection logic in controllers.
- `MyBlog` references `MyBlog.AislePilot`; `MyBlog.AislePilot` does not reference `MyBlog`.
- `MyBlog.Tests` is the only project that should reach both app projects for verification.
- Keep Firestore/OpenAI IO in service-layer methods; helper methods should stay deterministic when possible.

## Where to add changes
- New AislePilot HTTP action/endpoint behavior: `MyBlog/Controllers/AislePilotController*.cs`.
  - Validation or page-model composition: `AislePilotController.Validation.cs`.
  - Saved-week snapshot behavior: `AislePilotController.State.SavedWeeks.cs`.
  - Cookie/session persistence: `AislePilotController.State.Cookies.cs`.
  - Request normalization, swap/ignore parsing, current-plan resolution: `AislePilotController.State.RequestState.cs`.
  - Pantry suggestion UX or saved-meal toggles: `AislePilotController.Pantry.cs`.
  - Export and meal-image response behavior: `AislePilotController.Exports.cs`.
- New plan-generation rules or swap/rebalance logic: `AislePilotService.PlanGeneration.cs` or `AislePilotService.MealSelection.cs`.
- Final plan assembly, dessert add-on resolution, or plan context shaping: `AislePilotService.PlanGeneration.Assembly.cs`.
- Shopping-list shaping or daily-card output assembly: `AislePilotService.MealSelection.Shopping.cs`.
- Meal-type slot normalization and day-count helpers: `AislePilotService.MealSelection.TypesAndNormalization.cs`.
- New supported-option accessors, pantry orchestration, warmup coverage, or dessert add-on pool behavior: `AislePilotService.CatalogAndPantry.cs`.
- New AI prompt text: `AislePilotService.AiPrompting.cs`.
- New AI payload validation or repair logic: `AislePilotService.AiPrompting.Validation.cs` or `AislePilotService.AiPrompting.JsonRecovery.cs`.
- New meal image lookup/hydration logic: `AislePilotService.Images.cs`.
- New meal image persistence or background generation logic: `AislePilotService.Images.Persistence.cs` or `AislePilotService.Images.BackgroundGeneration.cs`.
- New budget rebalance attempt logic: `AislePilotService.PlanGeneration.Budget.cs`.
- New lowest-cost fallback or rebalance status messaging: `AislePilotService.PlanGeneration.Budget.Floor.cs`.
- New supermarket aisle discovery behavior: `AislePilotService.SupermarketLayouts.cs`.
- New AislePilot exports: `AislePilotExportService.cs`.
- UI-only AislePilot behavior: Razor views and `wwwroot/js/aisle-pilot*`.

## Where to add tests
- Logic-level: `MyBlog.Tests/*ServiceTests*.cs` (prefer narrow unit coverage).
  - `AislePilotServiceTests.cs`: broad plan generation, shopping output, compatibility, and utility regressions.
  - `AislePilotServiceTests.PantryAndSwap.cs`: pantry suggestions and meal-swap determinism.
  - `AislePilotServiceTests.SpecialTreatAndDessert.cs`: special-treat and dessert add-on rules.
  - `AislePilotServiceTests.AiFallbackAndPool.cs`: AI fallback parsing, pool reuse, and retry/fallback behavior.
- Server-rendered/HTTP behavior: `MyBlog.Tests/AislePilotIntegrationTests*.cs`.
  - `AislePilotIntegrationTests.cs`: controller validation, export contracts, saved-week and swap endpoints.
  - `AislePilotIntegrationTests.PantryInteractions.cs`: pantry suggestion forms, strict pantry mode, history, and saved-meal interactions.
  - `AislePilotIntegrationTests.UiRendering.cs`: Razor output contracts, layout shell, CSS/JS asset contract checks, and plan card rendering.
  - `AislePilotIntegrationTests.UiRendering.SavedStateMenus.cs`: saved-week, saved-meal, and header-menu rendering contracts.
- UX/layout/interaction regressions: `MyBlog.Tests/PlaywrightE2ETests*.cs`.
  - `PlaywrightE2ETests.cs`: high-level happy paths and smoke coverage.
  - `PlaywrightE2ETests.DesktopLayouts.cs`: desktop layout and interaction expectations.
  - `PlaywrightE2ETests.MobileLayouts.cs`, `PlaywrightE2ETests.MobileDayCards.cs`: mobile layout/state regressions.

## Verification workflows
- Fastest targeted verification after AislePilot controller or view changes: `dotnet test MyBlog.Tests/MyBlog.Tests.csproj --filter FullyQualifiedName~AislePilotIntegrationTests`.
- Logic-only AislePilot changes: run the narrowest matching `AislePilotServiceTests*` filter first, then broaden if shared plan-generation code changed.
- Full automated repo test pass: `powershell -NoProfile -ExecutionPolicy Bypass -File .\run_checks.ps1 -Mode Tests`.
  - This also runs the oversized-file check and skips `AIModerationServiceLiveTests` when `OPENAI_API_KEY` is unset.
- Oversized-file guardrail only: `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\check-oversized-files.ps1`.
- Browser/E2E verification: `powershell -NoProfile -ExecutionPolicy Bypass -File .\run_checks.ps1 -Mode E2E` after building Playwright tooling. Expect browser install unless `-SkipBrowserInstall` is passed.

## Hotspots and responsibility ownership
- `MyBlog.AislePilot/Services/AislePilotService*.cs`
  - Ownership: AislePilot planning + AI integration layer.
  - Touch for: meal selection, budgeting, pantry suggestions, image generation, layout discovery, catalog metadata, and warmup/dessert pools.
- `MyBlog/Controllers/AislePilotController*.cs`
  - Ownership: AislePilot web orchestration and persisted request/session state.
  - Touch for: input validation flow, cookie-backed saved state, exports, image response shaping, and action routing.
- `MyBlog.Tests/AislePilotServiceTests*.cs`
  - Ownership: deterministic planning engine regressions.
- `MyBlog.Tests/AislePilotIntegrationTests*.cs`
  - Ownership: end-to-end server-rendered contract coverage for AislePilot routes.
- `MyBlog.Tests/PlaywrightE2ETests*.cs`
  - Ownership: browser-level UX and responsive behavior.
- `MyBlog/Program.cs`
  - Ownership: DI, middleware, rate limiting, app mode routing.
