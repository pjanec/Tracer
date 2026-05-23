# BATCH-58 — Backend: SavedViews Export, Slow-State Bundle Fix, SharedMemory Drops, Health Metrics

## Overview

Four backend fixes. No frontend changes. All tasks require unit tests.

**Excluded test** (file-lock issue since BATCH-22): Always run tests with:
```
dotnet test --filter "FullyQualifiedName!~Publish_ProducesExpectedLayout"
```

---

## Task B1 — Export Saved Views in Aggregator

### Goal
The aggregator already exports annotations into `annotations/annotations.json`. It must also export saved views into `saved_views/saved_views.json` when a live `ISavedViewStore` is provided.

### Files to modify / create

#### 1. `src/Tracer.Aggregator/Progress/AggregationStage.cs`

Add `SavedViewsExported` after `AnnotationsExported`:

```csharp
/// <summary>Annotations (user notes) have been exported into the bundle's annotations/ directory.</summary>
AnnotationsExported,

/// <summary>Saved views have been exported into the bundle's saved_views/ directory.</summary>
SavedViewsExported,

/// <summary>Checksums and manifest have been computed and written.</summary>
ManifestWritten,
```

#### 2. Create `src/Tracer.Aggregator/Consolidation/SavedViewsExporter.cs`

Model it after `AnnotationsExporter.cs` (which you should read for context). The pattern:
- Call `liveStore.ListAsync(new SavedViewFilter { SessionId = sessionId, Limit = int.MaxValue }, ct)`
- If no records, return early (no file written)
- Otherwise, create `saved_views/` subdirectory under `bundleStagingPath` and write `saved_views.json`

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using Tracer.Storage.SavedViews;

namespace Tracer.Aggregator.Consolidation;

public static class SavedViewsExporter
{
    public static async Task ExportAsync(
        ISavedViewStore liveStore,
        string sessionId,
        string bundleStagingPath,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(liveStore);
        var views = await liveStore.ListAsync(
            new SavedViewFilter { SessionId = sessionId, Limit = int.MaxValue }, ct);
        if (views.Count == 0) return;

        var savedViewsDir = Path.Combine(bundleStagingPath, "saved_views");
        Directory.CreateDirectory(savedViewsDir);
        var path = Path.Combine(savedViewsDir, "saved_views.json");

        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, views,
            new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true,
                Converters = { new JsonStringEnumConverter() },
            }, ct);
    }
}
```

#### 3. `src/Tracer.Aggregator/AggregationOrchestrator.cs`

Add `ISavedViewStore? savedViewStore = null` to the primary constructor. Store as `_savedViewStore`. Add step 7c after the annotations export block (step 7b):

**Constructor change** — add the new parameter:
```csharp
using Tracer.Storage.SavedViews;
// ...
private readonly ISavedViewStore? _savedViewStore;

