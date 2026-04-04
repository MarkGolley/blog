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
