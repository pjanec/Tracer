# BATCH-43 Instructions

**Batch:** BATCH-43  
**Tasks:** TRC-P8-004 (SavedViews Assembly), TRC-P8-005 (Annotation REST API + DI wiring), TRC-P8-006 (Saved Views REST API), TRC-P8-009 (AnnotationsExporter)  
**Phase:** 8 — Annotations, Saved Views, Trigger Evaluation Log, Multi-Persona Polish  
**Estimated Effort:** 14–16 hours  
**Dependencies:** BATCH-42 complete (Tracer.Storage.Annotations already exists)  
**Report path:** `d:\WORK\Tracer\.dev\tracer\reports\BATCH-43-REPORT.md`

---

## 📋 Onboarding & Workflow

### Required Reading (IN ORDER)

1. **Design:** `docs/tracer_phase8_design.md` — §3 (annotation model recap), §4 (Annotation Web API), §6.1-6.4 (Saved Views), §3.7 (AnnotationsExporter)
2. **Task Details:** `docs/TASK-DETAIL.md` — §TRC-P8-004, §TRC-P8-005, §TRC-P8-006, §TRC-P8-009 (all success conditions)
3. **Previous Review:** `.dev/tracer/reviews/BATCH-42-REVIEW.md`
4. **Existing Storage:** `src/Tracer.Storage.Annotations/` — understand the pattern, SqliteAnnotationStore uses same SQLite file approach
5. **ObserverHostBuilder:** `src/Tracer.Observer/ObserverHostBuilder.cs` — understand current DI structure and endpoint registration
6. **OfflineViewerHostBuilder:** `src/Tracer.OfflineViewer/OfflineViewerHostBuilder.cs` — understand structure
7. **AggregationOrchestrator:** `src/Tracer.Aggregator/AggregationOrchestrator.cs` — understand the step sequence (steps 1-9)
8. **AggregationStage:** `src/Tracer.Aggregator/Progress/AggregationStage.cs` — add `AnnotationsExported` enum value
9. **Existing endpoint pattern:** `src/Tracer.WebApi/Endpoints/EntityEndpoints.cs` — study the endpoint pattern, typed results, DI injection

### Key existing files to study

- `src/Tracer.Storage.Annotations/SqliteAnnotationStore.cs` — replicate the SQLite pattern for SavedViews
- `src/Tracer.WebApi/Contracts/Dto/EntityDtos.cs` — DTO naming conventions
- `src/Tracer.WebApi/Endpoints/EntityEndpoints.cs` — endpoint handler pattern
- `tests/Tracer.Tests.Unit/Annotations/SqliteAnnotationStoreTests.cs` — test patterns

### DO NOT STOP — Complete everything before writing the report

Do not ask for confirmation to run tests or build. Do not leave work incomplete. Fix everything until all tests pass, then write the report.

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

1. **TRC-P8-004:** Implement SavedViews assembly → write tests → ALL tests pass ✅  
2. **TRC-P8-005:** Implement Annotation REST API + DI → write tests → ALL tests pass ✅  
3. **TRC-P8-006:** Implement Saved Views REST API → write tests → ALL tests pass ✅  
4. **TRC-P8-009:** Implement AnnotationsExporter + wiring → write tests → ALL tests pass ✅  

Do NOT move to the next task until ALL tests pass.

---

## ✅ Task 1 — TRC-P8-004: Tracer.Storage.SavedViews Assembly

**Design reference:** `docs/tracer_phase8_design.md` §6.1, §6.2  
**Task definition:** `docs/TASK-DETAIL.md` §TRC-P8-004

### 1.1 Create `src/Tracer.Storage.SavedViews/Tracer.Storage.SavedViews.csproj`

Same pattern as `Tracer.Storage.Annotations.csproj`. References:
- `Tracer.Core`
- `Microsoft.Data.Sqlite` (already in Directory.Packages.props)
- `Microsoft.Extensions.Logging.Abstractions`
- `Ulid`
- `InternalsVisibleTo` → `Tracer.Tests.Unit`

### 1.2 Types to create (see design §6.1 for exact field list):

**`src/Tracer.Storage.SavedViews/SavedViewKind.cs`**
```csharp
namespace Tracer.Storage.SavedViews;
public enum SavedViewKind { SavedView, Bookmark }
```

