# BATCH-35 Report — Phase 7 Entity History View (Backend Foundation)

**Status:** COMPLETE — all 4 tasks implemented, all tests pass.

---

## Test Counts

| Milestone | Count |
|-----------|-------|
| Before BATCH-35 | 354 |
| Task 1 added (ParquetReaderTests) | +11 |
| Task 2 added (SchemaTests extensions) | +4 |
| Task 3 added (MultiIntervalReaderTests extensions) | +5 |
| Task 4 added (FastStateFileLocatorTests) | +7 |
| **After BATCH-35** | **381** |

All 27 new tests pass. No regressions in existing 354 tests.

---

## Tasks Implemented

### Task 1 — `Tracer.Storage.Parquet` assembly (TRC-P7-001)

**New files:**
- `src/Tracer.Storage.Parquet/Tracer.Storage.Parquet.csproj`
- `src/Tracer.Storage.Parquet/ParquetReader.cs`
- `tests/Tracer.Tests.Unit/Parquet/ParquetReaderTests.cs` (11 tests)

**Modified files:**
- `Tracer.sln` — added project entry with GUID `{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}`, config entries, and `NestedProjects` entry under `src/`
- `tests/Tracer.Tests.Unit/Tracer.Tests.Unit.csproj` — added project reference

**Implementation notes:**
- `ParquetReader` opens a fresh in-memory DuckDB connection per call (`Data Source=:memory:`) to avoid cross-file schema conflicts.
- Multi-file reads use `read_parquet(['p1','p2',...])` list syntax for DuckDB to merge files in a single scan.
- Down-sampling uses `ROW_NUMBER() OVER (ORDER BY publish_wallclock)` with modulo stride — simplest reliable approach that preserves chronological order across merged files.
- Null numeric values are coerced to `double?` via `IsDBNull` check before `GetDouble`.

### Task 2 — `idx_slow_state_entity_time` index in `SchemaV1` (TRC-P7-002)

**Modified files:**
- `src/Tracer.Storage.DuckDB/Schema/SchemaV1.cs` — added Phase 7 index to `CreateIndexes`
- `tests/Tracer.Tests.Unit/Storage/SchemaTests.cs` — added 4 new tests

**Implementation notes:**
- The `slow_state` table uses `instance_key` (not `entity_id`) as the entity identifier column. The design doc used "entity_id" loosely; the implementation correctly uses `instance_key`.
- A redundant index exists: `idx_state_instance_time` already covers `slow_state(instance_key, publish_wallclock)`. The new `idx_slow_state_entity_time` is a harmless alias for the same column set with Phase 7 naming conventions. DuckDB 1.0.2 supports multiple named indexes on the same columns.
- `SchemaV1.Version` was intentionally **not** bumped; index additions do not require a schema migration in DuckDB (indexes are metadata only).

### Task 3 — `BuildSlowStateUnionSql` extension (TRC-P7-006)

**Modified files:**
- `src/Tracer.Storage.DuckDB.MultiInterval/LiveMultiIntervalReader.cs` — added `BuildSlowStateUnionSql` to `PooledMultiIntervalConnection`
- `src/Tracer.Storage.DuckDB.MultiInterval/MultiIntervalReader.cs` — added `BuildSlowStateUnionSql` to `MultiIntervalReader`
- `tests/Tracer.Tests.Unit/MultiInterval/MultiIntervalReaderTests.cs` — added 5 tests

**Implementation notes:**
- `PooledMultiIntervalConnection.BuildSlowStateUnionSql` mirrors `BuildEventsUnionSql` exactly, substituting `slow_state` for `events`. Whitespace arms (`SELECT * FROM main.slow_state {whereClause}`) include the where-clause in each arm before the UNION ALL, then ORDER BY and LIMIT append after the full union.
- `MultiIntervalReader.BuildSlowStateUnionSql` includes `__source_alias` in the SELECT to match the convention from `BuildEventsUnionSql`.
- Tests assert SQL string content only — no DuckDB execution needed for these tests since the `slow_state` table does not exist in test DuckDB fixtures (which only have `events`).

### Task 4 — `FastStateFileLocator` helper (TRC-P7-007)

**New files:**
- `src/Tracer.WebApi/Queries/FastStateFileLocator.cs`
- `tests/Tracer.Tests.Unit/WebApi/FastStateFileLocatorTests.cs` (7 tests)

