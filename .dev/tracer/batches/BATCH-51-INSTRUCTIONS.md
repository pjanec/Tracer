# BATCH-51 Instructions — Phase 10 Backend: SQL Console, Saved Queries, Bundle Library

**Batch:** BATCH-51  
**Tasks:** TRC-P10-001 through TRC-P10-010  
**Phase:** 10 — SQL Console, Saved Queries, Bundle Library (Backend)  
**Report to:** `.dev/tracer/reports/BATCH-51-REPORT.md`

---

## Onboarding

Read before starting:
- `docs/tracer_phase10_design.md` — full Phase 10 design
- `docs/TASK-DETAIL.md` — sections TRC-P10-001 through TRC-P10-010
- `.dev/tracer/reviews/BATCH-43-REVIEW.md` — for SQLite store pattern (`SqliteSavedViewStore`)
- `.dev/tracer/reviews/BATCH-49-REVIEW.md` — for DI registration patterns in HostBuilders

Phase 10 backend adds a read-only SQL console (DuckDB query execution with guardrails), saved query library (with 5 built-in queries), and bundle library management (metadata, import/export).

---

## Architecture Notes

### 1. New Project: `Tracer.Storage.SavedQueries`

Create a new project at `src/Tracer.Storage.SavedQueries/Tracer.Storage.SavedQueries.csproj`:
- Pattern: same as `Tracer.Storage.SavedViews.csproj`
- References: `Tracer.Core`, `Microsoft.Data.Sqlite`, `Microsoft.Extensions.Logging.Abstractions`, `Ulid`
- Add to `Tracer.sln` (use `dotnet sln add`)
- Built-in queries JSON: `BuiltIn/builtin-queries.json` (embedded resource via `<EmbeddedResource>`)
- The store shares `annotations.db` — both `SavedQueriesSchema` and `SavedViewsSchema` create tables in the same DB file

### 2. SQL Executor Design

The `SqlExecutorService` does NOT open new DuckDB connections. It leases from `LiveMultiIntervalReader.AcquireAsync`.

**`SqlGuardrails` — hand-rolled tokenizer:**
- Strip `--` and `/* */` comments first
- Split into tokens (whitespace/operator boundaries)
- Allowed leading keyword: SELECT, WITH, EXPLAIN, DESCRIBE, SHOW, VALUES (case-insensitive)
- Forbidden keywords anywhere in token stream (case-insensitive, not inside string literals): INSERT, UPDATE, DELETE, DROP, CREATE, ALTER, TRUNCATE, ATTACH, DETACH, COPY, EXPORT, IMPORT, LOAD, INSTALL, FORCE, PRAGMA
- Forbidden function names: read_csv_auto, read_parquet, read_json_auto, read_json, scan_parquet
- Multi-statement: reject if `;` appears outside quoted strings
- Quoted identifiers (double-quoted `"identifier"`) must NOT match forbidden keywords

**`SqlExecutorService`:**
- Config via `SqlExecutorConfig` record (DefaultTimeoutSeconds=30, DefaultMaxRows=100_000, MaxMemoryMb=1024)
- Before executing user SQL: `await pooled.Connection.ExecuteNonQueryAsync($"PRAGMA memory_limit = '{config.MaxMemoryMb}MB'")`
- Row limit injection: if no `LIMIT` keyword in token stream, append ` LIMIT {maxRows}`
- Timeout: `CancellationTokenSource` with `TimeSpan.FromSeconds(timeoutSeconds)`; catch `OperationCanceledException` → return `Timeout` state
- Invalid DuckDB SQL → DuckDBException → return `Failed` state with error message

**`SqlSchemaService`:**
- Schema introspection query: `SELECT table_name, column_name, data_type FROM information_schema.columns WHERE table_schema = 'main' ORDER BY table_name, ordinal_position`
- Cache: `_snapshot` field; `InvalidateAsync()` sets it to null; `GetAsync()` re-runs query if null
- Wire invalidation: `IntervalSetTracker.SetChanged` event (Phase 5)

