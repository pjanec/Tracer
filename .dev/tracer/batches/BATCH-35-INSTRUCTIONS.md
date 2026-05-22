# BATCH-35 — Phase 7 Backend Foundation: Parquet Assembly, Schema Index, SlowState SQL Builder, FastState File Locator

**Batch Number:** BATCH-35
**Tasks:** TRC-P7-001, TRC-P7-002, TRC-P7-006, TRC-P7-007
**Phase:** Phase 7 — Entity History View, Slow State Time Series, Fast State Drill-Down
**Estimated Effort:** 10–12 hours
**Priority:** HIGH
**Dependencies:** BATCH-34 (Phase 6 complete)

---

## 📋 Onboarding & Workflow

### Developer Instructions

This batch starts Phase 7 by implementing the infrastructure that all entity-history query services depend on:
1. A new `Tracer.Storage.Parquet` assembly for reading fast-state Parquet files on demand
2. A `slow_state` entity-time index in DuckDB schema
3. A `BuildSlowStateUnionSql` extension on `PooledMultiIntervalConnection` (mirrors the existing `BuildEventsUnionSql`)
4. A `FastStateFileLocator` helper that locates Parquet files for a given (topic, entity) pair

These four pieces are the prerequisite foundation for BATCH-36 which will add the entity query services and REST endpoints.

**Work sequentially in task order. Do NOT move to the next task until ALL tests for the current task pass.**

### Required Reading (IN ORDER)
1. **Phase 7 Design:** `docs/tracer_phase7_design.md` — read §1 (scope), §2 (project layout), §3.1 (schema index), §4.4 (Parquet reader), §4.5 (file locator), §4.3 last paragraph (BuildSlowStateUnionSql snippet)
2. **Task Definitions:** `docs/TASK-DETAIL.md` — sections `TRC-P7-001`, `TRC-P7-002`, `TRC-P7-006`, `TRC-P7-007`
3. **Previous batch review:** `.dev/tracer/reviews/BATCH-34-REVIEW.md`
4. **Existing multi-interval reader:** `src/Tracer.Storage.DuckDB.MultiInterval/LiveMultiIntervalReader.cs` — see `PooledMultiIntervalConnection.BuildEventsUnionSql()` as the exact pattern for `BuildSlowStateUnionSql`
5. **Existing MultiIntervalReader:** `src/Tracer.Storage.DuckDB.MultiInterval/MultiIntervalReader.cs` — see standalone `BuildEventsUnionSql()` for the `MultiIntervalReader` class (the same extension needs adding there too for completeness, used in offline tests)
6. **Existing schema:** `src/Tracer.Storage.DuckDB/Schema/SchemaV1.cs` — see existing `CreateIndexes` constant; extend it
7. **BundleNaming:** `src/Tracer.Bundle/Packaging/BundleNaming.cs` — for `SafeFileName(string)` used in `FastStateFileLocator`
8. **BundleOpenManager:** `src/Tracer.OfflineViewer/Browser/BundleOpenManager.cs` — interface shape for `FastStateFileLocator` constructor
9. **IntervalSetTracker:** `src/Tracer.Storage.DuckDB.MultiInterval/IntervalSetTracker.cs` — for `CurrentSnapshot()` and `IntervalSetSnapshot.Intervals`
10. **IntervalDirectory:** look up the `IntervalDirectory` or `IntervalReference` record — how to get `RootPath`

### Source Code Locations
- **New assembly:** `src/Tracer.Storage.Parquet/` (CREATE NEW)
- **Schema change:** `src/Tracer.Storage.DuckDB/Schema/SchemaV1.cs`
- **BuildSlowStateUnionSql:** `src/Tracer.Storage.DuckDB.MultiInterval/LiveMultiIntervalReader.cs` (add to `PooledMultiIntervalConnection`) AND `src/Tracer.Storage.DuckDB.MultiInterval/MultiIntervalReader.cs` (add to `MultiIntervalReader`)
- **FastStateFileLocator:** `src/Tracer.WebApi/Queries/FastStateFileLocator.cs` (CREATE NEW)
- **Unit tests:** `tests/Tracer.Tests.Unit/Parquet/ParquetReaderTests.cs` (CREATE NEW), `tests/Tracer.Tests.Unit/Storage/SchemaTests.cs` (EXTEND), `tests/Tracer.Tests.Unit/WebApi/FastStateFileLocatorTests.cs` (CREATE NEW), multi-interval tests (EXTEND)

