# BATCH-20 Report — TRC-P4-008 OfflineViewer

**Batch:** BATCH-20  
**Task:** TRC-P4-008 — OfflineViewer  
**Status:** ✅ Completed  
**Date:** 2025-05-20

---

## Summary

Implemented the `Tracer.OfflineViewer` project — a standalone ASP.NET Core minimal-API application that serves the existing WebApi query endpoints against a DuckDB bundle file in offline mode. Also added the corresponding Vue 3 composable (`useBundleMode`) and view (`BundleOpenView`) to the frontend.

---

## Files Created / Modified

### Backend — New Files

| File | Description |
|------|-------------|
| `src/Tracer.OfflineViewer/Tracer.OfflineViewer.csproj` | New project, `OutputType=Exe`, references Observer/WebApi/Bundle/DuckDB.MultiInterval/Serilog |
| `src/Tracer.OfflineViewer/OfflineViewerConfig.cs` | POCO: `HttpPort`, `LogFilePath`, `InitialBundlePath` |
| `src/Tracer.OfflineViewer/Lifecycle/InertObserverStateReporter.cs` | No-op subclass of `ObserverStateReporter` for offline mode |
| `src/Tracer.OfflineViewer/Lifecycle/BundleOpenManager.cs` | Opens/closes bundles; drives `ReadOnlyConnectionPool`; handles zip extraction |
| `src/Tracer.OfflineViewer/Lifecycle/OfflineHostedService.cs` | `IHostedService` — opens `InitialBundlePath` on startup |
| `src/Tracer.OfflineViewer/WebApi/BundleOpenDtos.cs` | `OpenBundleRequestDto`, `OpenBundleResponseDto`, `CurrentBundleDto`, `CurrentBundleTimeRange` |
| `src/Tracer.OfflineViewer/WebApi/BundleOpenEndpoints.cs` | Minimal API: `POST /api/bundle/open`, `POST /api/bundle/close`, `GET /api/bundle/current` |
| `src/Tracer.OfflineViewer/Browser/BrowserLauncher.cs` | Best-effort browser open via `Process.Start(UseShellExecute=true)` |
| `src/Tracer.OfflineViewer/OfflineViewerHostBuilder.cs` | Builds `WebApplication`: Kestrel on dynamic port 5400–5499, NSwag, Serilog, all WebApi endpoints |
| `src/Tracer.OfflineViewer/Program.cs` | Entry point — calls Build, logs path, opens browser |

### Backend — Modified Files

| File | Change |
|------|--------|
| `src/Tracer.Observer/Lifecycle/ObserverStateReporter.cs` | Removed `sealed` to allow `InertObserverStateReporter` subclass |
| `src/Tracer.Adapters.Mock/Scenarios/Scripts/CalmScenario.cs` | Added `sessionId` (trace ID as hex) to `session_start` payload so `SessionQueryService` can discover sessions from bundle |

### Tests — New / Modified Files

| File | Change |
|------|--------|
| `tests/Tracer.Tests.Integration/OfflineViewerSmokeTests.cs` | Two smoke tests: startup+bundle-serve, clean shutdown |
| `tests/Tracer.Tests.Integration/Tracer.Tests.Integration.csproj` | Added `ProjectReference` to `Tracer.OfflineViewer` |

### Frontend — New / Modified Files

| File | Change |
|------|--------|
| `tracer-viewer/src/api/tracerApiClient.ts` | Added `CurrentBundleDto`, `OpenBundleRequestDto`, `OpenBundleResponseDto` interfaces; added `getCurrentBundle()`, `openBundle()`, `closeBundle()` methods |
| `tracer-viewer/src/composables/useBundleMode.ts` | New composable — detects live/bundle/no-bundle mode via `/api/bundle/current` |
| `tracer-viewer/src/views/BundleOpenView.vue` | New view — path input + Open button, navigates to sessions on success |
| `tracer-viewer/tests/unit/useBundleMode.spec.ts` | 3 unit tests: throws→live, present→bundle, null→no-bundle |

---

## Test Results

### .NET Tests

```
Passed!  - Failed: 0, Passed: 56, Skipped: 0, Total: 56  (Integration)
Passed!  - Failed: 0, Passed: 254, Skipped: 0, Total: 254  (Unit)
Total: 310 tests, 0 failures
```

New integration tests:
- `OfflineViewer_StartsAndServesBundle` ✅ — starts viewer, polls `/api/bundle/current`, asserts sessions returned
- `OfflineViewer_ExitsCleanlyOnSigint` ✅ — start/stop lifecycle without exception

### Frontend Tests

```
Test Files  10 passed (10)
Tests       42 passed (42)
```

New tests: `useBundleMode` — 3 tests (live/bundle/no-bundle mode detection), all ✅

---

## Design Decisions & Deviations

1. **`sessionId` missing from `CalmScenario`** — The `session_start` event payload lacked `sessionId`, which `SessionQueryService.ListAsync` queries for. Added `sessionId` derived from the event's trace ID (`traceId.Value.ToString("x16")`). This is deterministic (seed-based) and doesn't break any existing tests.

2. **`ObserverStateReporter` sealed removal** — The class was `sealed`; removed `sealed` to allow `InertObserverStateReporter`. All existing tests still pass. The class is internal to the assembly so no external API contract changes.

3. **`useBundleMode` composable uses local `ref`** — `mode` is declared inside the function (not module-level) to prevent state pollution between tests that use `vi.resetModules()` / dynamic import.

4. **NSwag pattern confirmed** — `OfflineViewerHostBuilder` uses `OpenApiConfiguration.Configure(builder)` + `app.UseOpenApi()` + `app.UseSwaggerUi()` matching the Observer pattern. MS OpenAPI (`AddOpenApi`) is not used.

5. **Port selection** — `FindFreePort(5400, 5499)` iterates TCP sockets to find a free port. Falls back to 5400 on exception.

---

## Issues Encountered

- **CA1062 warnings-as-errors**: `TreatWarningsAsErrors=true` in the new project triggered CA1062 for all public methods. Added `ArgumentNullException.ThrowIfNull()` to all relevant parameters (`mgr`, `request`, `args`).

- **`BundleFixture` API**: The integration test spec said `new BundleFixture()` + `await fixture.InitializeAsync()` but the actual implementation has a static `BundleFixture.InitializeAsync()`. Used the correct static pattern.

- **Sessions empty in smoke test**: `CalmScenario` didn't include `sessionId` in the `session_start` payload. Root cause was discovered by tracing `SessionQueryService.ListAsync` SQL — it filters `WHERE json_extract_string(payload, '$.sessionId') IS NOT NULL`. Fixed by adding `sessionId` to the payload (see above).

---

## Known Issues / Tech Debt

- `BundleOpenView.vue` is minimal — no file browser dialog integration (relies on typed path). A file-picker dialog would improve UX.
- `BrowserLauncher.cs` uses `UseShellExecute=true` which works on Windows but may need `xdg-open` on Linux. Low priority for now.
- The `wwwroot` static file serving warning in tests ("WebRootPath was not found") is expected during integration tests where the Vue build artifacts are not present; offline viewer serves static files from the published output in production.