### 3. Bundle Library vs. Phase 4 Bundle Tracking

`BundleLibraryService` is a new read/write metadata service that works on the bundles root directory. It is SEPARATE from `BundleCatalog` (Phase 4, which tracks bundles for aggregation upload). The `BundleLibraryService` adds user-editable `bundle-metadata.json` on top of the aggregator's immutable `metadata.json`.

### 4. Zip-Slip Defense (TRC-P10-008)

In `BundleImportService`, for each `ZipArchiveEntry`:
```csharp
var entryPath = entry.FullName;
if (entryPath.Contains("..") || Path.IsPathRooted(entryPath))
    return BundleImportResult.InvalidFormat("Zip-slip detected");
var allowedExtensions = new[] { ".parquet", ".json", ".db" };
if (!allowedExtensions.Contains(Path.GetExtension(entryPath).ToLowerInvariant()))
    return BundleImportResult.InvalidFormat($"Unexpected extension: {entryPath}");
```

### 5. Built-In Queries JSON Format

`BuiltIn/builtin-queries.json` must be a valid JSON array:
```json
[
  {
    "savedQueryId": "builtin-top-topics-by-volume",
    "label": "Top topics by event count",
    "description": "...",
    "sql": "SELECT topic, COUNT(*) AS event_count FROM events WHERE publish_wallclock >= $from AND publish_wallclock < $to GROUP BY topic ORDER BY event_count DESC LIMIT 100",
    "parameters": [
      { "name": "from", "duckType": "TIMESTAMPTZ", "defaultValueText": "session_start", "description": "Start of range" },
      { "name": "to", "duckType": "TIMESTAMPTZ", "defaultValueText": "session_end", "description": "End of range" }
    ],
    "tags": ["overview", "topics"],
    "isBuiltIn": true,
    "isFavorite": false,
    "author": "tracer",
    "runCount": 0
  },
  ...
]
```

All 5 built-in SQLs MUST pass `SqlGuardrails.Validate`. Only `SELECT`/`WITH`/`APPROX_QUANTILE` etc.

### 6. `IntervalSetTracker.SetChanged` Event

Check `src/Tracer.Storage.DuckDB.MultiInterval/LiveMultiIntervalReader.cs` or `BundleIntervalSetTracker.cs` for the correct event name. Use it to wire `SqlSchemaService.InvalidateAsync()`.

---

## Tasks

### TRC-P10-001 — `SqlGuardrails` + `SqlExecutorService` + `SqlSchemaService`

**Files:**
- `src/Tracer.WebApi/Queries/SqlGuardrails.cs`
- `src/Tracer.WebApi/Queries/SqlExecutorService.cs`
- `src/Tracer.WebApi/Queries/SqlSchemaService.cs`
- `src/Tracer.WebApi/Queries/SqlExecutorConfig.cs`

**Domain types (in same files or companion file):**
- `SqlExecutionRequest` — Sql, Parameters (Dictionary<string,object?>?), TimeoutSeconds?, MaxRows?
- `SqlExecutionResult` — State, Columns, Rows, TruncatedToRows?, DurationMs, ErrorMessage?
- `SqlExecutionState` enum — Succeeded, Failed, Timeout, Rejected
- `SqlColumnInfo` — Name, DuckType
- `SqlExplainResult` — PlanText
- `SqlGuardrailsResult` — IsValid, RejectionReason?
- `SqlSchemaSnapshot` — Tables (IReadOnlyList<SqlTableInfo>)
- `SqlTableInfo` — Name, Columns (IReadOnlyList<SqlColumnInfo>)

**Unit tests:** `tests/Tracer.Tests.Unit/WebApi/SqlGuardrailsTests.cs` (≥15 tests) + `SqlExecutorServiceTests.cs` (≥7 tests) + `SqlSchemaServiceTests.cs` (≥3 tests)