public AggregationOrchestrator(
    ITelemetryStorageReader nasReader,
    ILogger<AggregationOrchestrator> logger,
    IAnnotationStore? annotationStore = null,
    ISavedViewStore? savedViewStore = null)
{
    // ... existing body ...
    _savedViewStore = savedViewStore;
}
```

**Step 7c** — insert after the existing step 7b block:
```csharp
// 7c. Export saved views (if live store provided)
if (_savedViewStore is not null)
{
    await SavedViewsExporter.ExportAsync(
        _savedViewStore, request.SessionId ?? "", staging.BundleStagingPath, ct);
    progress?.Report(AggregationStage.SavedViewsExported, "Saved views exported into bundle");
}
```

#### 4. `src/Tracer.Aggregator/Tracer.Aggregator.csproj`

Add project reference to `Tracer.Storage.SavedViews`:
```xml
<ProjectReference Include="..\Tracer.Storage.SavedViews\Tracer.Storage.SavedViews.csproj" />
```

### Tests for B1

Add to `tests/Tracer.Tests.Unit/Aggregator/` (create a new file `SavedViewsExporterTests.cs`):

1. **`ExportAsync_WhenStoreHasViews_WritesJsonFile`** — mock `ISavedViewStore` returning two `SavedViewRecord`s; verify `saved_views/saved_views.json` exists and contains `camelCase` JSON with both records.

2. **`ExportAsync_WhenStoreIsEmpty_DoesNotCreateFile`** — mock returns empty list; verify the file is NOT created.

3. **`AggregationOrchestrator_WithSavedViewStore_FiresSavedViewsExportedStage`** — if the unit tests for `AggregationOrchestrator` stage reporting already exist, extend with a test that passes a mock `ISavedViewStore` and confirms `SavedViewsExported` appears in the reported stages list.

---

## Task B5 — Include slow_state.duckdb in Bundle Connections

### Background
When the aggregator runs, it produces **two separate DuckDB files**:
- `events.duckdb` — contains only the `events` table
- `slow_state.duckdb` — contains only the `slow_state` table

When the offline viewer opens a bundle, `BundleIntervalSetTracker.SwitchToBundleAsync` creates one `IntervalReference` pointing to `events.duckdb`. `LiveMultiIntervalReader.BuildMemoryConnectionAsync` attaches `events.duckdb` with alias `iv_xxx`. But `PooledMultiIntervalConnection.BuildSlowStateUnionSql` generates `SELECT * FROM iv_xxx.slow_state`, which FAILS because `slow_state` is not in `events.duckdb`.

**Fix**: In the no-active-interval path (bundle mode), also attach `slow_state.duckdb` with a second alias when it's a separate file, and make `BuildSlowStateUnionSql` use the correct alias.

### Files to modify

#### 1. `src/Tracer.Storage.DuckDB.MultiInterval/LiveMultiIntervalReader.cs`

**In `PooledMultiIntervalConnection`** — add a nullable `_slowStateAliases` field to the constructor and `BuildSlowStateUnionSql`:

```csharp
// Add new field
private readonly IReadOnlyList<string>? _slowStateAliases;