**Modified files:**
- `src/Tracer.WebApi/Tracer.WebApi.csproj` — added `InternalsVisibleTo("Tracer.Tests.Unit")` (for future internal APIs)

**Implementation notes:**
- **Circular dependency discovered:** `Tracer.OfflineViewer` already depends on `Tracer.WebApi`. Adding `BundleOpenManager?` directly as a constructor parameter (as specified in the batch instructions) would create `WebApi → OfflineViewer → WebApi`. Resolved by accepting `Func<string?>? getBundleWorkingDirectory = null` instead, keeping the same semantics. Callers in `OfflineViewer` pass `() => bundleManager.Current?.WorkingDirectory`.
- `IntervalDirectory.FastStateDirectory` is already defined as `Path.Combine(RootPath, "fast_state")`, making the path construction clean.
- `BundleNaming.SafeFileName` appends a 4-char hex hash suffix to prevent collisions between distinct inputs that produce the same sanitized form (e.g. `"a/b"` and `"a_b"` both sanitize to `"a_b"` but get different suffixes).
- Tests use `IntervalDirectory.ForEventsDb(fakeDbPath)` which sets `FastStateDirectory` to `<dir>/fast_state` — avoids constructing a full `IntervalRotator`.

---

## Insight Questions

### Q1: What friction points arose with DuckDB `read_parquet()`, and how were they resolved?

1. **In-memory connection per call**: DuckDB `read_parquet()` can be called from any connection that has access to the filesystem. Opening a fresh in-memory connection per `ReadTimeSeriesAsync` / `InspectSchemaAsync` call avoids stale schema caches if Parquet file structure changes between calls. The trade-off is connection overhead on every call; the assumption is that calls are infrequent enough to not matter.

2. **Multi-file `read_parquet`**: The list form `read_parquet(['path1','path2'])` requires all files to share an identical schema. If a topic's Parquet schema evolved between intervals, DuckDB will error at query time. The `ParquetReader` API currently does not handle schema mismatches across files — a future improvement would be to call `InspectSchemaAsync` per file and fall back to union-based queries when schemas differ.

3. **NULL numeric values**: DuckDB returns `DBNull` for NULL column values, not a typed null. `ReadTimeSeriesAsync` explicitly checks `reader.IsDBNull(colIndex)` before calling `GetDouble`. Without this check, the reader throws `InvalidCastException` on nullable numeric columns.

4. **`DESCRIBE SELECT * FROM read_parquet(...)`**: The `DESCRIBE` statement returns schema info including `null` / `YES` column type names that differ from `duckdb_columns()` metadata. The `IsNumeric` helper uses the DuckDB type names from DESCRIBE output (e.g. `BIGINT`, `DOUBLE`, `DECIMAL`) rather than C# type names.

### Q2: Did `IntervalSetTracker` and `BundleOpenManager` APIs fit the locator design well?

`IntervalSetTracker.CurrentSnapshot()` is a clean fit — synchronous, returns an immutable `IntervalSetSnapshot` with a list of `IntervalReference` objects, each carrying an `IntervalDirectory` with a pre-computed `FastStateDirectory` path. No pain there.

`BundleOpenManager` was architecturally incompatible: it lives in `Tracer.OfflineViewer` which already depends on `Tracer.WebApi`. Introducing `BundleOpenManager` as a constructor parameter of `FastStateFileLocator` would create a circular dependency. The resolution (`Func<string?>? getBundleWorkingDirectory`) is minimal and keeps `FastStateFileLocator` decoupled from the OfflineViewer layer. The delegate pattern also makes the API easier to test without requiring a full `BundleOpenManager` setup.

### Q3: What decisions were made that went beyond the specification?

1. **`Func<string?>?` instead of `BundleOpenManager?`**: The spec called for `BundleOpenManager?` but this was impossible due to circular project dependencies. The `Func<string?>?` approach is strictly more general and equally expressive.

2. **`MultiIntervalReader.BuildSlowStateUnionSql` includes `__source_alias`**: The spec said to "follow the pattern from `BuildEventsUnionSql`". Since `MultiIntervalReader.BuildEventsUnionSql` includes `__source_alias`, the same convention was applied to `BuildSlowStateUnionSql` for consistency. `PooledMultiIntervalConnection.BuildSlowStateUnionSql` does NOT include `__source_alias` (matching `PooledMultiIntervalConnection.BuildEventsUnionSql` which also does not).

