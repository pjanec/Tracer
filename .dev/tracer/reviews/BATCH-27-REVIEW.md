# BATCH-27 Review — TRC-P5-011 & TRC-P5-012

**Tasks:** Backend Unit Tests (TRC-P5-011) + Backend Integration Tests (TRC-P5-012)
**Status:** APPROVED (with production fixes required — all applied before commit)

---

## Summary

BATCH-27 covered backend test naming/additions for Phase 5 components (multi-interval query pipeline and event endpoints) plus the new `TimelineRoundTripTests` integration test class. The sub-agent's implementation was substantially correct but exposed three real production bugs that required fixes before the tests could pass.

---

## Files Reviewed

### Unit Tests

| File | Tests | Notes |
|---|---|---|
| `tests/Tracer.Tests.Unit/MultiInterval/IntervalSetTrackerTests.cs` | 6 renamed + 1 new (`SetChanged_FiresAfterEviction`) | Good — new test verifies eviction fires `SetChanged` |
| `tests/Tracer.Tests.Unit/MultiInterval/LiveMultiIntervalReaderTests.cs` | 5 renamed | Clean renames, no behaviour change |
| `tests/Tracer.Tests.Unit/WebApi/EventQueryServiceTests.cs` | 9 renamed | Descriptive names follow `Method_Condition_Expected` |
| `tests/Tracer.Tests.Unit/WebApi/EventAggregationServiceTests.cs` | 5 renamed | OK |
| `tests/Tracer.Tests.Unit/WebApi/EventEndpointsListTests.cs` | 5 renamed | OK |
| `tests/Tracer.Tests.Unit/WebApi/EventEndpointsAggregateTests.cs` | 2 renamed + 1 new (`GetAggregate_MissingFromOrTo_Returns400ProblemDetails`) | New test exposed missing validation — production fix required |

### Integration Tests

| File | Tests | Notes |
|---|---|---|
| `tests/Tracer.Tests.Integration/LiveMultiIntervalQueryTests.cs` | 4 new tests | All pass; `LiveQuery_ResultsOrderedAcrossIntervalBoundaries` correctly uses `MakeSessionStart` |
| `tests/Tracer.Tests.Integration/TimelineRoundTripTests.cs` | 4 new tests | Complex round-trip fixture; required 3 production fixes (see below) |

---

## Production Bugs Found & Fixed

### 1. `DuckDbStorageWriter.DisposeAsync` — Missing CHECKPOINT (Critical)

**Root cause:** DuckDB WAL is only flushed to disk automatically when the *last* connection closes. When `LiveMultiIntervalReader` held READ_ONLY connections open, closing the write connection did not trigger the automatic checkpoint. The `events.duckdb` main file appeared empty to offline bundle readers.

**Fix:** Added explicit `CHECKPOINT;` command before `_connection.Dispose()` in `DuckDbStorageWriter.DisposeAsync()`.

```csharp
// Checkpoint WAL to the main file before closing so that the on-disk
// events.duckdb is fully populated even when READ_ONLY connections are open.
try
{
    await using var cmd = _connection.CreateCommand();
    cmd.CommandText = "CHECKPOINT;";
    await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
}
catch (Exception ex)
{
    _logger.LogWarning(ex, "Error checkpointing WAL during dispose.");
}
_connection.Dispose();
```

**Impact:** This is a correctness fix for all bundle export paths, not just tests.

---

### 2. `OfflineViewerHostBuilder` — Missing Service Registrations

**Root cause:** `EventQueryService` and `EventAggregationService` were registered in `ObserverHostBuilder` but not in `OfflineViewerHostBuilder`. Calling `EventEndpoints.Map(app)` without these services caused 500 errors at runtime.

**Fix:** Added both services to `OfflineViewerHostBuilder.Build`:

```csharp
builder.Services.AddSingleton<EventQueryService>();
builder.Services.AddSingleton<EventAggregationService>();
```

---

### 3. `TestCollections.cs` — Port Race Condition

**Root cause:** `TimelineRoundTrip` and `OfflineViewerSmoke` collections both call `FindFreePort(5400, 5499)` during `InitializeAsync`. xUnit runs test collections in parallel by default, causing both collections to race for the same port range, resulting in `address already in use` failures.

**Fix:** Added `DisableParallelization = true` to both collection definitions:

```csharp
[CollectionDefinition("OfflineViewerSmoke", DisableParallelization = true)]
[CollectionDefinition("TimelineRoundTrip", DisableParallelization = true)]
```

---

### 4. `EventEndpoints.cs` — Missing from/to Validation

**Root cause:** `GetAggregate_MissingFromOrTo_Returns400ProblemDetails` test (added in TRC-P5-011) called the aggregate endpoint without `from`/`to` parameters and expected a 400. The endpoint was returning 200 with empty results instead.

**Fix:** Added guard in `HandleAggregateAsync`:

```csharp
if (!from.HasValue || !to.HasValue)
    return TypedResults.Problem("'from' and 'to' query parameters are required.", statusCode: 400);
```

---

## Test Quality Assessment

**Strengths:**
- `SetChanged_FiresAfterEviction` covers an important edge case that was not previously tested
- `TimelineRoundTripTests` is a high-value integration test — it exercises the full pipeline from event ingestion through interval rotation, NAS upload, bundle build, OfflineViewer startup, and HTTP query, comparing live and bundle query results for parity
- Performance tests (`1MEvents_FirstResponseUnder500ms`, `100MEvents_CompletesUnder1s`) are appropriately marked `[Trait("Category","Performance")]` so they can be excluded from CI
- `MakeSessionStart` is correctly called first in `InitializeAsync` to satisfy session time-range lookup

**Weaknesses / Improvements Applied:**
- Sub-agent used a static 2026 base time — corrected to `DateTimeOffset.UtcNow`
- Sub-agent left diagnostic `Console.WriteLine` calls — removed
- Sub-agent used a CS0136-conflicting variable name (`manifest` in both a foreach and outer scope) — renamed inner variable to `ivManifest`

---

## Test Results

| Suite | Passed | Failed |
|---|---|---|
| Unit (326 total) | 326 | 0 |
| Integration (77 total, excl. PDB-lock) | 77 | 0 |

---

## Verdict

APPROVED. All 4 production bugs found by the tests are fixed. Test suite is green. Commit proceeds.
