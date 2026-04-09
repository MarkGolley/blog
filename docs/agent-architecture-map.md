# Agent-Friendly Architecture Map

## Module map

### Web app (`MyBlog/`)
- `Controllers/`: HTTP entrypoints and request/response shaping.
- `Models/`: view models and request models for MVC pages.
- `Services/`: blog/comment/subscription/moderation support services.
- `Startup/`: startup extension modules for service registration, rate limiting, and middleware pipeline.
- `Views/`: Razor UI.
- `wwwroot/`: static assets (app assets in `css/`, `js/`, `images/`; vendor libs in `lib/`).
- `Program.cs`: composition root and middleware pipeline.

### AislePilot domain (`MyBlog.AislePilot/`)
- `Services/AislePilotService*.cs`: planning engine split by responsibility:
  - `AislePilotService.cs`: core wiring, capability checks, pantry entrypoints, normalization.
  - `AislePilotService.PlanGeneration.cs`: plan build/swap/rebalance orchestration.
  - `AislePilotService.MealSelection.cs`: meal selection, scoring, pantry ranking, shopping builders.
  - `AislePilotService.AiPrompting.cs`: AI prompt construction, payload normalization/validation, retry plumbing.
  - `AislePilotService.Images.cs`: meal image resolution/storage/recovery/background generation.
  - `AislePilotService.SupermarketLayouts.cs`: supermarket aisle-order discovery/caching.
  - `AislePilotService.Leftovers.cs`: leftover/cook-day distribution helpers.
  - `AislePilotService.Types.cs`: nested records/data contracts/firestore DTOs.
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
- New plan-generation rules or swap/rebalance logic: `AislePilotService.PlanGeneration.cs` or `AislePilotService.MealSelection.cs`.
- New AI prompt or payload-recovery logic: `AislePilotService.AiPrompting.cs`.
- New meal image logic: `AislePilotService.Images.cs`.
- New supermarket aisle discovery behavior: `AislePilotService.SupermarketLayouts.cs`.
- New AislePilot exports: `AislePilotExportService.cs`.
- UI-only AislePilot behavior: Razor views and `wwwroot/js/aisle-pilot*`.

## Where to add tests
- Logic-level: `MyBlog.Tests/*ServiceTests*.cs` (prefer narrow unit coverage).
- Server-rendered/HTTP behavior: `MyBlog.Tests/AislePilotIntegrationTests*.cs`.
- UX/layout/interaction regressions: `MyBlog.Tests/PlaywrightE2ETests*.cs`.

## Hotspots and responsibility ownership
- `MyBlog.AislePilot/Services/AislePilotService*.cs`
  - Ownership: AislePilot planning + AI integration layer.
  - Touch for: meal selection, budgeting, pantry suggestions, image generation, layout discovery.
- `MyBlog/Controllers/AislePilotController*.cs`
  - Ownership: AislePilot web orchestration and persisted request/session state.
  - Touch for: input validation flow, cookie-backed saved state, action routing.
- `MyBlog.Tests/AislePilotServiceTests*.cs`
  - Ownership: deterministic planning engine regressions.
- `MyBlog.Tests/AislePilotIntegrationTests*.cs`
  - Ownership: end-to-end server-rendered contract coverage for AislePilot routes.
- `MyBlog.Tests/PlaywrightE2ETests*.cs`
  - Ownership: browser-level UX and responsive behavior.
- `MyBlog/Program.cs`
  - Ownership: DI, middleware, rate limiting, app mode routing.