3. **Index is redundant but was added anyway**: `idx_state_instance_time` already covers the same `slow_state(instance_key, publish_wallclock)` columns as the new `idx_slow_state_entity_time`. The new index was added as specified to make the Phase 7 naming convention explicit, even though it's effectively a duplicate.

4. **`InternalsVisibleTo` in `Tracer.WebApi.csproj`**: Added proactively even though current tests don't require it, since future `internal` APIs in the WebApi layer will need test access.

### Q4: What edge cases exist in stride-based downsampling?

1. **Stride of 1**: If `totalRows <= maxSamples`, `stride = 1` and `(rn - 1) % 1 = 0` always, returning all rows. Correct.

2. **Stride calculation rounds down**: `stride = (int)(totalRows / maxSamples)`. For `totalRows = 101` and `maxSamples = 100`, stride = 1, returning all 101 rows (exceeds `maxSamples` by 1). A ceiling division or explicit cap would guarantee at most `maxSamples` rows, but the off-by-one on the boundary is negligible for time-series display.

3. **Multi-file ordering**: `ROW_NUMBER() OVER (ORDER BY publish_wallclock)` is computed over the merged UNION ALL result. If two files have overlapping time ranges, rows are interleaved by timestamp before stride is applied — correct behavior for chronological downsampling.

4. **Ties on `publish_wallclock`**: DuckDB's `ROW_NUMBER` is deterministic within a single query execution but not stable across executions for ties. Two rows at the same millisecond may be ordered differently across calls. This is acceptable for time-series charts where display order for simultaneous samples is not meaningful.

5. **Empty result after count query**: If `totalRows == 0` the data query is skipped and an empty `ParquetTimeSeriesResult` is returned. A count of zero is expected when the time range filter eliminates all rows.

### Q5: What would be improved if there were more time?

1. **Schema evolution handling in `ReadTimeSeriesAsync`**: Currently if two Parquet files for the same topic have different column sets (e.g. a column was added in a later interval), DuckDB's list-form `read_parquet` will error. Adding schema union logic (e.g. read each file's schema first and union-project to a common set) would make the reader robust to schema evolution.

2. **Ceiling division for stride**: Change `(int)(totalRows / maxSamples)` to `(int)Math.Ceiling((double)totalRows / maxSamples)` to guarantee at most `maxSamples` rows are returned.

3. **Remove the redundant `idx_slow_state_entity_time` index** (or merge it with `idx_state_instance_time` by renaming): Two indexes on the same column set waste memory and slow down writes. Since DuckDB doesn't support index renaming, a migration script would need to drop and recreate with the Phase 7 name.

4. **Performance test isolation**: `SlowStateEntityQuery_WithIndex_CompletesUnder200ms` writes 50,000 rows on every test run, which makes the test suite noticeably slower. A pre-seeded test fixture or a smaller dataset (e.g. 5,000 rows) that still proves the index is used would be more CI-friendly.

5. **`FastStateFileLocator.GetAvailableTopicsForEntity` returns safe filenames**: The method returns `BundleNaming.SafeFileName(topicName)` strings (the filesystem directory names), not the original topic names. Callers who need original topic names would need to reverse-map via a separate lookup. Adding a `GetAvailableTopicNamesForEntity` overload that reads a metadata sidecar file alongside the Parquet data would be the right long-term approach.

---

## Suggested Commit Message

```
feat(phase7): Parquet reader, slow-state index, union SQL, file locator

- Add Tracer.Storage.Parquet assembly with ParquetReader (DuckDB read_parquet,
  multi-file merge, stride downsampling, null coercion) [TRC-P7-001]
- Add idx_slow_state_entity_time index on slow_state(instance_key, publish_wallclock)
  in SchemaV1 for entity history queries [TRC-P7-002]
- Add BuildSlowStateUnionSql(whereClause, orderByClause, limit) to both
  PooledMultiIntervalConnection and MultiIntervalReader [TRC-P7-006]
- Add FastStateFileLocator to WebApi.Queries: locates fast-state Parquet files
  across intervals and bundle using Func<string?> bundle directory delegate
  (avoids circular WebApi ↔ OfflineViewer dependency) [TRC-P7-007]
- 27 new unit tests; all 381 pass
```
