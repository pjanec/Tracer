# BATCH-16 Review — TRC-P4-006: Aggregation Consolidators

**Reviewer:** Dev Lead  
**Status:** APPROVED  
**Test results:** 284/284 passing (243 unit + 41 integration)

---

## Review Checklist

### Architecture

- [x] `EventsConsolidator` uses `ATTACH … READ_ONLY` + `INSERT INTO events SELECT * WHERE publish_wallclock IN [from,to)` + `DETACH` — correct pattern; file-level isolation prevents cross-interval contamination
- [x] Both `events` and `slow_state` tables live in the same `events.duckdb` (as produced by `DuckDbStorageWriter`); `SlowStateConsolidator` correctly reads `slow_state` from source `events.duckdb`, not a non-existent `slow_state.duckdb`
- [x] `FastStateCopier` uses per-entity `DISTINCT instance_key` discovery + entity-scoped `COPY … TO` with atomic file-replace for merges — correct approach for idempotent multi-source consolidation
- [x] `BundleNaming.SafeFileName` used for all directory path components — prevents path injection from untrusted topic/entity names
- [x] `EventsConsolidator` only creates events-specific indexes (not slow_state ones) — correct since the output DB has only the `events` table
- [x] `SlowStateConsolidator` creates its own slow_state indexes and CHECKPOINT — parallel structure to EventsConsolidator is consistent
- [x] `InternalsVisibleTo("Tracer.Aggregator")` added to `Tracer.Storage.DuckDB.csproj` — necessary for `SchemaV1.CreateEventsTable/CreateSlowStateTable`; intentional coupling

### Test quality

- [x] **EventsConsolidatorTests (5)**: single source row count, multi-source sum, time-filter exclusion, index presence via `duckdb_indexes()`, WAL-absent checkpoint — all success conditions 1–3 covered
- [x] **FastStateCopierTests (5)**: ScopeNone no-dir, ScopeAll, SelectedEntities filter, multi-source merge row count + file row count, time-range filter — all success conditions 4–7 covered
- [x] **TopologyExtractorTests (3)**: empty input, distinct node extraction, first/last seen from min/max descriptors
- [x] **ManifestAndStagingTests (2)**: SHA-256 values from manifest match independent `SHA256.HashData` computation; `StagingDirectory.DisposeAsync` leaves `Directory.Exists` returning `false` — success conditions 8–9 covered
- [x] All test classes use `IDisposable`/`IAsyncDisposable` with temp directory lists — no test pollution
- [x] DuckDB file lock issue correctly avoided by wrapping `DuckDbStorageWriter` in inner block scope before `ZipFile.CreateFromDirectory` in `AggregationOrchestratorTests`
- [x] `TotalRowCount` semantics for merge case documented in test comment (2 + 3 = 5 accumulated)

### Issues Resolved During Development

- `InternalsVisibleTo` missing from `Tracer.Storage.DuckDB.csproj` — added; clean root cause
- `DuckDBParameter` does not accept `DateTimeOffset` — fixed to `.UtcDateTime` in all three consolidators; consistent fix
- `SchemaV1.CreateIndexes` includes slow_state indexes — fixed by inlining events-only DDL in `EventsConsolidator`; `SlowStateConsolidator` gets its own inline indexes
- Orchestrator test zip failure due to DuckDB file lock — fixed by block-scoped `await using`; correct fix

### Minor Points

- `TotalRowCount` in `FastStateCopier` accumulates `COUNT(*)` of the output file after each write (including merged files), not "new rows only". This is acceptable for progress/statistics purposes but could be surprising. No action required now.

---

## Verdict: APPROVED — proceed to BATCH-17 (TRC-P4-007: tracer-aggregate.exe CLI)
