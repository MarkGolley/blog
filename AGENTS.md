# Agent Instructions

## Testing Policy

- When fixing a bug, always add or update at least one automated test that would fail without the fix and pass with it.
- Prefer the narrowest regression test at the right level:
- `MyBlog.Tests/AislePilotIntegrationTests.cs` for server-rendered/output regressions.
- `MyBlog.Tests/PlaywrightE2ETests.cs` for interaction/layout/UX regressions.
- `MyBlog.Tests/*ServiceTests.cs` for logic-level regressions.
- If a bug cannot be reasonably covered by an automated test, explicitly document why in the final response.

## Completion Criteria

- A bug-fix task is not complete until:
- the fix is implemented,
- regression/new test coverage is added or updated,
- and the affected tests are run (or the reason they could not be run is stated).

## Working Agreement (Small-Context Edits)

- Start with `docs/agent-architecture-map.md` and edit the smallest responsible module.
- Prefer extending existing split files (`*.<Responsibility>.cs`) over enlarging a core file.
- Keep edits scoped to one behavior slice (controller flow, planner rule, image pipeline, etc.).
- Preserve behavior by default; if behavior changes, call it out explicitly and add regression coverage.
- Add tests in the narrowest layer:
  - service logic in `*ServiceTests*.cs`
  - server-rendered route/output in `AislePilotIntegrationTests*.cs`
  - interaction/layout in `PlaywrightE2ETests*.cs`
- Run `run_checks.ps1 -Mode Tests` (or equivalent targeted `dotnet test`) for touched areas.
- Run `scripts/check-oversized-files.ps1` and keep files under threshold unless explicitly allowlisted.