**Key success tests:**
- `Select_Accepted`, `InsertInto_Rejected`, `CreateTable_Rejected`, `DropTable_Rejected`, `Attach_Rejected`, `CopyTo_Rejected`, `Pragma_Rejected`, `MultiStatement_Rejected`, `BlockCommentHidingDdl_Rejected`, `ReadCsvAuto_Rejected`, `ReadParquet_Rejected`, `QuotedIdentifierInsert_Accepted`, `MixedCaseInsert_Rejected`, `With_Select_Accepted`
- `SimpleSelect_ReturnsRows`, `ParameterBinding_Honored`, `DefaultLimitInjected_WhenAbsent`, `ExplicitLimit_NotModified`, `Timeout_ReturnsTimeoutState`, `InvalidSql_ReturnsFailedState`
- `GetAsync_ReturnsTables`, `Cache_SecondCallDoesNotRequery`, `Invalidate_ForcesRefresh`

---

### TRC-P10-002 — SQL API Endpoints

**Files:**
- `src/Tracer.WebApi/Endpoints/SqlEndpoints.cs`
- `src/Tracer.WebApi/Contracts/Dto/SqlDtos.cs` — all SQL DTOs + `SqlDtoMapper`

**Endpoints:**
- `POST /api/sql/execute` → `SqlExecuteResultDto` (HTTP 200 for all states including Rejected, HTTP 400 for empty SQL)
- `GET /api/sql/schema` → `SqlSchemaDto`
- `POST /api/sql/explain` → `SqlExplainResultDto` (HTTP 400 on forbidden/empty SQL, HTTP 200 on success)
- `GET /api/sql/view-template` (from TRC-P10-009, can add here)

**Unit tests:** `tests/Tracer.Tests.Unit/WebApi/SqlEndpointsTests.cs` (≥9 tests)

**Integration tests:** `tests/Tracer.Tests.Integration/SqlConsoleIntegrationTests.cs` (3 tests using `ObserverFixture` with `IBundleModeMarker`)

---

### TRC-P10-003 — `Tracer.Storage.SavedQueries` New Project

**Steps:**
1. Create `src/Tracer.Storage.SavedQueries/Tracer.Storage.SavedQueries.csproj` (model after `Tracer.Storage.SavedViews.csproj`)
2. `dotnet sln add src/Tracer.Storage.SavedQueries/Tracer.Storage.SavedQueries.csproj`
3. Add project reference from `Tracer.WebApi` to `Tracer.Storage.SavedQueries`
4. Add project reference from `Tracer.Tests.Unit` and `Tracer.Tests.Integration` to `Tracer.Storage.SavedQueries`

**Files:**
- `src/Tracer.Storage.SavedQueries/SavedQueryRecord.cs` — `SavedQueryRecord`, `SavedQueryParameter`, `SavedQueryFilter`
- `src/Tracer.Storage.SavedQueries/ISavedQueryStore.cs`
- `src/Tracer.Storage.SavedQueries/SqliteSavedQueryStore.cs`
- `src/Tracer.Storage.SavedQueries/Schema/SavedQueriesSchema.cs`
- `src/Tracer.Storage.SavedQueries/BuiltIn/builtin-queries.json` (embedded resource)
- `src/Tracer.Storage.SavedQueries/BuiltIn/BuiltInLoader.cs`

**Unit/integration tests:** `tests/Tracer.Tests.Unit/SavedQueries/SavedQueriesRoundTripTests.cs` (≥11 tests)

---

### TRC-P10-004 — Saved Queries API Endpoints

**File:** `src/Tracer.WebApi/Endpoints/SavedQueriesEndpoints.cs`
**DTO file:** `src/Tracer.WebApi/Contracts/Dto/SavedQueryDto.cs`