### Build and Test Commands
```
# Full solution build
dotnet build d:\Work\Tracer\Tracer.sln -c Release --no-incremental

# Backend unit tests (exclude slow publish test)
dotnet test d:\Work\Tracer\tests\Tracer.Tests.Unit -c Release --no-build --filter "FullyQualifiedName!~Publish_ProducesExpectedLayout"

# Current counts before this batch: 354 unit tests passing
```

### Constraints
- `TreatWarningsAsErrors=true`, `Nullable=enable`, `LangVersion=12`
- No new NuGet packages: `DuckDB.NET.Data` is already in `Directory.Packages.props`
- `Tracer.Core` must have zero third-party package references (unchanged)
- `ParquetReader`: every public method opens its own `new DuckDBConnection("Data Source=:memory:")` — no shared connection field
- Column names go through `SafeColumnIdentifier`, file paths go through `EscapeSql` — never raw-concatenated into SQL

### Report Submission
**When done, submit your report to:** `.dev/tracer/reports/BATCH-35-REPORT.md`
**If you have questions, create:** `.dev/tracer/questions/BATCH-35-QUESTIONS.md`

---

## Context

Phase 7 adds the entity-centric perspective to Tracer: "what happened to this specific entity over its lifetime?" This requires querying:
- **Events** from DuckDB (fast path, multi-interval)
- **Slow state** from DuckDB (new path: `slow_state` table, needs index + new union builder)
- **Fast state** from Parquet files (new path: per-entity files written by Phase 2 writer)

This batch builds the infrastructure layer. Subsequent batch (BATCH-36) will add the query services and REST endpoints.

---

## 🎯 Batch Objectives

1. `Tracer.Storage.Parquet` — new project that reads fast-state Parquet files using DuckDB's `read_parquet()` function
2. `idx_slow_state_entity_time` — composite index on `(entity_id, publish_wallclock)` added to `SchemaV1`
3. `BuildSlowStateUnionSql` — extends `PooledMultiIntervalConnection` and `MultiIntervalReader` so slow-state can be queried across all attached intervals
4. `FastStateFileLocator` — scans interval directories and/or bundle working dir to find `samples.parquet` files for a (topic, entity) pair

---

## ✅ Tasks

### Task 1: Tracer.Storage.Parquet Assembly (TRC-P7-001)

