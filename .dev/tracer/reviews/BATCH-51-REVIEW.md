# BATCH-51 Review — Phase 10 Backend

**Batch:** BATCH-51  
**Tasks:** TRC-P10-001 through TRC-P10-010  
**Reviewer:** Dev Lead  
**Verdict:** ✅ APPROVED

---

## Review Checklist

### TRC-P10-001 — `SqlGuardrails` + `SqlExecutorService` + `SqlSchemaService` ✅

- Hand-rolled tokenizer correctly handles comment stripping before keyword detection ✅
- Double-quoted identifiers (`"INSERT"`) not falsely rejected ✅
- All forbidden keywords rejected: INSERT, UPDATE, DELETE, DROP, CREATE, ALTER, TRUNCATE, ATTACH, DETACH, COPY, EXPORT, IMPORT, LOAD, INSTALL, FORCE, PRAGMA ✅
- File-read functions (`read_csv_auto`, `read_parquet`) rejected ✅
- Multi-statement (`;` outside strings) rejected ✅
- `SqlExecutorService` leases from `LiveMultiIntervalReader.AcquireAsync` (no new connection) ✅
- Timeout via linked CTS + Task.Run (DuckDB.NET limitation documented) ✅
- Row limit auto-injected when absent ✅
- `SqlSchemaService` caches and invalidates on `IntervalSetTracker.SetChanged` ✅
- 18 guardrails + 7 executor + 3 schema tests = 28 tests ✅

### TRC-P10-002 — SQL API Endpoints ✅

- `POST /api/sql/execute` returns HTTP 200 for all states including `Rejected` ✅
- Empty/whitespace SQL → HTTP 400 ✅
- `GET /api/sql/schema` returns cached schema ✅
- `POST /api/sql/explain` — forbidden SQL → HTTP 400, valid SQL → HTTP 200 ✅
- `GET /api/sql/view-template` → view template SQL ✅
- All endpoints `.WithOpenApi()` ✅
- 12 endpoint unit tests ✅

### TRC-P10-003 — `Tracer.Storage.SavedQueries` ✅

- New project created and added to solution ✅
- `SqliteSavedQueryStore` uses `annotations.db` (shared with annotation/saved-views stores) ✅
- `UpdateAsync`/`DeleteAsync` throw `InvalidOperationException` for built-in queries ✅
- `ToggleFavoriteAsync` added (bypasses built-in guard — needed for favoriting built-ins) ✅
- Parameters and tags round-trip as JSON ✅
- 11 store round-trip tests ✅

### TRC-P10-004 — Saved Queries API Endpoints ✅

- All 8 endpoints implemented ✅
- PUT/DELETE on built-ins → HTTP 405 ✅
- Clone creates new ULID record with `IsBuiltIn=false` ✅
- Favorite toggle works for built-ins (uses `ToggleFavoriteAsync`) ✅
- Empty label → HTTP 400 ✅
- Non-existent ID → HTTP 404 ✅
- 12 endpoint tests ✅

### TRC-P10-005 — Built-In Saved Queries ✅

- 5 built-in queries: top-topics, events-by-trace, event-counts-per-node, latency-distribution, entity-events ✅
- All SQL passes `SqlGuardrails.Validate` ✅
- `BuiltInLoader.EnsureLoadedAsync` is idempotent (checks by ID) ✅
- 8 built-in loader tests ✅

### TRC-P10-006 — `BundleLibraryService` ✅

- Reads immutable `metadata.json` (nested `timeRange`/`sessionContext` correctly handled) ✅
- Reads/writes user-editable `bundle-metadata.json` (atomic write via temp file) ✅
- Partial update preserves existing fields ✅
- `UpdateMetadataAsync` does NOT touch `metadata.json` ✅
- `DeleteAsync` returns false if directory not found ✅
- `SizeBytes` sums all nested files ✅
- 9 unit tests + 1 integration test ✅

### TRC-P10-007 — Bundle Library API Endpoints ✅

- `GET /api/bundles/library` filters archived (default excludes) ✅
- Sort by `builtAt|sessionStart|size|label` ✅
- Filter by tag ✅
- Import → delegates to `BundleImportService` (201/409/400) ✅
- Export → streaming zip via `BundleExportService` ✅
- 12 endpoint tests ✅

### TRC-P10-008 — Bundle Import/Export ✅

- Export streams directly to response (no temp file on disk) ✅
- Import: zip-slip defense (`..` and rooted paths rejected) ✅
- Import: extension allow-list (`.parquet`, `.json`, `.db` only) ✅
- Import: atomic move (temp dir → final) ✅
- Import: `BundleManifest.SerializerOptions` (camelCase) used correctly ✅
- Duplicate import returns `AlreadyExists` ✅
- 3 export tests + 6 import tests ✅

### TRC-P10-009 — View SQL Template ✅

- 6 view types: `timeline`, `entity-history`, `causal`, `latency`, `gaps`, `topology` ✅
- Single-quote escaping for SQL injection defense ✅
- Unknown view → HTTP 400 ✅
- All generated SQL passes `SqlGuardrails.Validate` ✅
- 9 endpoint tests ✅

### TRC-P10-010 — DI Wiring ✅

- Both `ObserverHostBuilder` and `OfflineViewerHostBuilder` updated ✅
- `BuiltInLoader.EnsureLoadedAsync` called on `ApplicationStarted` ✅
- `SqlSchemaService.InvalidateAsync` wired to `IntervalSetTracker.SetChanged` ✅
- All Phase 10 services resolvable (verified by WiringTests) ✅
- Phase 10 endpoints mapped in all host builders ✅
- `ObserverFixture` updated for integration tests ✅
- 8 wiring tests ✅

---

## Notable Decisions

**DuckDB timeout caveat**: `DuckDB.NET.Data 1.0.2` has no `Interrupt()` method. The timeout mechanism relies on linked `CancellationTokenSource` + `Task.Run`. The underlying query continues running until DuckDB finishes, but the caller receives a `Timeout` result immediately. This is documented in the report and is an acceptable limitation for Phase 10 scope.

**`ToggleFavoriteAsync` addition**: Not in the original ISavedQueryStore spec but needed to allow users to favorite built-in queries without hitting the "read-only" guard. Clean implementation via direct SQL UPDATE.

**`AggregatorMetadata` nested classes**: Correctly handles the real `metadata.json` structure (nested `timeRange`/`sessionContext` objects) rather than flat fields.

---

## Test Summary

| Suite | Count |
|---|---|
| Backend unit (Phase 10) | 91 ✅ |
| Integration tests (Phase 10) | 6 ✅ |
| Build | 0 errors, 0 warnings ✅ |

**APPROVED — TRC-P10-001 through TRC-P10-010 complete.**