**`src/Tracer.Storage.SavedViews/SavedViewRecord.cs`** — sealed record, 12 fields:
- `SavedViewId` (required string)
- `SessionId` (required string)
- `Kind` (required SavedViewKind)
- `ViewType` (required string) — "timeline" | "causal-tree" | "entity-history" | "scenario" | "trigger-eval"
- `Url` (required string) — full path + query, relative
- `Label` (required string)
- `Description` (string?)
- `Persona` (required string) — "engineer" | "scenario-author" | "operator"
- `Author` (string?)
- `CreatedAtUtc` (required DateTimeOffset)
- `LastOpenedAtUtc` (DateTimeOffset?)
- `OpenCount` (required int)

**`src/Tracer.Storage.SavedViews/SavedViewFilter.cs`** — sealed record:
- `SessionId` (string?)
- `Kind` (SavedViewKind?)
- `ViewType` (string?)
- `Persona` (string?)
- `OrderBy` (string) — defaults to `"created"` (`"created"` = created_at DESC; `"recent"` = last_opened_at DESC nulls last)
- `Limit` (int) — defaults to `100`

**`src/Tracer.Storage.SavedViews/ISavedViewStore.cs`**
```csharp
public interface ISavedViewStore
{
    Task<IReadOnlyList<SavedViewRecord>> ListAsync(SavedViewFilter filter, CancellationToken ct);
    Task<SavedViewRecord?> GetAsync(string savedViewId, CancellationToken ct);
    Task<SavedViewRecord> CreateAsync(SavedViewRecord record, CancellationToken ct);
    Task<SavedViewRecord?> UpdateAsync(SavedViewRecord record, CancellationToken ct);
    Task<bool> DeleteAsync(string savedViewId, CancellationToken ct);
    Task RecordOpenedAsync(string savedViewId, CancellationToken ct);
}
```

**`src/Tracer.Storage.SavedViews/Schema/SavedViewsSchema.cs`** — SQL DDL:
```sql
CREATE TABLE IF NOT EXISTS saved_views (
    saved_view_id  TEXT PRIMARY KEY,
    session_id     TEXT NOT NULL,
    kind           TEXT NOT NULL,
    view_type      TEXT NOT NULL,
    url            TEXT NOT NULL,
    label          TEXT NOT NULL,
    description    TEXT,
    persona        TEXT NOT NULL,
    author         TEXT,
    created_at     TEXT NOT NULL,
    last_opened_at TEXT,
    open_count     INTEGER NOT NULL DEFAULT 0
);
CREATE INDEX IF NOT EXISTS idx_saved_views_session_persona ON saved_views (session_id, persona);
CREATE INDEX IF NOT EXISTS idx_saved_views_session_kind    ON saved_views (session_id, kind);
CREATE INDEX IF NOT EXISTS idx_saved_views_last_opened     ON saved_views (last_opened_at);
```

**`src/Tracer.Storage.SavedViews/SqliteSavedViewStore.cs`** — same pattern as SqliteAnnotationStore:
- Constructor: `(string dbPath, ILogger<SqliteSavedViewStore> logger)`
- `InitializeAsync(CancellationToken ct = default)` — splits DDL on `;` and executes each statement
- `_writeLock = new SemaphoreSlim(1, 1)` for Create/Update/Delete/RecordOpened
- `ListAsync` — builds WHERE clause from filter; ORDER BY: `"recent"` → `last_opened_at DESC NULLS LAST, created_at DESC`; default → `created_at DESC`; LIMIT = `Math.Min(filter.Limit, 500)`
- `RecordOpenedAsync` — `UPDATE saved_views SET open_count = open_count + 1, last_opened_at = $now WHERE saved_view_id = $id`
- `CreateAsync` — generates ULID when `SavedViewId` is empty; sets `CreatedAtUtc = UtcNow` when `default`
- `MapRecord` — reads 12 columns by ordinal

### 1.3 Wire into solution

```powershell
dotnet sln d:\Work\Tracer\Tracer.sln add src\Tracer.Storage.SavedViews\Tracer.Storage.SavedViews.csproj
```