**Task Definition:** See [TASK-DETAIL.md TRC-P7-001](../../../docs/TASK-DETAIL.md#trc-p7-001--tracerstorageparquet-assembly)  
**Design Reference:** `docs/tracer_phase7_design.md` §4.4

**New project:** `src/Tracer.Storage.Parquet/Tracer.Storage.Parquet.csproj`
- Add to `Tracer.sln`
- Project references: `Tracer.Core` only
- Package references: `DuckDB.NET.Data` (already in central packages, no version needed in csproj)
- Standard properties: `<Nullable>enable</Nullable>`, `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`, `<LangVersion>12</LangVersion>`, `<ImplicitUsings>enable</ImplicitUsings>`

**Files to create:**
- `src/Tracer.Storage.Parquet/ParquetReader.cs` — main class

**Key design choices from §4.4 (do NOT duplicate, read the design):**
- Per-call in-memory DuckDB connection: `new DuckDBConnection("Data Source=:memory:")`
- Stride downsampling (ROW_NUMBER approach) when `totalSamples > maxSamples`
- `SafeColumnIdentifier(string name)` — wraps in double-quotes, escapes internal `"` as `""`
- `EscapeSql(string s)` — doubles single quotes for path interpolation
- `IsNumeric(string duckType)` — returns true for all DuckDB numeric types
- Multi-file overload uses `read_parquet(['path1','path2',...])` syntax

**Result types** (can be in same file or companion `ParquetTypes.cs`):
```csharp
public sealed record ParquetColumn(string Name, string DuckType, bool IsNumeric);
public sealed record ParquetSchema(string Path, IReadOnlyList<ParquetColumn> Columns);
public sealed record ParquetSample(WallclockTime PublishWallclock, IReadOnlyDictionary<string, double?> Values);
public sealed record ParquetTimeSeriesResult
{
    public required IReadOnlyList<string> Columns { get; init; }
    public required IReadOnlyList<ParquetSample> Samples { get; init; }
    public required long TotalSamples { get; init; }
    public required bool Downsampled { get; init; }
}
```

**Tests required** (`tests/Tracer.Tests.Unit/Parquet/ParquetReaderTests.cs`):

The tests need to create real Parquet files using DuckDB's `COPY ... TO '...' (FORMAT PARQUET)`. Example setup:
```csharp
// In test helper — create a temp parquet file with DuckDB
using var conn = new DuckDBConnection("Data Source=:memory:");
await conn.OpenAsync();
await using var cmd = conn.CreateCommand();
cmd.CommandText = $"""
    CREATE TABLE t (publish_wallclock TIMESTAMP, instance_key VARCHAR, x FLOAT);
    INSERT INTO t VALUES (timestamptz '2026-01-01 00:00:01', 'ent-A', 1.0);
    -- ... more rows ...
    COPY t TO '{tempPath}' (FORMAT PARQUET);
    """;
await cmd.ExecuteNonQueryAsync();
```

All 10 success conditions from TRC-P7-001 must be verified. Key tests:
- `InspectSchemaAsync_ThreeColumnParquet_ReturnsAllColumns` — creates real 3-col parquet, verifies schema
- `ReadTimeSeriesAsync_BelowMaxSamples_NoDownsampling` — 50 samples, maxSamples=100 → all returned, Downsampled=false
- `ReadTimeSeriesAsync_AboveMaxSamples_StridedDownsampling` — 1000 samples, maxSamples=100 → Downsampled=true, ≤100 returned
- `ReadTimeSeriesAsync_MultipleFiles_MergesRows` — two parquet files, 50 samples each → 100 total
- `SafeColumnIdentifier_EmbeddedDoubleQuote_Escaped` — unit test for static method
- `EscapeSql_SingleQuoteInPath_Doubled` — unit test for static method

**Note:** `SafeColumnIdentifier` and `EscapeSql` can be `internal static` to enable direct unit testing. Add `[assembly: InternalsVisibleTo("Tracer.Tests.Unit")]` if needed.

---

### Task 2: Schema Extension — slow_state Entity-Time Index (TRC-P7-002)

**Task Definition:** See [TASK-DETAIL.md TRC-P7-002](../../../docs/TASK-DETAIL.md#trc-p7-002--schema-extension-slow_state-entity-time-index)  
**Design Reference:** `docs/tracer_phase7_design.md` §3.1

**File:** `src/Tracer.Storage.DuckDB/Schema/SchemaV1.cs` (UPDATE)

Append to `CreateIndexes` after the existing `-- Phase 6` block:
```sql
-- Phase 7
CREATE INDEX IF NOT EXISTS idx_slow_state_entity_time
    ON slow_state (entity_id, publish_wallclock) WHERE entity_id IS NOT NULL;
```

**Important constraints:**
- `SchemaV1.Version` stays `1` (no version bump for index additions)
- `CREATE INDEX IF NOT EXISTS` ensures idempotency
- Single `const string` field — do not split it

**Tests:** Update `tests/Tracer.Tests.Unit/Storage/SchemaTests.cs`:
- Extend `AllIndexes_AreCreated` (or add `AllIndexes_AreCreated_IncludesSlowStateEntityTimeIndex`) to verify `idx_slow_state_entity_time` appears in `duckdb_indexes()` WHERE `table_name = 'slow_state'`
- Add `CreateIndexes_IsIdempotent_SlowStateIndex` — run CreateIndexes twice, no exception, index appears exactly once
- Add `SchemaV1_CreateIndexes_ContainsPhase7CommentBlock` — string contains `"-- Phase 7"`
- Add `SlowStateEntityQuery_WithIndex_CompletesUnder200ms` — write 50,000 slow-state rows for 10 entities, query by entity_id + time range, assert elapsed < 200ms and only matching entity rows returned

All 4 success conditions from TRC-P7-002 must be verified.

---

### Task 3: BuildSlowStateUnionSql Extension (TRC-P7-006)

**Task Definition:** See [TASK-DETAIL.md TRC-P7-006](../../../docs/TASK-DETAIL.md#trc-p7-006--buildslowstateunionsql-extension)  
**Design Reference:** `docs/tracer_phase7_design.md` §4.3 (last paragraph, code snippet)

**Files to update:**
1. `src/Tracer.Storage.DuckDB.MultiInterval/LiveMultiIntervalReader.cs` — add to `PooledMultiIntervalConnection` class
2. `src/Tracer.Storage.DuckDB.MultiInterval/MultiIntervalReader.cs` — add to `MultiIntervalReader` class

For `PooledMultiIntervalConnection`, add after `BuildEventsUnionSql()`:
```csharp
/// <summary>
/// Builds a UNION ALL SQL covering all intervals' <c>slow_state</c> tables.
/// Returns <c>"SELECT NULL WHERE FALSE"</c> when there are no intervals.
/// </summary>
public string BuildSlowStateUnionSql(string whereClause = "", string orderByClause = "", int? limit = null)
{
    var parts = new List<string>();
    if (_hasActive) parts.Add($"SELECT * FROM main.slow_state {whereClause}");
    foreach (var alias in _aliases) parts.Add($"SELECT * FROM {alias}.slow_state {whereClause}");
    if (parts.Count == 0) return "SELECT NULL WHERE FALSE";
    var sql = string.Join("\nUNION ALL\n", parts);
    if (!string.IsNullOrEmpty(orderByClause)) sql += "\n" + orderByClause;
    if (limit.HasValue) sql += $"\nLIMIT {limit.Value}";
    return sql;
}
```

For `MultiIntervalReader`, add analogous method (uses `_manager.Attachments.Keys` instead of `_aliases`).

**Tests:** Add to `tests/Tracer.Tests.Unit/Storage/MultiIntervalReaderTests.cs` (or create a focused test class):
- `BuildSlowStateUnionSql_TwoAttachments_ProducesUnionAll` — two attached intervals → SQL contains both `.slow_state` entries joined with `UNION ALL`
- `BuildSlowStateUnionSql_WhereClause_AppearsInBothArms` — WHERE clause text in both subquery arms
- `BuildSlowStateUnionSql_NoAttachments_ReturnsSentinel` — exact `"SELECT NULL WHERE FALSE"`
- `BuildSlowStateUnionSql_LimitSet_AppendsLimitClause` — SQL contains `LIMIT 500`
- `BuildSlowStateUnionSql_DoesNotReferenceEventsTable` — returned SQL does not contain `.events`

All 5 success conditions from TRC-P7-006 must be verified.

---

### Task 4: FastStateFileLocator (TRC-P7-007)

**Task Definition:** See [TASK-DETAIL.md TRC-P7-007](../../../docs/TASK-DETAIL.md#trc-p7-007--faststatefilelocator)  
**Design Reference:** `docs/tracer_phase7_design.md` §4.5

**File:** `src/Tracer.WebApi/Queries/FastStateFileLocator.cs` (CREATE NEW)

The locator finds `samples.parquet` files for a (topic, entity) pair across:
- Live mode: all intervals from `IntervalSetTracker.CurrentSnapshot()`
- Offline mode: bundle working directory from `BundleOpenManager?.Current`

Key behaviors (from §4.5 code snippet):
- `BundleNaming.SafeFileName(topic)` and `BundleNaming.SafeFileName(entityId)` for directory-safe encoding
- `File.Exists(candidate)` check before adding — must NOT throw if the directory doesn't exist
- `BundleOpenManager` is nullable; null means live-only
- `GetAvailableTopicsForEntity(string entityId)` enumerates `fast_state/` subdirectories in all interval roots (and bundle dir if present) looking for entity sub-folder matching `BundleNaming.SafeFileName(entityId)`

**Note:** To access `IntervalSetTracker.CurrentSnapshot()`, review the actual `IntervalSetTracker` API in `src/Tracer.Storage.DuckDB.MultiInterval/IntervalSetTracker.cs`. To navigate interval directory paths, review how existing code accesses the intervals' directory paths — look at `IntervalReference` and `IntervalDirectory` types.

**Tests** (`tests/Tracer.Tests.Unit/WebApi/FastStateFileLocatorTests.cs`):

All 7 success conditions from TRC-P7-007 must be verified. Tests must use **real temp directories and placeholder files** (not mocks for the file system). For `IntervalSetTracker` and `BundleOpenManager`, use test doubles or minimal fakes.

Key tests:
- `LocateFiles_LiveMode_TwoIntervals_ReturnsTwoPaths` — real temp dirs with placeholder `samples.parquet` files
- `LocateFiles_TopicAbsentInInterval_NotIncluded` — file missing for topic → empty list
- `LocateFiles_OfflineMode_FindsBundleFile` — bundle working dir has the file
- `LocateFiles_TopicWithSlash_SafeFileNameApplied` — topic with `/` uses safe encoded directory name
- `GetAvailableTopicsForEntity_MultipleTopicDirs_ReturnsAll` — multiple topic dirs → all topics listed
- `LocateFiles_FileDoesNotExist_DirectoryExists_NotIncluded` — directory exists but file missing → not included
- `LocateFiles_NullBundleManager_LiveModeOnly_NoException` — null bundle manager → no exception

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: You MUST complete tasks in sequence with passing tests:**

1. **Task 1 (TRC-P7-001):** New project + `ParquetReader` → tests → **ALL tests pass** ✅
2. **Task 2 (TRC-P7-002):** Schema index → tests → **ALL tests pass** ✅
3. **Task 3 (TRC-P7-006):** `BuildSlowStateUnionSql` → tests → **ALL tests pass** ✅
4. **Task 4 (TRC-P7-007):** `FastStateFileLocator` → tests → **ALL tests pass** ✅

**DO NOT** move to the next task until:
- ✅ Current task implementation complete
- ✅ Current task tests written
- ✅ **ALL tests passing** (including all previous tests from prior batches)

After all 4 tasks pass, run the full test suite and fix any regressions before writing your report. Do NOT stop at any point to ask permission for obvious steps — just implement, test, and fix until everything is green.

---

## 🧪 Testing Requirements

**Minimum new test counts:** ~20 new unit tests
**Specific quality requirements:**

- Parquet tests must use **real Parquet files** created via DuckDB `COPY ... TO ... (FORMAT PARQUET)` — no mocking file reads
- Schema tests must use a **real DuckDB file** created by `DuckDbStorageWriter.CreateAsync` — query `duckdb_indexes()` for real index verification
- `FastStateFileLocator` tests must use **real temp directories** on disk with placeholder files
- All `SafeColumnIdentifier` and `EscapeSql` tests verify actual output strings
- Downsampling test must verify `TotalSamples` vs actual `Samples.Count` ratio AND `Downsampled` flag

**❗ TEST QUALITY — NOT ACCEPTABLE:**
- Tests that only verify object construction or property assignment
- Tests that mock `File.Exists` (use real files instead)
- Tests that don't assert on actual returned data values

---

## 📊 Report Requirements

When done, write `.dev/tracer/reports/BATCH-35-REPORT.md` including:

**Q1:** What issues did you encounter with the DuckDB `read_parquet()` API or the in-memory connection approach? How did you work around them?

**Q2:** Did the `IntervalSetTracker` and `BundleOpenManager` APIs fit cleanly for `FastStateFileLocator`, or did you need to adapt?

**Q3:** What design decisions did you make beyond the spec? (e.g., visibility of `SafeColumnIdentifier`, how you handled errors in the Parquet reader)

**Q4:** Were there any edge cases in the stride-based downsampling that weren't obvious from the spec?

**Q5:** What would you improve in this batch's implementation if you had more time?

**Include final test counts** (before and after): unit test count, any integration test regressions.

**Include suggested commit message.**

---

## 🎯 Success Criteria

This batch is DONE when:
- [ ] `Tracer.Storage.Parquet` project added to solution, builds clean
- [ ] `ParquetReader.InspectSchemaAsync`, `ReadTimeSeriesAsync(single)`, `ReadTimeSeriesAsync(multi)` implemented with per-call connections
- [ ] `SafeColumnIdentifier` and `EscapeSql` are injection-safe helpers
- [ ] `idx_slow_state_entity_time` added to `SchemaV1.CreateIndexes` under `-- Phase 7` comment
- [ ] `BuildSlowStateUnionSql` added to `PooledMultiIntervalConnection` and `MultiIntervalReader`
- [ ] `FastStateFileLocator` with `LocateFiles` and `GetAvailableTopicsForEntity` implemented
- [ ] All ~20 new tests passing
- [ ] All 354 existing unit tests still passing (no regressions)
- [ ] Report submitted

---

## ⚠️ Common Pitfalls to Avoid

1. **DuckDB in-memory connections and `read_parquet`**: DuckDB's in-memory mode can read external Parquet files via `read_parquet()`. This works even though the DB is in-memory. Do not try to ATTACH the Parquet file.
2. **`SafeColumnIdentifier` for column names in SQL**: Column names from the user (topic schema) must ALWAYS go through `SafeColumnIdentifier` before appearing in SQL. Never do `$"SELECT {columnName} FROM ..."`.
3. **`EscapeSql` for file paths**: File paths in `read_parquet('...')` must have single quotes escaped. Always use `EscapeSql(path)`.
4. **Stride of 1 when not downsampling**: When `totalSamples <= maxSamples`, skip the `ROW_NUMBER()` approach entirely — run a simple `SELECT` to avoid the overhead.
5. **IntervalSetTracker snapshot**: Call `CurrentSnapshot()` each time `LocateFiles` is invoked — don't cache the snapshot.
6. **`SafeFileName` for directory construction**: Always use `BundleNaming.SafeFileName(topic)` and `BundleNaming.SafeFileName(entityId)` before path construction — topic names can contain slashes, colons, etc.
7. **SchemaV1.Version stays 1**: Index-only changes don't bump the schema version.

---

## 📚 Reference Materials

- **Task Defs:** `docs/TASK-DETAIL.md` — TRC-P7-001, TRC-P7-002, TRC-P7-006, TRC-P7-007
- **Phase 7 Design:** `docs/tracer_phase7_design.md` — §3.1, §4.3 (BuildSlowStateUnionSql snippet), §4.4 (full ParquetReader impl), §4.5 (FastStateFileLocator)
- **Existing PooledMultiIntervalConnection:** `src/Tracer.Storage.DuckDB.MultiInterval/LiveMultiIntervalReader.cs` — `BuildEventsUnionSql` as the pattern
- **Existing SchemaV1:** `src/Tracer.Storage.DuckDB/Schema/SchemaV1.cs`
- **BundleNaming:** `src/Tracer.Bundle/Packaging/BundleNaming.cs`
- **Phase 7 project layout overview:** `docs/tracer_phase7_design.md` §2