**Endpoints:**
- `GET /api/saved-queries` (with `tag`, `author`, `favorite`, `builtIn` query params)
- `GET /api/saved-queries/{id}` (404 if not found)
- `POST /api/saved-queries` (201, 400 for empty label)
- `PUT /api/saved-queries/{id}` (200, 405 for built-ins, 404 not found)
- `DELETE /api/saved-queries/{id}` (204, 405 for built-ins)
- `POST /api/saved-queries/{id}/favorite` (200 toggle)
- `POST /api/saved-queries/{id}/clone` (201 new record)
- `POST /api/saved-queries/{id}/run` (204)

**Unit tests:** `tests/Tracer.Tests.Unit/WebApi/SavedQueryEndpointsTests.cs` (≥12 tests)

---

### TRC-P10-005 — Built-In Saved Queries (5 queries)

**Files within `Tracer.Storage.SavedQueries`:**
- `BuiltIn/builtin-queries.json` (5 queries as above)
- `BuiltIn/BuiltInLoader.cs` — `EnsureLoadedAsync(ISavedQueryStore, CancellationToken)`

All 5 queries:
1. `builtin-top-topics-by-volume` — `SELECT topic, COUNT(*) AS event_count ... GROUP BY topic ORDER BY event_count DESC LIMIT 100`
2. `builtin-events-by-trace` — `SELECT event_id, topic, publisher_node, publish_wallclock FROM events WHERE trace_id = $trace_id ORDER BY publish_wallclock`
3. `builtin-event-counts-per-node` — `SELECT publisher_node, COUNT(*) AS event_count FROM events WHERE publish_wallclock >= $from AND publish_wallclock < $to GROUP BY publisher_node ORDER BY event_count DESC`
4. `builtin-latency-distribution-by-topic` — `SELECT topic, COUNT(*) AS sample_count, APPROX_QUANTILE(EXTRACT(EPOCH FROM (receive_wallclock - publish_wallclock)) * 1000.0, 0.50) AS p50_ms, APPROX_QUANTILE(EXTRACT(EPOCH FROM (receive_wallclock - publish_wallclock)) * 1000.0, 0.99) AS p99_ms FROM events WHERE publish_wallclock >= $from AND publish_wallclock < $to AND publisher_node != subscriber_node GROUP BY topic ORDER BY p99_ms DESC`
5. `builtin-entity-events` — `SELECT event_id, topic, publisher_node, publish_wallclock, payload_json FROM events WHERE entity_id = $entity_id AND publish_wallclock >= $from AND publish_wallclock < $to ORDER BY publish_wallclock LIMIT 500`

**Unit tests:** `tests/Tracer.Tests.Unit/SavedQueries/BuiltInQueriesServiceTests.cs` (≥8 tests)

---

### TRC-P10-006 — `BundleLibraryService`

**File:** `src/Tracer.WebApi/Queries/BundleLibraryService.cs`

**Records:**
- `BundleLibraryEntry` — BundleId, SessionId, Label?, Description?, Tags, IsArchived, BuiltAtUtc, SizeBytes, LastOpenedAtUtc?, SessionStartUtc, SessionEndUtc?
- `BundleUserMetadata` — Label?, Description?, Tags, IsArchived, LastOpenedAtUtc?
- `BundleMetadataUpdate` — Label?, Description?, Tags?, IsArchived?, LastOpenedAtUtc?

