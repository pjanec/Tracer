# BATCH-51 Report — Phase 10 Backend: SQL Console, Saved Queries, Bundle Library

**Batch:** BATCH-51  
**Tasks:** TRC-P10-001 through TRC-P10-010  
**Status:** COMPLETE  
**Build:** ✅ 0 errors, 0 warnings (`dotnet build Tracer.sln -c Release --no-incremental`)  
**Unit Tests (Phase 10 subset):** ✅ 91 passed, 0 failed  
**Integration Tests (Phase 10 subset):** ✅ 6 passed, 0 failed  

---

## Files Created

### New Project: `src/Tracer.Storage.SavedQueries/`

- **`Tracer.Storage.SavedQueries.csproj`** — New library project referencing `Tracer.Core`, `Microsoft.Data.Sqlite`, `Microsoft.Extensions.Logging.Abstractions`, `Ulid`. `InternalsVisibleTo` for `Tracer.Tests.Unit`. EmbeddedResource for `builtin-queries.json`.
- **`SavedQueryRecord.cs`** — Domain types: `SavedQueryRecord` (12 properties incl. `SavedQueryId`, `Label`, `Sql`, `Parameters`, `Tags`, `IsBuiltIn`, `IsFavorite`, `Author`, `CreatedAtUtc`, `LastRunAtUtc`, `RunCount`), `SavedQueryParameter`, `SavedQueryFilter`.
- **`ISavedQueryStore.cs`** — Interface with `ListAsync`, `GetAsync`, `CreateAsync`, `UpdateAsync`, `DeleteAsync`, `IncrementRunCountAsync`, `ToggleFavoriteAsync` (bypasses IsBuiltIn guard to allow favorite toggling on built-in queries).
- **`SqliteSavedQueryStore.cs`** — Full SQLite implementation using `Microsoft.Data.Sqlite`. Constructor calls `InitializeSync()`. `UpdateAsync`/`DeleteAsync` throw `InvalidOperationException` for built-in records. `ToggleFavoriteAsync` uses direct SQL `UPDATE` bypassing the built-in check. Implements `IDisposable`.
- **`Schema/SavedQueriesSchema.cs`** — `CREATE TABLE IF NOT EXISTS saved_queries` DDL with two indexes (label, is_favorite).
- **`BuiltIn/builtin-queries.json`** — 5 embedded built-in queries (read-only SELECT SQL):
  - `builtin-top-topics-by-volume` — event counts by topic
  - `builtin-events-by-trace` — trace-grouped event summary
  - `builtin-event-counts-per-node` — node event counts
  - `builtin-latency-distribution-by-topic` — latency percentile proxy
  - `builtin-entity-events` — entity-level event history
- **`BuiltIn/BuiltInLoader.cs`** — `EnsureLoadedAsync(ISavedQueryStore, CancellationToken)` reads embedded JSON and inserts missing built-in queries (idempotent by ID).

### New/Modified Services: `src/Tracer.WebApi/Queries/`