Add `<ProjectReference Include="..\..\src\Tracer.Storage.SavedViews\Tracer.Storage.SavedViews.csproj" />` to `tests/Tracer.Tests.Unit/Tracer.Tests.Unit.csproj`

### 1.4 Tests — `tests/Tracer.Tests.Unit/SavedViews/SqliteSavedViewStoreTests.cs`

10 tests covering all TRC-P8-004 success conditions. Use `IDisposable` with a temp dir.

**Required test scenarios** (map to SC-1 through SC-10 in TASK-DETAIL.md):
1. `SchemaInitialization_IsIdempotent` — calls InitializeAsync twice; no exception; checks `saved_views` table and 3 indexes in sqlite_master
2. `CreateAsync_AssignsUlid_WhenIdEmpty` — `SavedViewId = ""` → result has 26-char ULID
3. `RecordOpenedAsync_IncrementsOpenCount` — create view → RecordOpened → GetAsync → `OpenCount == 1` and non-null `LastOpenedAtUtc`
4. `RecordOpenedAsync_CalledTwice_OpenCountIsTwo` — RecordOpened twice → `OpenCount == 2`
5. `FilterByPersona_ReturnsOnlyMatchingPersona` — engineer + scenario-author entries; filter by engineer → only engineer results; verify `OnlyContain`
6. `FilterByKind_ReturnsOnlyBookmarks` — 1 SavedView + 2 Bookmarks; filter by Bookmark → count == 2
7. `UpdateAsync_UpdatesLabelAndDescription` — create with "old"; update with `Label = "new"`, `Description = "desc"`; GetAsync verifies new values
8. `ListAsync_OrderByCreated_Descending` — create 3 with explicit DateTimeOffset values; list → first item has latest CreatedAtUtc
9. `ListAsync_OrderByRecent_NullsLast` — view A with LastOpenedAtUtc set, view B with null; orderBy=recent → A before B
10. `DeleteAsync_RemovesRecord` — create → delete → GetAsync returns null

---

## ✅ Task 2 — TRC-P8-005: Annotation REST API Endpoints + DI Wiring

**Design reference:** `docs/tracer_phase8_design.md` §4.1–§4.4  
**Task definition:** `docs/TASK-DETAIL.md` §TRC-P8-005

### 2.1 Add project references in consumer projects

Add `Tracer.Storage.Annotations` project reference to:
- `src/Tracer.Observer/Tracer.Observer.csproj`
- `src/Tracer.OfflineViewer/Tracer.OfflineViewer.csproj`
- `src/Tracer.WebApi/Tracer.WebApi.csproj`
- `tests/Tracer.Tests.Unit/Tracer.Tests.Unit.csproj` (already done in BATCH-42)

### 2.2 Create DTOs — `src/Tracer.WebApi/Contracts/Dto/AnnotationDtos.cs`

Three records as specified in design §4.3: `AnnotationDto`, `CreateAnnotationDto`, `UpdateAnnotationDto`.

### 2.3 Create `src/Tracer.WebApi/Contracts/Dto/AnnotationDtoMapper.cs`

Static mapper class with:
- `Map(AnnotationRecord r) → AnnotationDto`
- `FromCreate(CreateAnnotationDto dto) → AnnotationRecord` — sets `AnnotationId = ""`, `CreatedAtUtc = default` (store fills these)

For `Kind` in `Map`: use `r.Kind.ToString()`.  
For `FromCreate`: parse `dto.Kind` via `Enum.Parse<AnnotationKind>(dto.Kind, true)`.

### 2.4 Create `src/Tracer.WebApi/Endpoints/AnnotationEndpoints.cs`

Five endpoints as specified in design §4.2:
- `GET /api/annotations` — list with filter params
- `POST /api/annotations` — create; returns 201 + Location header
- `GET /api/annotations/{annotationId}` — get one
- `PUT /api/annotations/{annotationId}` — update (patch semantics: only non-null DTO fields applied)
- `DELETE /api/annotations/{annotationId}` — delete

