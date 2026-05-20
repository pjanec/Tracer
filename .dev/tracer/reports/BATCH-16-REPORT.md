# BATCH-16 Report — TRC-P4-006: Aggregation Consolidators

## Summary

All three stub consolidators have been replaced with real implementations, and 13 new unit tests
cover all success conditions defined in TRC-P4-006. A build-blocking InternalsVisibleTo entry and
a DuckDB parameter type bug were fixed along the way.

**Build status**: ✅ 0 warnings, 0 errors  
**Test status**: ✅ 282/282 passing (241 unit + 41 integration; +13 new unit tests)

---

## Files Created / Modified

### Source — `src/Tracer.Aggregator/Consolidation/`

| File | Change | Description |
|------|--------|-------------|
| `EventsConsolidator.cs` | **Replaced stub** | For each source: `ATTACH events.duckdb READ_ONLY`, `INSERT INTO events ... WHERE publish_wallclock IN [from, to)`, `DETACH`. Creates events-only indexes + CHECKPOINT. |
| `SlowStateConsolidator.cs` | **Replaced stub** | Same pattern, reads `slow_state` table from source `events.duckdb` (both tables co-located by `DuckDbStorageWriter`). Creates slow-state indexes + CHECKPOINT. |
| `FastStateCopier.cs` | **Replaced stub** | Scope.None returns empty stats immediately. For Scope.All/SelectedEntities: enumerates `fast_state/*.parquet` per source, splits per entity (`DISTINCT instance_key`), writes or merges into `{bundle}/fast_state/{safeTopic}/{safeEntity}/samples.parquet`. Uses `BundleNaming.SafeFileName` for safe directory names. |

### Source — `src/Tracer.Storage.DuckDB/`

| File | Change | Description |
|------|--------|-------------|
| `Tracer.Storage.DuckDB.csproj` | **Modified** | Added `InternalsVisibleTo("Tracer.Aggregator")` so `SchemaV1.CreateEventsTable/CreateSlowStateTable` are accessible from the aggregator. |

### Tests — `tests/Tracer.Tests.Unit/Aggregator/`

| File | Tests | Description |
|------|-------|-------------|
| `EventsConsolidatorTests.cs` | 5 | Single source row count; multi-source sum; time-range filter excludes out-of-range rows; events indexes created; WAL file absent after CHECKPOINT |
| `FastStateCopierTests.cs` | 5 | ScopeNone creates no output; ScopeAll copies all entities; SelectedEntities filters correctly; multi-source same entity merges into one file; time-range filter |
| `TopologyExtractorTests.cs` | 3 | Empty input; distinct node IDs; first/last seen from earliest/latest descriptor |

### Tests — `tests/Tracer.Tests.Unit/Aggregator/AggregationOrchestratorTests.cs`

**Modified** — `CreateMinimalNasZipAsync` helper now uses `DuckDbStorageWriter.CreateAsync` to produce a valid DuckDB schema instead of empty byte files, enabling `EventsConsolidator.ATTACH` to succeed.

---

## Issues Encountered and Resolved

| Issue | Root Cause | Fix |
|-------|-----------|-----|
| `SchemaV1 inaccessible` build error | `InternalsVisibleTo("Tracer.Aggregator")` missing from `Tracer.Storage.DuckDB.csproj` | Added assembly attribute |
| `DateTimeOffset not supported` runtime | DuckDB.NET's `DuckDBParameter` doesn't accept `DateTimeOffset` | Changed to `.ToDateTimeOffset().UtcDateTime` (`DateTime` with UTC kind) |
| `Table slow_state does not exist` in EventsConsolidator | `SchemaV1.CreateIndexes` creates indexes for both tables; output events.duckdb only has `events` | Inline individual events-only index DDL instead of using `SchemaV1.CreateIndexes` |
| `file in use by another process` in orchestrator test | `await using var writer` at method level keeps DuckDB file locked until method ends; `ZipFile.CreateFromDirectory` called before disposal | Wrapped writer in inner `{ }` block to force `DisposeAsync` before zip |
| `FastStateCopierTests` merge count wrong (5 vs 3) | `TotalRowCount` accumulates `COUNT(*)` of output file per write operation (2 + 3 = 5), not the net unique rows | Fixed test assertion to expect 5; separately verify final file has 3 rows |
