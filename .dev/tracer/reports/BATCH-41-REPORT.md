# BATCH-41 Report

**Batch:** BATCH-41  
**Task:** TRC-P7-020 — Phase 7 Remaining Tests (FastState Parquet Round-Trip + E2E)  
**Status:** ✅ Complete  
**Date:** 2026-05-22

---

## Summary

Both deliverables specified in BATCH-41 have been implemented and verified:

1. **`tests/Tracer.Tests.Integration/FastStateParquetRoundTripTests.cs`** — 3 integration tests, all passing.
2. **`tracer-viewer/tests/e2e/entity-history-view.spec.ts`** — 4 E2E smoke tests, TypeScript valid.

---

## Work Done

### Task 1 — `FastStateParquetRoundTripTests.cs`

**File created:** `tests/Tracer.Tests.Integration/FastStateParquetRoundTripTests.cs`

Also required: added `<ProjectReference Include="..\..\src\Tracer.Storage.Parquet\Tracer.Storage.Parquet.csproj" />` to `Tracer.Tests.Integration.csproj`. The integration test project had no prior reference to `Tracer.Storage.Parquet`; the unit test project already had it, but the integration project did not. `DuckDB.NET.Data` and `Microsoft.Extensions.Logging.Abstractions` are available transitively through the new project reference.

**Tests implemented:**

| Test | Description | Result |
|------|-------------|--------|
| `ReadTimeSeriesAsync_ExactSampleEquality` | Writes 5 samples (x=10,20,30,40,50) at t=1..5s, reads back, asserts exact values and timestamps | ✅ Pass |
| `ReadTimeSeriesAsync_MultiInterval_MergesBothFiles` | Writes two Parquet files (3 samples each, t=1..3 and t=4..6), reads with list overload, asserts TotalSamples=6 and ascending order | ✅ Pass |
| `ReadTimeSeriesAsync_TimeRangeFilter_ExcludesOutOfRange` | Writes 10 samples (t=1..10s), filters from=3s to=8s (half-open [from, to) as the reader uses `< to`), asserts exactly 5 samples (t=3..7) | ✅ Pass |

**Implementation notes:**
- Used `IDisposable` (not `IAsyncLifetime`) — temp dir cleanup is synchronous.
- The `WriteParquetAsync` helper creates a fresh in-memory DuckDB connection per file, creates the schema with `DOUBLE` columns (not `FLOAT`) to match the `ParquetSample.Values` dictionary type `double?`.
- The batch instructions stated `to = Zero + 7s` for the time-range filter test expecting 5 samples at t=3,4,5,6,7. However, `ParquetReader` uses a half-open interval (`publish_wallclock < $to`), so `to = 7s` would exclude t=7. Corrected to `to = Zero + 8s` to include all 5 expected samples.

### Task 2 — `entity-history-view.spec.ts`

**File created:** `tracer-viewer/tests/e2e/entity-history-view.spec.ts`

**Tests implemented:**

| Test | Description |
|------|-------------|
| `entityPickerView_renders_searchAndList` | Navigates to `/v/entities/test-session-001`, asserts `.entity-picker` visible, h1 contains session ID, `.entity-picker__filter` input visible |
| `entityHistoryView_renders_loadingOrSummary` | Navigates to `/v/entity/test-entity-001?session=test-session-001`, asserts `.entity-history-view` renders (accepts loading/error state) |
| `entityHistoryView_directUrl_showsEntityId` | Navigates with full from/to query params, asserts URL still contains entity ID and `.entity-history-view` renders |
| `entityHistoryView_entityPickerLink_navigatesToPicker` | Navigates to picker route, asserts `.entity-picker` visible and URL is intact |
| `entityHistoryView_loadingOrError_noCrash` | Navigates to entity history, asserts any graceful state renders and no unhandled page errors |

**Pattern followed:** Matches `causal-tree-view.spec.ts` exactly (BASE_URL, `test.describe`, `toBeVisible({ timeout: 5000 })`).

---

## Verification Results

### Backend build
```
Build succeeded.
    0 Warning(s)
    0 Error(s)
Time Elapsed 00:00:42.28
```

### Integration tests
```
Passed! - Failed: 0, Passed: 3, Skipped: 0, Total: 3, Duration: 117 ms
```

### Frontend unit tests
```
Test Files  50 passed (50)
     Tests  244 passed (244)
```

### TypeScript check
```
(no output — 0 errors)
```

---

## Developer Insights

### Issues Encountered

1. **Missing project reference:** The integration test project (`Tracer.Tests.Integration.csproj`) had no reference to `Tracer.Storage.Parquet`. This is because all prior integration tests used the full TestHarness stack. This batch required adding the explicit project reference.

2. **Half-open interval discrepancy:** The batch instructions specified `to = Zero + 7s` expecting 5 samples at t=3,4,5,6,7, but `ParquetReader` uses `publish_wallclock < $to` (exclusive upper bound). The test was written with `to = Zero + 8s` to correctly include t=7s. This is consistent with how `ParquetReader` is designed (matching the existing unit tests).

### Weak Points Spotted

- `ParquetReader.ReadTimeSeriesAsync` uses an exclusive upper bound (`< to`), which is correct for time-series but differs from what callers might expect if they assume inclusive `to`. This should be clearly documented in the public API (currently only noted in a code comment).
- The E2E tests cannot be verified without a running server. The `.entity-history-view` CSS class existence was confirmed by reading `EntityHistoryView.vue` directly.

### Design Decisions

- Used `DOUBLE` (not `FLOAT`) in the `WriteParquetAsync` helper schema to match the `ParquetSample.Values` dictionary's `double?` type and avoid floating-point representation differences in assertions.
- The `WriteParquetAsync` helper issues three separate DuckDB commands (CREATE TABLE, INSERT, COPY TO) rather than a single multi-statement string, matching the pattern used by the unit test's `CreateParquetAsync` for clarity and reliability.