- **`SqlGuardrails.cs`** — Hand-rolled tokenizer enforcing read-only SQL. Forbidden mutating keywords (INSERT, UPDATE, DELETE, CREATE, DROP, ALTER, TRUNCATE, …), forbidden file-read functions (`read_csv_auto`, `read_parquet`, etc.), multi-statement detection, double-quoted identifier handling (not treated as keywords), comment stripping. Returns `SqlGuardrailsResult(IsValid, RejectionReason)`.
- **`SqlExecutorService.cs`** — Executes user SQL via `LiveMultiIntervalReader`. Validates via `SqlGuardrails`, auto-injects `LIMIT` if absent, wraps sync DuckDB in `Task.Run`, uses linked `CancellationTokenSource` for per-query timeout, returns `SqlExecutionResult` with state enum (`Rejected`, `Succeeded`, `Timeout`, `Failed`). `ExplainAsync` prefixes with `EXPLAIN`. `SetMemoryLimit` uses `PRAGMA memory_limit`.
- **`SqlSchemaService.cs`** — Schema introspection via `PRAGMA database_list` + `information_schema.tables` + `DESCRIBE`. Thread-safe via `SemaphoreSlim`. `GetAsync` caches result; `InvalidateAsync` clears cache. Wired to `IntervalSetTracker.SetChanged` in host builders.
- **`BundleLibraryService.cs`** — File-system metadata service. Reads immutable `metadata.json` (BundleManifest JSON) via private `AggregatorMetadata` deserializer (nested `timeRange` + `sessionContext`). Reads/writes user-editable `bundle-metadata.json`. Atomic writes via `.tmp` + `File.Move`. `ListAsync`, `UpdateMetadataAsync`, `RecordOpenedAsync`, `DeleteAsync`. Internal `ComputeDirectorySize` for SizeBytes.
- **`BundleExportService.cs`** — Streams bundle directory as zip (no temp file). Uses `ZipArchive` with `CompressionLevel.Fastest`, relative paths, skips path-traversal entries.
- **`BundleImportService.cs`** — Extracts bundle zip with validation. Zip-slip defense (`..`, rooted paths, leading `/` or `\`). Extension allow-list (`.parquet`, `.json`, `.db`). Reads `metadata.json` using `BundleManifest.SerializerOptions` (camelCase). Duplicate check. Extracts to `.import-{guid}` temp dir. Calls `BundleValidator.ValidateAsync`. Atomic `Directory.Move` to final location.
- **`ViewSqlTemplateService.cs`** — Generates SQL templates for 6 view types: `timeline`, `entity-history`, `causal`, `latency`, `gaps`, `topology`. `SqlEscape` replaces single quotes. `IsKnownView` check.

### New Endpoint Files: `src/Tracer.WebApi/Endpoints/`

- **`SqlEndpoints.cs`** — 4 endpoints:
  - `POST /api/sql/execute` — validate, clamp TimeoutSeconds (1–300), MaxRows (1–1M), return `SqlExecuteResultDto`. `Rejected` state returns HTTP 200.
  - `GET /api/sql/schema` — returns cached `SqlSchemaDto`.
  - `POST /api/sql/explain` — returns `SqlExplainResultDto`.
  - `GET /api/sql/view-template` — returns `ViewSqlTemplateResultDto`.
- **`SavedQueriesEndpoints.cs`** — 8 endpoints:
  - `GET /api/saved-queries` (filter by tag, author, favorite, builtIn)
  - `GET /api/saved-queries/{id}`
  - `POST /api/saved-queries`
  - `PUT /api/saved-queries/{id}` (405 for built-ins via caught `InvalidOperationException`)
  - `DELETE /api/saved-queries/{id}` (405 for built-ins)
  - `POST /api/saved-queries/{id}/favorite` — uses `ToggleFavoriteAsync` (works for built-ins)
  - `POST /api/saved-queries/{id}/clone`
  - `POST /api/saved-queries/{id}/run` — increments run count
- **`BundleLibraryEndpoints.cs`** — 6 endpoints:
  - `GET /api/bundles/library` (filter by archived, tag; sort by builtAt, sessionstart, size, label)
  - `PUT /api/bundles/{id}/metadata`
  - `POST /api/bundles/{id}/opened` (204)
  - `DELETE /api/bundles/{id}` (204)
  - `POST /api/bundles/import` (multipart or raw stream → BundleImportService; 201/409/400)
  - `GET /api/bundles/{id}/download` (streaming zip via BundleExportService)

### New DTO Files: `src/Tracer.WebApi/Contracts/Dto/`

- **`SqlDtos.cs`** — `SqlExecuteRequestDto`, `SqlExplainRequestDto`, `SqlExecuteResultDto`, `SqlColumnInfoDto`, `SqlSchemaDto`, `SqlTableInfoDto`, `SqlExplainResultDto`, `ViewSqlTemplateResultDto`, `SqlDtoMapper`.
- **`SavedQueryDto.cs`** — `SavedQueryDto`, `SavedQueryParameterDto`, `CreateSavedQueryDto`, `UpdateSavedQueryDto`, `CloneSavedQueryDto`, `SavedQueryDtoMapper`.
- **`BundleLibraryEntryDto.cs`** — `BundleLibraryEntryDto`, `UpdateBundleMetadataDto`, `BundleLibraryListDto`, `BundleLibraryDtoMapper`.

### Modified Host Builders

- **`src/Tracer.Observer/ObserverHostBuilder.cs`** — Added Phase 10 DI registrations (ISavedQueryStore, SqlExecutorConfig, SqlExecutorService, SqlSchemaService, ViewSqlTemplateService, BundleLibraryService, BundleExportService, BundleImportService). Added endpoint mapping for SqlEndpoints, SavedQueriesEndpoints, BundleLibraryEndpoints. Wired `IntervalSetTracker.SetChanged` → `SqlSchemaService.InvalidateAsync`. Added `BuiltInLoader.EnsureLoadedAsync` on `ApplicationStarted`.
- **`src/Tracer.OfflineViewer/OfflineViewerHostBuilder.cs`** — Same Phase 10 DI registrations using `config.LogFilePath` directory for bundles root. Wired `BundleIntervalSetTracker.SetChanged` → `SqlSchemaService.InvalidateAsync`. Added `BuiltInLoader.EnsureLoadedAsync` on `ApplicationStarted`.

### Modified Project Files

- **`src/Tracer.WebApi/Tracer.WebApi.csproj`** — Added `<ProjectReference>` to `Tracer.Storage.SavedQueries`.
- **`tests/Tracer.Tests.Unit/Tracer.Tests.Unit.csproj`** — Added `<ProjectReference>` to `Tracer.Storage.SavedQueries`.
- **`tests/Tracer.Tests.Integration/Tracer.Tests.Integration.csproj`** — Added `<ProjectReference>` to `Tracer.Storage.SavedQueries`.
- **`Tracer.sln`** — Added `Tracer.Storage.SavedQueries` project.

### Modified TestHarness

- **`src/Tracer.TestHarness/Observer/ObserverFixture.cs`** — Added Phase 10 service registrations and endpoint mapping (SqlEndpoints, SavedQueriesEndpoints, BundleLibraryEndpoints) so integration tests can exercise new endpoints.

---

## New Test Files

### Unit Tests: `tests/Tracer.Tests.Unit/`

| File | Tests |
|------|-------|
| `WebApi/SqlGuardrailsTests.cs` | 18 tests — SELECT/WITH/EXPLAIN/DESCRIBE/SHOW/VALUES allowed; INSERT/UPDATE/DELETE/DROP/CREATE/ALTER forbidden; multi-statement; comment stripping; double-quoted identifier; read_csv_auto/read_parquet forbidden |
| `WebApi/ViewTemplateEndpointsTests.cs` | 9 tests — all 6 view types generate SQL; unknown view throws; null args throw; topic in SQL; single-quote escaping |
| `SavedQueries/BuiltInQueriesServiceTests.cs` | 8 tests — populates built-ins, idempotent, all have labels, all SQL passes guardrails, ≥5 built-ins, non-empty SQL, cannot delete, can toggle favorite |
| `SavedQueries/SavedQueriesRoundTripTests.cs` | 11 tests — create/get, unique IDs, list all, filter by favorite/tag, update, delete, increment run count, get non-existent, toggle favorite, toggle non-existent |
| `WebApi/SavedQueryEndpointsTests.cs` | 12 tests — list empty, get 404, create valid, create missing label, update 404, delete 404, delete user query, favorite built-in, clone, run increments, delete built-in returns 405, list filter builtIn |
| `WebApi/BundleLibraryServiceTests.cs` | 9 tests — empty root, no-metadata skipped, returns bundle, update label, update non-existent, record opened, delete, delete non-existent, compute directory size |
| `WebApi/BundleExportServiceTests.cs` | 3 tests — non-existent false, creates zip, zip contains metadata |
| `WebApi/BundleImportServiceTests.cs` | 6 tests — missing manifest, zip-slip, valid (attempted), duplicate, not-a-zip, forbidden extension |
| `WebApi/BundleLibraryEndpointsTests.cs` | 12 tests — list empty, list with bundle, update metadata 404/OK, record opened 404/NoContent, delete 404/NoContent, download 404/FileStreamHttpResult, sort, filter archived |
| `WebApi/WiringTests.cs` | 8 tests — DI resolvability for all Phase 10 services; interface assignment check; config defaults |

**Total: 91 unit tests, all passing.**

### Integration Tests: `tests/Tracer.Tests.Integration/`

| File | Tests |
|------|-------|
| `SavedQueriesRoundTripTests.cs` | 3 tests — list returns built-ins, create/get round-trip, toggle favorite on built-in |
| `BundleLibraryRoundTripTests.cs` | 3 tests — library empty list, invalid zip returns 400, non-existent download returns 404 |

**Total: 6 integration tests, all passing.**

---

## Deviations from Batch Instructions

1. **DuckDB `Interrupt()` not available** — `DuckDBConnection` in `DuckDB.NET.Data 1.0.2` does not expose an `Interrupt()` method. The timeout mechanism relies on the linked `CancellationTokenSource` + `Task.Run` with `CancellationToken` passed to the task. Query timeout is enforced by discarding the result when `timeoutCts.IsCancellationRequested`; the underlying DuckDB query runs to completion before the pool slot is returned. This is a known limitation of the library version.

2. **`BundleManifest.SerializerOptions` used in `BundleImportService`** — The import service now correctly uses `BundleManifest.SerializerOptions` (camelCase) instead of default JSON options to ensure proper deserialization of the manifest.

3. **`AggregatorMetadata` private class redesigned** — The original design assumed flat fields in `metadata.json`. The actual `BundleManifest` JSON has nested `timeRange` and `sessionContext` objects, so `AggregatorMetadata` was redesigned with nested classes `AggregatorTimeRange` and `AggregatorSessionContext`.

4. **`OfflineViewerConfig.BundlesRoot` not added** — Instead of adding a new property to `OfflineViewerConfig`, the OfflineViewer bundles root is derived from `config.LogFilePath` directory (same pattern used for other data storage in that builder), avoiding an unrelated config change.

5. **`ISavedQueryStore.ToggleFavoriteAsync` added** — Not in the original interface design but required to allow favorite toggling on built-in queries (which `UpdateAsync` forbids). This direct SQL `UPDATE` bypasses the IsBuiltIn guard cleanly.