**Important implementation notes:**
- `[FromQuery] string? sessionId` for list — `sessionId` is optional in the filter (caller may omit for global list)
- `limit` clamped to `[1, 5000]`
- `ValidateCreate`: rejects empty Body, empty SessionId, and any count of target identifiers ≠ 1
- Bundle-mode 405: catch `InvalidOperationException` from `BundleAnnotationStore`, return `ProblemDetails` with `Status = 405`
- All handlers: `[FromServices] IAnnotationStore store` injected via minimal API DI
- Apply `.WithOpenApi()` to all routes

### 2.5 Create `src/Tracer.WebApi/Endpoints/LazyBundleAnnotationStore.cs`

(Place in `src/Tracer.WebApi/Bundles/` or `src/Tracer.OfflineViewer/`) — wraps `BundleOpenManager.Current?.WorkingDirectory`:

```csharp
public sealed class LazyBundleAnnotationStore : IAnnotationStore
{
    private readonly BundleOpenManager _mgr;
    public LazyBundleAnnotationStore(BundleOpenManager mgr) { _mgr = mgr; }

    private IAnnotationStore? Resolve() =>
        _mgr.Current is { } c ? new BundleAnnotationStore(c.WorkingDirectory) : null;

    public Task<IReadOnlyList<AnnotationRecord>> ListAsync(AnnotationFilter f, CancellationToken ct) =>
        Resolve() is { } s ? s.ListAsync(f, ct)
            : Task.FromResult<IReadOnlyList<AnnotationRecord>>(Array.Empty<AnnotationRecord>());

    public Task<AnnotationRecord?> GetAsync(string id, CancellationToken ct) =>
        Resolve() is { } s ? s.GetAsync(id, ct) : Task.FromResult<AnnotationRecord?>(null);

    public Task<AnnotationRecord> CreateAsync(AnnotationRecord r, CancellationToken ct) =>
        throw new InvalidOperationException("Bundle annotations are read-only");

    public Task<AnnotationRecord?> UpdateAsync(AnnotationRecord r, CancellationToken ct) =>
        throw new InvalidOperationException("Bundle annotations are read-only");

    public Task<bool> DeleteAsync(string id, CancellationToken ct) =>
        throw new InvalidOperationException("Bundle annotations are read-only");

    public Task<IReadOnlyList<AnnotationRecord>> ExportAllForSessionAsync(string sessionId, CancellationToken ct) =>
        Resolve() is { } s ? s.ExportAllForSessionAsync(sessionId, ct)
            : Task.FromResult<IReadOnlyList<AnnotationRecord>>(Array.Empty<AnnotationRecord>());
}
```

### 2.6 Wire `IAnnotationStore` in `ObserverHostBuilder.cs`

Add to the DI registrations (after Entity history services, before Live streaming):

```csharp
// ── Annotations (Phase 8) ─────────────────────────────────────────────────
builder.Services.AddSingleton<IAnnotationStore>(sp =>
{
    var cfg = sp.GetRequiredService<ObserverConfig>();
    var path = Path.Combine(cfg.DataRoot, "annotations.db");
    var store = new SqliteAnnotationStore(path, sp.GetRequiredService<ILogger<SqliteAnnotationStore>>());
    store.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
    return store;
});
```

Add `AnnotationEndpoints.Map(app)` after `EntityEndpoints.Map(app)`.

Add `using Tracer.Storage.Annotations;` to ObserverHostBuilder.cs imports.

### 2.7 Wire `IAnnotationStore` in `OfflineViewerHostBuilder.cs`

After entity history services:
```csharp
// ── Annotations (Phase 8) ─────────────────────────────────────────────────
builder.Services.AddSingleton<IAnnotationStore, LazyBundleAnnotationStore>();
```

Add `AnnotationEndpoints.Map(app)` after `EntityEndpoints.Map(app)`.

### 2.8 Tests — `tests/Tracer.Tests.Unit/WebApi/AnnotationEndpointsTests.cs`

13 tests covering all TRC-P8-005 success conditions SC-1 through SC-13.

**Test infrastructure:** Use `Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory` or test the handler methods directly with a mock `IAnnotationStore` (the direct-handler approach is simpler and faster). Use `SqliteAnnotationStore` for tests that need real persistence; use a stub that throws `InvalidOperationException` for bundle-mode tests.

For direct handler invocation, call the static `HandleListAsync`, `HandleCreateAsync`, etc. methods — they're `public static`.

