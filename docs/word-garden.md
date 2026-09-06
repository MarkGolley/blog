# WordGarden

WordGarden is integrated into MyBlog at `/projects/word-garden`, linked from Projects and included in the sitemap. It works in Combined and BlogOnly modes. It is intentionally unavailable on the AislePilotOnly service.

## Features

- Twelve starter words with meanings, example sentences and memory cues.
- Reveal-and-rate flashcards. Again returns in one minute; Remembered begins at one day and doubles the prior interval; Easy begins at three days and triples it. Intervals cap at 365 days.
- A shuffled matching round with up to six word/meaning pairs and attempt feedback. This practice does not alter scheduled reviews.
- Add, search and remove personal words. The collection supports up to 2,000 entries.
- Export/import JSON backups, including review dates. Import validates the complete file before asking to replace the collection.
- Browser-local progress, empty/error states, keyboard controls, and light/dark responsive layouts.

Progress belongs to this browser and origin; it does not sync between devices. Clearing site data removes it. Visitors should export backups. No new database, login, API key or service is required. The rest of the blog retains its existing runtime requirements.

## Preview

From `C:\Users\markg\RiderProjects\MyBlog`:

```powershell
dotnet run --project .\MyBlog
```

Open `http://localhost:8080/projects/word-garden`.

## Deploy

The existing Dockerfile includes the controller, Razor page and static assets automatically. No deployment configuration changes are required.

Review the current checkout before deploying: the existing pipeline publishes all current source changes. Unrelated AislePilot changes appeared during this task and were left untouched.

Use the repository's paired deployment workflow, which keeps the blog and AislePilot on the same image:

```powershell
Set-Location C:\Users\markg\RiderProjects\MyBlog
.\Deployment\deploy-all.ps1 -EnvironmentName Production
```

Keep the default pre-deploy checks enabled. The script builds and publishes to Google Cloud and changes the live services. For staging first, use `-EnvironmentName Staging`.

After deployment, open `https://markgolley.dev/projects/word-garden`. Confirm Projects links to it, reveal/rate a word, refresh to check progress, and add/export a personal word. Check on a phone as well. For rollback, use Cloud Run's revision traffic controls to return both services to their previously working revisions; retain the existing paired-service version policy.

## Verification performed

- 515 non-E2E .NET tests passed, including two new WordGarden route/asset/discovery checks covering Combined and BlogOnly modes.
- Four Node tests passed for exact scheduling, immutable updates, backup round trips, malformed data rejection and shuffle preservation.
- Headless Edge browser checks passed for review/reload persistence, adding/searching/removing, export/import, invalid import preservation, complete matching rounds, empty/corrupt storage, and mobile overflow.
- Axe checks found no violations within the app in the checked desktop, matching, mobile answer, dark mode and library states. Desktop/mobile screenshots were inspected.
- Release `dotnet publish` and the existing Docker image build succeeded.
- Docker was running, and the active gcloud login successfully described the existing Cloud Run blog service.
- Source size checks passed. Production was not deployed by this task. Cloud permissions for an actual deployment are still exercised by the deployment command.

## Repeat app-specific checks

```powershell
dotnet test .\MyBlog.Tests\MyBlog.Tests.csproj --filter FullyQualifiedName~WordGardenIntegrationTests
node --test .\scripts\word-garden.test.mjs
```

Browser checks use an installed Playwright package and a running local app. Set `PLAYWRIGHT_MODULE` to the Playwright package directory if it is not otherwise resolvable, `PLAYWRIGHT_CHANNEL=msedge` to use installed Edge, `WORD_GARDEN_BASE_URL` to your local origin (defaults to `http://localhost:5187`), `WORD_GARDEN_EVIDENCE` for screenshots, and optionally `AXE_SCRIPT` to `axe-core/axe.min.js`. Then run `node scripts/word-garden.browser.cjs`.

Implementation is isolated in `WordGardenController`, `Views/WordGarden`, `word-garden.css`, `word-garden.js` and `word-garden-core.mjs`, with small Projects/sitemap additions.
