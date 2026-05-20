# BATCH-30 Report — TRC-P6-003 & TRC-P6-004

## Status: ✅ Completed (No Implementation Required — All Pre-existing)

---

## Summary

Upon inspection, all deliverables for TRC-P6-003 and TRC-P6-004 were already fully implemented
in the codebase from a prior session referenced in BATCH-29. No new files were created and no
existing files were modified.

---

## Files Verified (Pre-existing, Not Modified)

| File | Status | Notes |
|------|--------|-------|
| `src/Tracer.WebApi/Contracts/Dto/TraceDtos.cs` | Pre-existing | Contains `TraceTreeDto`, `TraceNodeDto`, `TraceEdgeDto`, `TraceSummaryDto`. Has extra `SessionId` on `TraceTreeDto` vs. spec — carries session context from `TraceQueryService`. |
| `src/Tracer.WebApi/Contracts/Mapping/TraceDtoMapper.cs` | Pre-existing | `Map(TraceTree)`, `MapNode`, `MapEdge`, `Map(TraceSummary)`. Matches spec exactly. |
| `src/Tracer.WebApi/Endpoints/TraceEndpoints.cs` | Pre-existing | All 5 routes: `/api/traces/{traceId}`, `/api/traces/{traceId}/tree`, `/api/events/{eventId}/trace`, `/api/events/{eventId}/ancestors`, `/api/events/{eventId}/descendants`. |
| `src/Tracer.Observer/ObserverHostBuilder.cs` | Pre-existing | `TraceQueryService` registered as singleton; `TraceEndpoints.Map(app)` wired. |
| `src/Tracer.OfflineViewer/OfflineViewerHostBuilder.cs` | Pre-existing | `TraceQueryService` registered; `TraceEndpoints.Map(app)` wired. |
| `src/Tracer.TestHarness/Observer/WebApiFixture.cs` | Pre-existing | `TraceQueryService` registered; `TraceEndpoints.Map(app)` wired. |
| `tests/Tracer.Tests.Unit/WebApi/TraceDtoMapperTests.cs` | Pre-existing | 5 tests matching spec exactly. |
| `tests/Tracer.Tests.Unit/WebApi/TraceEndpointsTests.cs` | Pre-existing | 9 tests (8 from spec + 1 additional `GetAncestors_10DeepChain_WalkExpandsBefore200ms`). |

---

## Deviations from Instructions

1. **`TraceTreeDto` has an extra `SessionId` property** — The existing `TraceDtos.cs` includes
   `public required string SessionId { get; init; }` on `TraceTreeDto`. This was added in the
   prior session (BATCH-29) to carry session context from `TraceQueryService.ResolveSessionId()`.
   The spec's `TraceDtoMapper` did not require it, but it is populated in `TraceDtoMapper.Map(TraceTree)`
   from `tree.SessionId`. This is an additive deviation; no spec behavior was removed.

2. **`TraceEndpointsTests` has 9 tests instead of 8** — The file contains all 8 spec tests plus
   `GetAncestors_10DeepChain_WalkExpandsBefore200ms`. This is an additive extension.

3. **`TraceDtoMapper.MapEdge` includes null check** — `ArgumentNullException.ThrowIfNull(edge)`
   added. Minor defensive addition consistent with existing mapper style.

---

## Build Output (last 5 lines)

```
  Tracer.Tests.Integration -> D:\Work\Tracer\tests\Tracer.Tests.Integration\bin\Release\net8.0\Tracer.Tests.Integration.dll
  Tracer.Tests.Unit -> D:\Work\Tracer\tests\Tracer.Tests.Unit\bin\Release\net8.0\Tracer.Tests.Unit.dll

Build succeeded.
    0 Warning(s)
    0 Error(s)
```

---

## Test Results — New Test Classes

```
Filter: FullyQualifiedName~TraceDtoMapperTests|FullyQualifiedName~TraceEndpointsTests

Passed!  - Failed: 0, Passed: 14, Skipped: 0, Total: 14, Duration: 960 ms
```

Breakdown:
- `TraceDtoMapperTests`: 5 tests — all pass
- `TraceEndpointsTests`: 9 tests — all pass

---

## Total Unit Test Count

```
Passed!  - Failed: 0, Passed: 351, Skipped: 0, Total: 351, Duration: 3 m 32 s
```

**351 unit tests, 0 failures.** (Unchanged — the 14 new tests were pre-existing and already
counted in the BATCH-29 total of 351.)

---

## Developer Insights

### Issues Encountered

None. All deliverables were already present from a prior development session that was committed
as part of BATCH-29.

### Weak Points Spotted

- The `TraceSummaryDto.TotalEventsAvailable` logic in `TraceDtoMapper.Map(TraceSummary)` sets it
  to `null` when `Truncated = false`, even if `TotalEventsAvailable` has a value in the domain
  model. This is by design (per spec) but could cause confusion if a caller sets `TotalEventsAvailable`
  on a non-truncated summary.

- `GetTraceTreeAsync(id, maxEvents: 1, ct)` in `HandleGetTraceSummaryAsync` gives an accurate
  "exists" check but produces misleading summary stats (`TotalEvents = 1`, `Truncated = true`
  when the trace has more events). The spec acknowledges this as acceptable, directing callers
  to `/tree` for full stats.

### Design Decisions Made Beyond the Spec

- `TraceTree.SessionId` (empty string default) allows `TraceDtoMapper` to pass session context
  through without requiring a separate query from the endpoint layer. The session resolution
  happens inside `TraceQueryService`, keeping endpoint handlers thin.

- Performance tests (300 ms / 200 ms SLAs) in `TraceEndpointsTests` depend on warm DuckDB
  connections. A cold first hit is warm-up via `await _fixture.Client.GetAsync(url)` before
  the timed call — matching the pattern from `SseEndpointTests`.