**Required test scenarios:**
1. `POST_ValidRequest_Returns201Created` — create event annotation; assert HTTP 201, Location header, non-empty annotationId
2. `POST_EmptyBody_Returns400` — body = ""; assert 400 ProblemDetails
3. `POST_MultipleTargetIdentifiers_Returns400` — eventId + entityId both set; assert 400
4. `POST_NoTargetIdentifier_Returns400` — all targets null; assert 400
5. `POST_BundleMode_Returns405` — stub store throws `InvalidOperationException("read-only")`; assert 405 ProblemDetails
6. `PUT_NonExistentId_Returns404`
7. `PUT_BundleMode_Returns405`
8. `DELETE_NonExistentId_Returns404`
9. `DELETE_BundleMode_Returns405`
10. `GET_List_FiltersBySessionId` — two sessions; list session-A; assert all results are session-A
11. `GET_Single_Returns200WithDto` — create annotation; GET by id; assert 200 with matching annotationId
12. `GET_Single_UnknownId_Returns404`
13. `DI_Observer_RegistersSqliteAnnotationStore` — build Observer DI container; resolve `IAnnotationStore`; assert instance of `SqliteAnnotationStore`

**For SC-13:** Construct a minimal DI container without starting the full WebHost. Use `WebApplication.CreateBuilder()`, copy the DI registrations for `IAnnotationStore` from `ObserverHostBuilder`, build the ServiceProvider, and verify resolution.

---

## ✅ Task 3 — TRC-P8-006: Saved Views REST API Endpoints

**Design reference:** `docs/tracer_phase8_design.md` §6.4  
**Task definition:** `docs/TASK-DETAIL.md` §TRC-P8-006

### 3.1 Add project references

Add `Tracer.Storage.SavedViews` project reference to:
- `src/Tracer.Observer/Tracer.Observer.csproj`
- `src/Tracer.OfflineViewer/Tracer.OfflineViewer.csproj`
- `src/Tracer.WebApi/Tracer.WebApi.csproj`
- `tests/Tracer.Tests.Unit/Tracer.Tests.Unit.csproj`

### 3.2 Create DTOs — `src/Tracer.WebApi/Contracts/Dto/SavedViewDtos.cs`

Three records:
- `SavedViewDto` — all 12 fields from `SavedViewRecord` (camelCase in JSON)
- `CreateSavedViewDto` — required: sessionId, kind (string), viewType, url, label, persona; optional: description, author
- `UpdateSavedViewDto` — optional: label, description (only non-null fields applied in PUT handler)

`SavedViewDtoMapper` — static class with `Map(SavedViewRecord) → SavedViewDto` and `FromCreate(CreateSavedViewDto) → SavedViewRecord`.

### 3.3 Create `src/Tracer.WebApi/Endpoints/SavedViewEndpoints.cs`

Six endpoints as in design §6.4:
- `GET /api/saved-views` — list with sessionId, persona, kind, orderBy, limit params
- `POST /api/saved-views` — create; 201 + Location
- `GET /api/saved-views/{id}` — get one
- `PUT /api/saved-views/{id}` — update label/description only
- `DELETE /api/saved-views/{id}` — 204 on success
- `POST /api/saved-views/{id}/opened` — 204 always (fire-and-forget; ignore unknown IDs)

**Constraints:**
- `limit` clamped to `[1, 500]`
- `POST /opened`: always returns 204, regardless of whether ID exists

### 3.4 Wire `ISavedViewStore` in both host builders

Same pattern as annotations: `SqliteSavedViewStore` in Observer (same `annotations.db` file), `LazyBundleSavedViewStore` (or simply no-op store) in OfflineViewer.

**For OfflineViewer**: Create `LazyBundleSavedViewStore` similarly to `LazyBundleAnnotationStore` — reads from a bundle's `saved_views.json` if present. Since bundle saved views export is TRC-P8-009 scope, for now just serve an empty list when bundle has no saved_views.json. Write attempts throw `InvalidOperationException("Bundle saved views are read-only")`.

Add `SavedViewEndpoints.Map(app)` to both host builders.

### 3.5 Tests — `tests/Tracer.Tests.Unit/WebApi/SavedViewEndpointsTests.cs`