// Update constructor to accept the new parameter (add as last before issuingSnapshot):
internal PooledMultiIntervalConnection(
    LiveMultiIntervalReader owner,
    DuckDBConnection connection,
    AttachedDatabaseManager? manager,
    IReadOnlyList<string> aliases,
    IReadOnlyList<string>? slowStateAliases,
    IntervalSetSnapshot? issuingSnapshot,
    bool hasActive)
{
    // ... existing assignments ...
    _slowStateAliases = slowStateAliases;
}
```

**In `BuildSlowStateUnionSql`** — use `_slowStateAliases ?? _aliases`:
```csharp
public string BuildSlowStateUnionSql(string whereClause = "", string orderByClause = "", int? limit = null)
{
    var effectiveAliases = _slowStateAliases ?? _aliases;
    var parts = new List<string>();
    if (_hasActive) parts.Add($"SELECT * FROM main.slow_state {whereClause}");
    foreach (var alias in effectiveAliases) parts.Add($"SELECT * FROM {alias}.slow_state {whereClause}");
    if (parts.Count == 0) return "SELECT NULL WHERE FALSE";
    var sql = string.Join("\nUNION ALL\n", parts);
    if (!string.IsNullOrEmpty(orderByClause)) sql += "\n" + orderByClause;
    if (limit.HasValue) sql += $"\nLIMIT {limit.Value}";
    return sql;
}
```

**Update all 3 existing `PooledMultiIntervalConnection` construction sites** (search with `new PooledMultiIntervalConnection`) to pass `null` for `slowStateAliases` (preserving current behaviour for live intervals):
- `BuildCoordinatorAsync`: pass `null` for slowStateAliases
- `BuildWorkerAsync`: pass `null` for slowStateAliases
- `BuildMemoryConnectionAsync`: compute and pass real slow-state aliases (see below)

**In `BuildMemoryConnectionAsync`** — attach slow_state separately per interval when it's a distinct file:

```csharp
private async Task<PooledMultiIntervalConnection> BuildMemoryConnectionAsync(
    IntervalSetSnapshot snapshot, CancellationToken ct)
{
    var completed = snapshot.Completed.ToList();

    var conn = new DuckDBConnection("DataSource=:memory:");
    await conn.OpenAsync(ct);

    var manager = new AttachedDatabaseManager(conn);
    var aliases = new List<string>();
    var slowStateAliases = new List<string>();

    foreach (var ivref in completed)
    {
        var file = new IntervalDbFile(
            ivref.Directory.EventsDbPath,
            $"iv_{ivref.Directory.Timestamp.Value}");
        var alias = await manager.AttachAsync(file, ct);
        aliases.Add(alias);

        // If slow_state lives in a separate file (bundle mode), attach it independently
        var ssPath = ivref.Directory.SlowStateDbPath;
        if (!string.IsNullOrEmpty(ssPath) &&
            !string.Equals(ssPath, ivref.Directory.EventsDbPath, StringComparison.OrdinalIgnoreCase) &&
            File.Exists(ssPath))
        {
            var ssFile = new IntervalDbFile(ssPath, $"ss_{ivref.Directory.Timestamp.Value}");
            var ssAlias = await manager.AttachAsync(ssFile, ct);
            slowStateAliases.Add(ssAlias);
        }
        else
        {
            slowStateAliases.Add(alias);
        }
    }

    return new PooledMultiIntervalConnection(
        this, conn, manager, aliases, slowStateAliases, snapshot, hasActive: false);
}
```

### Tests for B5

Add to `tests/Tracer.Tests.Unit/MultiInterval/` (new file `PooledMultiIntervalConnectionSlowStateTests.cs` or extend existing test class):

1. **`BuildSlowStateUnionSql_WithSlowStateAliases_UsesSlowStateAliases`** — construct a `PooledMultiIntervalConnection` with `aliases = ["iv_aaa"]` and `slowStateAliases = ["ss_aaa"]`. Call `BuildSlowStateUnionSql()`. Assert the SQL contains `ss_aaa.slow_state` and does NOT contain `iv_aaa.slow_state`.

2. **`BuildSlowStateUnionSql_WithNullSlowStateAliases_FallsBackToAliases`** — construct with `aliases = ["iv_aaa"]` and `slowStateAliases = null`. Assert SQL contains `iv_aaa.slow_state`.

3. **`BuildMemoryConnectionAsync_WhenSlowStateFileExists_AttachesSeparately`** — use a `BundleIntervalSetTracker` backed by a real (temp) `events.duckdb` and a real `slow_state.duckdb` (minimal schema, no data). Call `InitializeAsync` on a `LiveMultiIntervalReader`, then `AcquireAsync`. Verify `BuildSlowStateUnionSql()` produces SQL referencing the `ss_` prefixed alias (not the `iv_` alias). NOTE: This test requires creating actual DuckDB temp files.

---

## Task I1 — SharedMemory Drop Telemetry

### Goal
`SharedMemoryTransport.GetHealth()` always returns `TotalDropped = 0L`. It should instead return the actual count of dropped records as reported by the underlying `SharedMemoryReader.GetDroppedCount()`.

### File to modify: `src/Tracer.Adapters.SharedMemory/SharedMemoryTransport.cs`

Current state:
- `_totalReceived` is tracked correctly
- `TotalDropped = 0L` is hardcoded in `GetHealth()`
- `ReadAsync` creates a local `using var reader = new SharedMemoryReader(...)` — the reader is disposed when the `IAsyncEnumerable` is not consumed or cancelled

**Problem**: `reader` is local to `ReadAsync`, and `GetHealth()` can't access it. The fix is to promote the drop count tracking:

Add a `_totalDropped` field and update it from inside the read loop:

```csharp
private long _totalDropped;
```

In `ReadAsync`, after each batch yield, update the field from `reader.GetDroppedCount()`. Because `GetHealth()` may be called concurrently, use `Interlocked`:

```csharp
foreach (var record in batch)
{
    Interlocked.Increment(ref _totalReceived);
    _lastReceivedAt = WallclockTime.FromDateTimeOffset(DateTimeOffset.UtcNow);
    yield return record;
}
// Update drop count after processing each batch
Interlocked.Exchange(ref _totalDropped, reader.GetDroppedCount());
```

Update `GetHealth()`:
```csharp
TotalDropped = Interlocked.Read(ref _totalDropped),
```

### Tests for I1

Add to `tests/Tracer.Tests.Unit/Adapters/SharedMemoryTransportTests.cs` (create if not exists):

1. **`GetHealth_Initially_ReturnsTotalDroppedZero`** — construct `SharedMemoryTransport` with a mock/fake config; call `GetHealth()` without starting `ReadAsync`; assert `TotalDropped == 0`.

**Note**: The `SharedMemoryReader` constructor connects to a real shared memory buffer and cannot be easily unit-tested without the actual reader infrastructure. For I1, the test coverage is limited to the initial state and the property exposure. If `SharedMemoryReader` is mockable/injectable, add a second test checking that drops accumulate, but do NOT create a complex test harness just for this.

---

## Task I4 — Expand /api/health Endpoint Metrics

### Goal
`GET /api/health` currently returns only `status`, `sharedMemoryDropped`, and `ingestChannelDepth`. Add:
- `sseConnectionsActive`: number of active SSE streaming connections from `SseConnectionManager.ActiveCount`
- `intervalsAwaitingUpload`: number of upload-ready intervals from `UploadIntentDispatcher.PendingCount`

Both services may not be registered (e.g., in minimal deployments), so inject them as optional services.

### File to modify: `src/Tracer.WebApi/Endpoints/HealthEndpoints.cs`

Current content:
```csharp
app.MapGet("/api/health", ([FromServices] IAgentTransport? transport) =>
{
    var health = transport?.GetHealth();
    return Results.Ok(new
    {
        status = "ok",
        sharedMemoryDropped = health?.TotalDropped ?? 0L,
        ingestChannelDepth = health?.PendingCount ?? 0,
    });
})
```

**Replace the lambda with**:
```csharp
app.MapGet("/api/health", (
    [FromServices] IAgentTransport? transport,
    [FromServices] SseConnectionManager? sseManager,
    [FromServices] UploadIntentDispatcher? uploadDispatcher) =>
{
    var health = transport?.GetHealth();
    return Results.Ok(new
    {
        status = "ok",
        sharedMemoryDropped = health?.TotalDropped ?? 0L,
        ingestChannelDepth = health?.PendingCount ?? 0,
        sseConnectionsActive = sseManager?.ActiveCount ?? 0,
        intervalsAwaitingUpload = uploadDispatcher?.PendingCount ?? 0,
    });
})
```

Add the required using statements at the top of the file:
```csharp
using Tracer.Agent.Upload;
using Tracer.WebApi.Streaming;
```

### Tests for I4

Add to `tests/Tracer.Tests.Unit/WebApi/HealthEndpointsTests.cs` (create if not exists, or extend):

1. **`GetHealth_WithAllServicesNull_ReturnsZeroMetrics`** — call the endpoint with no services registered; assert all four fields are in the response with value 0 / "ok".

2. **`GetHealth_WithSseManager_ReturnsActiveCount`** — register a mock/stub `SseConnectionManager` with `ActiveCount = 3`; call endpoint; assert `sseConnectionsActive == 3`.

3. **`GetHealth_WithUploadDispatcher_ReturnsPendingCount`** — register a mock/stub `UploadIntentDispatcher` with `PendingCount = 5`; call endpoint; assert `intervalsAwaitingUpload == 5`.

Use `WebApplicationFactory<Program>` or minimal `WebApplication` builder approach consistent with existing WebApi tests. Check existing `HealthEndpoints` tests first to follow the same test pattern.

---

## Build and Test Verification

After all changes, confirm:

```powershell
cd d:\Work\Tracer
dotnet build --no-restore -warnaserror
dotnet test --filter "FullyQualifiedName!~Publish_ProducesExpectedLayout" --no-build
```

Expected: 0 build errors, 0 build warnings, all tests passing.

---

## Report Format

Return a structured report with:
1. Files created/modified (with brief description)
2. Test count added
3. Any deviations from these instructions (with justification)
4. Build output summary (errors/warnings count)
5. Test output summary (pass/fail counts)