**Behavior:**
- `ListAsync` — reads `metadata.json` (aggregator's, read-only) + `bundle-metadata.json` (user-editable) for each bundle dir
- `UpdateMetadataAsync` — patches `bundle-metadata.json` (partial update: null fields preserved)
- `DeleteAsync` — `Directory.Delete(bundleDir, recursive: true)`; returns false if not found

**Unit tests:** `tests/Tracer.Tests.Unit/WebApi/BundleLibraryServiceTests.cs` (≥9 tests)
**Integration tests:** `tests/Tracer.Tests.Integration/BundleLibraryRoundTripTests.cs` (1 round-trip test)

---

### TRC-P10-007 — Bundle Library API Endpoints

**File:** `src/Tracer.WebApi/Endpoints/BundleLibraryEndpoints.cs`
**DTO file:** `src/Tracer.WebApi/Contracts/Dto/BundleLibraryEntryDto.cs`

**Endpoints:**
- `GET /api/bundles/library` (optional: `archived`, `tag`, `sortBy`, `desc`)
- `PUT /api/bundles/{id}/metadata` (200, 404)
- `POST /api/bundles/{id}/opened` (204, 404)
- `DELETE /api/bundles/{id}` (204, 404)
- `POST /api/bundles/import` (multipart body → `BundleImportService`; 201, 409 duplicate, 400 invalid)
- `GET /api/bundles/{id}/download` (streaming zip via `BundleExportService`)

**Unit tests:** `tests/Tracer.Tests.Unit/WebApi/BundleLibraryEndpointsTests.cs` (≥12 tests)

---

### TRC-P10-008 — Bundle Import/Export

**Files:**
- `src/Tracer.WebApi/Queries/BundleExportService.cs`
- `src/Tracer.WebApi/Queries/BundleImportService.cs`
- `src/Tracer.WebApi/Queries/BundleImportResult.cs`

**Export:** Stream zip of entire bundle directory to `destination` stream; no temp file; relative paths only.
**Import:** Extract to temp dir → validate (zip-slip check, extension check, Phase 4 BundleValidator on `metadata.json`) → atomic rename to final dir.

**Unit tests** (add to `BundleLibraryServiceTests.cs` or new file): `BundleExportServiceTests.cs` (≥3 tests) + `BundleImportServiceTests.cs` (≥6 tests)
**Integration test:** `BundleLibraryRoundTripTests.ExportThenImport_RoundTrip`

---

### TRC-P10-009 — View SQL Template Endpoint

**Add to `SqlEndpoints.cs`** (or new file `ViewTemplateEndpoints.cs`):
- `GET /api/sql/view-template?view={view}&from=...&to=...` and other view-specific params
- `ViewSqlTemplateService.cs` — maps `(view, params)` → parameterized SQL string

**View types:** `timeline`, `entity-history`, `causal`, `latency`, `gaps`, `topology`
**SQL injection defense:** single-quote escaping for string literal params (replace `'` with `''`)
**Unit tests:** `tests/Tracer.Tests.Unit/WebApi/ViewTemplateEndpointsTests.cs` (≥9 tests)

---

### TRC-P10-010 — DI Wiring

**Files to modify:**
- `src/Tracer.Observer/ObserverHostBuilder.cs` — add Phase 10 service registrations + endpoint mapping
- `src/Tracer.OfflineViewer/OfflineViewerHostBuilder.cs` — same
- `src/Tracer.TestHarness/Observer/ObserverFixture.cs` — register Phase 10 services if needed for integration tests

**Registrations to add:**
```csharp
// SQL Console
builder.Services.AddSingleton(new SqlExecutorConfig { DefaultTimeoutSeconds = 30, DefaultMaxRows = 100_000, MaxMemoryMb = 1024 });
builder.Services.AddSingleton<SqlExecutorService>();
builder.Services.AddSingleton<SqlSchemaService>();
builder.Services.AddSingleton<ViewSqlTemplateService>();

// Saved Queries
builder.Services.AddSingleton<ISavedQueryStore>(sp => {
    var annotationsDbPath = Path.Combine(config.DataRoot, "annotations.db");
    return new SqliteSavedQueryStore(annotationsDbPath, sp.GetRequiredService<ILogger<SqliteSavedQueryStore>>());
});

// Bundle Library
builder.Services.AddSingleton<BundleLibraryService>(sp => new BundleLibraryService(bundlesRoot));
builder.Services.AddSingleton<BundleExportService>(sp => new BundleExportService(bundlesRoot));
builder.Services.AddSingleton<BundleImportService>(sp => new BundleImportService(bundlesRoot, sp.GetRequiredService<ILogger<BundleImportService>>()));
```

**Endpoints to map:**
```csharp
SqlEndpoints.Map(app);
SavedQueriesEndpoints.Map(app);
BundleLibraryEndpoints.Map(app);
```

**Built-in seeding (on startup):**
Use `app.Lifetime.ApplicationStarted.Register(async () => { await BuiltInLoader.EnsureLoadedAsync(store, ct); })` or an `IHostedService`.

**Wire schema invalidation:**
```csharp
intervalSetTracker.SetChanged += (_, _) => schemaService.InvalidateAsync();
```

**Integration/wiring tests:** `tests/Tracer.Tests.Unit/WebApi/WiringTests.cs` (≥8 tests)

---

## Build and Test Commands

```powershell
# Kill stale testhost
Get-Process -Name "testhost" -ErrorAction SilentlyContinue | Stop-Process -Force

# Add new project to solution (run once)
cd d:\Work\Tracer; dotnet sln add src\Tracer.Storage.SavedQueries\Tracer.Storage.SavedQueries.csproj

# Build
dotnet build Tracer.sln -c Release --no-incremental 2>&1 | Select-Object -Last 5

# Run Phase 10 backend unit tests
Get-Process -Name "testhost" -ErrorAction SilentlyContinue | Stop-Process -Force
dotnet test tests\Tracer.Tests.Unit -c Release --no-build --filter "FullyQualifiedName~SqlGuardrails|FullyQualifiedName~SqlExecutor|FullyQualifiedName~SqlSchema|FullyQualifiedName~SqlEndpoints|FullyQualifiedName~SavedQuery|FullyQualifiedName~BuiltIn|FullyQualifiedName~BundleLibrary|FullyQualifiedName~BundleExport|FullyQualifiedName~BundleImport|FullyQualifiedName~ViewTemplate|FullyQualifiedName~Wiring" 2>&1 | Select-Object -Last 8

# Run integration tests
dotnet test tests\Tracer.Tests.Integration -c Release --no-build --filter "FullyQualifiedName~SqlConsole|FullyQualifiedName~BundleLibrary|FullyQualifiedName~SavedQueries" 2>&1 | Select-Object -Last 8
```

---

## Notes and Traps

1. **Multi-statement DuckDB.NET**: DuckDB.NET does NOT support multi-statement execution. `SqlGuardrails` must reject multi-statement SQL before it reaches DuckDB.

2. **`SqlGuardrails` quoted identifiers**: `"INSERT"` as a column name (double-quoted) must NOT be rejected. Only unquoted tokens are checked against the forbidden list.

3. **`PRAGMA` in DuckDB**: `PRAGMA threads = 4` is DuckDB syntax. The executor uses `PRAGMA memory_limit` internally but must reject it in user-submitted SQL.

4. **`BundleValidator` location**: Phase 4 validator is in `src/Tracer.Bundle/Validation/`. Check the class name before referencing it in `BundleImportService`.

5. **`IntervalSetTracker.SetChanged`**: Check the actual event name in `src/Tracer.Storage.DuckDB.MultiInterval/` — it may be `IntervalSetChanged` or `SetChanged` on `BundleIntervalSetTracker`.

6. **`annotations.db` shared between stores**: `SqliteSavedQueryStore` and `SqliteSavedViewStore` and `SqliteAnnotationStore` all use the same `annotations.db`. Each creates their own table with `CREATE TABLE IF NOT EXISTS`. This is safe as long as each opens a separate `SqliteConnection` (SQLite handles concurrent access internally for reads).

7. **Zip streaming for export**: Use `ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true)` and `entry.Open()` for streaming. Do NOT buffer the entire archive in memory.

8. **`bundle-metadata.json` atomic write**: Write to a `.tmp` file, then `File.Move(tmp, dest, overwrite: true)` for atomicity on NTFS.