10 tests covering SC-1 through SC-10 in TASK-DETAIL.md §TRC-P8-006. Same direct-handler pattern as AnnotationEndpointsTests.

---

## ✅ Task 4 — TRC-P8-009: AnnotationsExporter + AggregationOrchestrator Wiring

**Design reference:** `docs/tracer_phase8_design.md` §3.7  
**Task definition:** `docs/TASK-DETAIL.md` §TRC-P8-009

### 4.1 Add `AnnotationsExported` to AggregationStage enum

In `src/Tracer.Aggregator/Progress/AggregationStage.cs`, add after `MetadataWritten`:

```csharp
/// <summary>Annotations (user notes) have been exported into the bundle's annotations/ directory.</summary>
AnnotationsExported,
```

### 4.2 Create `src/Tracer.Aggregator/Consolidation/AnnotationsExporter.cs`

```csharp
namespace Tracer.Aggregator.Consolidation;

public static class AnnotationsExporter
{
    public static async Task ExportAsync(
        IAnnotationStore liveStore,
        string sessionId,
        string bundleStagingPath,
        CancellationToken ct)
    {
        var annotations = await liveStore.ExportAllForSessionAsync(sessionId, ct);
        if (annotations.Count == 0) return;

        var annotationsDir = Path.Combine(bundleStagingPath, "annotations");
        Directory.CreateDirectory(annotationsDir);
        var path = Path.Combine(annotationsDir, "annotations.json");

        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, annotations,
            new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true,
                Converters = { new JsonStringEnumConverter() },
            }, ct);
    }
}
```

Add `Tracer.Storage.Annotations` project reference to `src/Tracer.Aggregator/Tracer.Aggregator.csproj`.

### 4.3 Wire into AggregationOrchestrator

Modify `AggregationOrchestrator`:
1. Add optional `IAnnotationStore? _annotationStore` field
2. Add optional constructor parameter or overload for `IAnnotationStore?`
3. Insert after step 7 (MetadataWritten), before step 8 (manifest):

```csharp
// 7b. Export annotations (if live store provided)
if (_annotationStore is not null)
{
    await AnnotationsExporter.ExportAsync(
        _annotationStore, request.SessionId ?? "", staging.BundleStagingPath, ct);
    progress?.Report(AggregationStage.AnnotationsExported, "Annotations exported into bundle");
}
```

**Important**: The `AggregationOrchestrator` must remain backward-compatible — existing callers that don't pass `IAnnotationStore` must still work without annotations export.

Update `ObserverHostBuilder.cs` to pass the `IAnnotationStore` to `AggregationOrchestrator`:
```csharp
builder.Services.AddSingleton<IAggregationOrchestrator>(sp =>
    new AggregationOrchestrator(
        sp.GetRequiredService<ITelemetryStorageReader>(),
        sp.GetRequiredService<ILogger<AggregationOrchestrator>>(),
        sp.GetRequiredService<IAnnotationStore>()));  // ← add this
```

### 4.4 Tests — `tests/Tracer.Tests.Unit/Aggregator/AnnotationsExporterTests.cs`

7 tests covering SC-1 through SC-7 in TASK-DETAIL.md §TRC-P8-009:

1. `ExportAsync_NoAnnotations_DoesNotCreateFile` — empty store → no file
2. `ExportAsync_WithAnnotations_WritesJsonFile` — 3 annotations → file exists; deserialize → 3 records with matching fields
3. `ExportAsync_FiltersToTargetSession` — session-A and session-B annotations; export session-A → only session-A in JSON
4. `ExportAsync_OutputPathMatchesBundleAnnotationStore` — path check
5. `AggregationStage_AnnotationsExported_EnumValueExists` — `Enum.IsDefined(typeof(AggregationStage), AggregationStage.AnnotationsExported)` is true
6. `AggregationOrchestrator_WithAnnotationStore_CallsExporter` — use a mock/stub `IAnnotationStore` that records calls; run orchestrator; verify `ExportAllForSessionAsync` was called and `AnnotationsExported` stage reported
7. `AggregationOrchestrator_WithoutAnnotationStore_SkipsExport` — null store; run orchestrator; verify `AnnotationsExported` stage never reported; no exception

**Note for SC-6 and SC-7:** To run `AggregationOrchestrator.RunAsync` in tests you need a real `ITelemetryStorageReader`. Use the existing pattern from `BundleRoundTripTests` or `AggregatorIntegrationTests` (check `tests/Tracer.Tests.Unit/Aggregator/` for existing patterns). If no easy integration fixture exists, use a stub `ITelemetryStorageReader` that returns no intervals — in that case the orchestrator will throw `InvalidOperationException("No intervals found")` before it reaches the annotation export step. To test SC-6 properly, mock the orchestrator's internal dependencies or write the test as a unit test that creates a spy `IAnnotationStore` and verifies `ExportAsync` is called; the AggregationOrchestrator is the public surface to test through. 

A simpler approach for SC-6/SC-7: Test `AnnotationsExporter.ExportAsync` in isolation (SC-1 to SC-4 cover that) and use separate integration tests for full orchestrator wiring. Mark SC-6/SC-7 as integration-level tests in `Tracer.Tests.Integration` if direct unit testing proves impractical due to the NAS dependency.

---

## 🧪 Testing Requirements

### Verification Commands

```powershell
# Kill stale testhost
Get-Process -Name "testhost" -ErrorAction SilentlyContinue | Stop-Process -Force

# Build
dotnet build d:\Work\Tracer\Tracer.sln -c Release --no-incremental 2>&1 | Select-Object -Last 10

# Run new tests by category
dotnet test d:\Work\Tracer\tests\Tracer.Tests.Unit -c Release --no-build --filter "FullyQualifiedName~SavedViews" 2>&1 | Select-Object -Last 15
dotnet test d:\Work\Tracer\tests\Tracer.Tests.Unit -c Release --no-build --filter "FullyQualifiedName~AnnotationEndpoints" 2>&1 | Select-Object -Last 15
dotnet test d:\Work\Tracer\tests\Tracer.Tests.Unit -c Release --no-build --filter "FullyQualifiedName~SavedViewEndpoints" 2>&1 | Select-Object -Last 15
dotnet test d:\Work\Tracer\tests\Tracer.Tests.Unit -c Release --no-build --filter "FullyQualifiedName~AnnotationsExporter" 2>&1 | Select-Object -Last 15

# Full suite (no regressions)
dotnet test d:\Work\Tracer\tests\Tracer.Tests.Unit -c Release --no-build --filter "FullyQualifiedName!~Publish_ProducesExpectedLayout" --logger "console;verbosity=minimal" 2>&1 | Select-Object -Last 20
```

### Expected Results

- **TRC-P8-004:** 10 SavedViews tests passing
- **TRC-P8-005:** 13 AnnotationEndpoints tests passing
- **TRC-P8-006:** 10 SavedViewEndpoints tests passing
- **TRC-P8-009:** 5-7 AnnotationsExporter tests passing
- **Full suite:** all previously-passing tests still pass (169 → ~207 new total)
- **Build:** 0 errors, 0 warnings (Release, `TreatWarningsAsErrors=true`)

---

## ⚠️ Quality Standards

**❗ TEST QUALITY EXPECTATIONS**
- Tests must verify actual behavior: real SQLite I/O, real HTTP status codes, real field values
- NOT ACCEPTABLE: "it compiled" tests, `Assert.NotNull(result)` only, tests that don't actually exercise the feature
- REQUIRED: verify concrete values — annotationId is 26 chars, OpenCount == 2, response DTO has correct sessionId
- REQUIRED: test SQL injection safety for any new parameterized SQL

**❗ NO SQL INJECTION**
- All user data via `$parameter` bindings in all SQLite queries
- Never use string interpolation in SQL statements

**❗ BACKWARD COMPATIBILITY**
- `AggregationOrchestrator` existing tests must still pass
- Both `AggregationOrchestrator(nasReader)` and `AggregationOrchestrator(nasReader, logger)` constructors must still work

---

## 📊 Report Requirements

Write your report to: `d:\WORK\Tracer\.dev\tracer\reports\BATCH-43-REPORT.md`

Include:
- Table of files created/modified
- Test results per task (counts + pass/fail)
- Build status
- Issues encountered and resolutions
- Design decisions made beyond the spec
- Weak points spotted
- Suggested git commit message
