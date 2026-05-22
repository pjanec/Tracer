# BATCH-42 Instructions

**Batch:** BATCH-42  
**Tasks:** TRC-P8-001 (Tracer.Storage.Annotations Assembly) + TRC-P8-002 (SqliteAnnotationStore) + TRC-P8-003 (BundleAnnotationStore)  
**Assigned to:** Developer  
**Reference skill:** `d:\WORK\Tracer\.github\skills\developer\SKILL.md`

---

## Overview

Create the `Tracer.Storage.Annotations` assembly with:
- Core types: `AnnotationRecord`, `AnnotationKind`, `IAnnotationStore`, `AnnotationFilter`, `AnnotationsSchema`
- `SqliteAnnotationStore` — full CRUD backed by SQLite, with `SemaphoreSlim` write lock
- `BundleAnnotationStore` — read-only store backed by `annotations/annotations.json` in a bundle directory

Add the assembly to `Tracer.sln`. Add `Microsoft.Data.Sqlite` to `Directory.Packages.props`.

No other assemblies are modified in this batch (DI wiring is TRC-P8-005).

---

## Reference Files to Read First

- `d:\WORK\Tracer\docs\tracer_phase8_design.md` §3 (data model, SqliteAnnotationStore, BundleAnnotationStore, schema)
- `d:\WORK\Tracer\docs\TASK-DETAIL.md` §TRC-P8-001, §TRC-P8-002, §TRC-P8-003 (success conditions)
- `d:\WORK\Tracer\src\Tracer.Storage.DuckDB\Tracer.Storage.DuckDB.csproj` (pattern for .csproj)
- `d:\WORK\Tracer\src\Tracer.Storage.Parquet\Tracer.Storage.Parquet.csproj` (pattern for .csproj)
- `d:\WORK\Tracer\Directory.Packages.props` (central package versions — add Microsoft.Data.Sqlite)
- `d:\WORK\Tracer\Tracer.sln` (add new project entry)
- `d:\WORK\Tracer\tests\Tracer.Tests.Unit\Tracer.Tests.Unit.csproj` (add project reference)

---

## Step 1 — Update `Directory.Packages.props`

Add one line to the `<ItemGroup>` inside `Directory.Packages.props`:
```xml
<PackageVersion Include="Microsoft.Data.Sqlite" Version="8.0.0" />
```

---

## Step 2 — Create `Tracer.Storage.Annotations` Assembly

### 2.1 Create `src/Tracer.Storage.Annotations/Tracer.Storage.Annotations.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <AssemblyName>Tracer.Storage.Annotations</AssemblyName>
  </PropertyGroup>

  <ItemGroup>
    <AssemblyAttribute Include="System.Runtime.CompilerServices.InternalsVisibleToAttribute">
      <_Parameter1>Tracer.Tests.Unit</_Parameter1>
    </AssemblyAttribute>
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\Tracer.Core\Tracer.Core.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Data.Sqlite" />
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" />
    <PackageReference Include="Ulid" />
  </ItemGroup>

</Project>
```

### 2.2 Create `src/Tracer.Storage.Annotations/AnnotationKind.cs`

```csharp
namespace Tracer.Storage.Annotations;

public enum AnnotationKind { Event, Entity, Trace, TimePoint }
```

### 2.3 Create `src/Tracer.Storage.Annotations/AnnotationRecord.cs`

```csharp
namespace Tracer.Storage.Annotations;

public sealed record AnnotationRecord
{
    public required string AnnotationId { get; init; }
    public required string SessionId { get; init; }
    public required AnnotationKind Kind { get; init; }

    public string? EventId { get; init; }
    public string? EntityId { get; init; }
    public string? TraceId { get; init; }
    public DateTimeOffset? TargetWallclock { get; init; }

    public required string Body { get; init; }
    public string? Title { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();

    public string? Author { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset? ModifiedAtUtc { get; init; }
}
```

### 2.4 Create `src/Tracer.Storage.Annotations/AnnotationFilter.cs`

```csharp
namespace Tracer.Storage.Annotations;

public sealed record AnnotationFilter
{
    public string? SessionId { get; init; }
    public AnnotationKind? Kind { get; init; }
    public string? EventId { get; init; }
    public string? EntityId { get; init; }
    public string? TraceId { get; init; }
    public DateTimeOffset? FromUtc { get; init; }
    public DateTimeOffset? ToUtc { get; init; }
    public IReadOnlyList<string>? Tags { get; init; }
    public int Limit { get; init; } = 500;
}
```

### 2.5 Create `src/Tracer.Storage.Annotations/IAnnotationStore.cs`

```csharp
namespace Tracer.Storage.Annotations;

public interface IAnnotationStore
{
    Task<IReadOnlyList<AnnotationRecord>> ListAsync(AnnotationFilter filter, CancellationToken ct);
    Task<AnnotationRecord?> GetAsync(string annotationId, CancellationToken ct);
    Task<AnnotationRecord> CreateAsync(AnnotationRecord record, CancellationToken ct);
    Task<AnnotationRecord?> UpdateAsync(AnnotationRecord record, CancellationToken ct);
    Task<bool> DeleteAsync(string annotationId, CancellationToken ct);
    Task<IReadOnlyList<AnnotationRecord>> ExportAllForSessionAsync(string sessionId, CancellationToken ct);
}
```

### 2.6 Create `src/Tracer.Storage.Annotations/Schema/AnnotationsSchema.cs`

```csharp
namespace Tracer.Storage.Annotations.Schema;

public static class AnnotationsSchema
{
    public const string CreateSql = """
        CREATE TABLE IF NOT EXISTS annotations (
            annotation_id     TEXT PRIMARY KEY,
            session_id        TEXT NOT NULL,
            kind              TEXT NOT NULL,
            event_id          TEXT,
            entity_id         TEXT,
            trace_id          TEXT,
            target_wallclock  TEXT,
            body              TEXT NOT NULL,
            title             TEXT,
            tags_json         TEXT NOT NULL DEFAULT '[]',
            author            TEXT,
            created_at        TEXT NOT NULL,
            modified_at       TEXT
        );
        CREATE INDEX IF NOT EXISTS idx_annotations_session    ON annotations (session_id);
        CREATE INDEX IF NOT EXISTS idx_annotations_event_id   ON annotations (event_id)   WHERE event_id   IS NOT NULL;
        CREATE INDEX IF NOT EXISTS idx_annotations_entity_id  ON annotations (entity_id)  WHERE entity_id  IS NOT NULL;
        CREATE INDEX IF NOT EXISTS idx_annotations_trace_id   ON annotations (trace_id)   WHERE trace_id   IS NOT NULL;
        CREATE INDEX IF NOT EXISTS idx_annotations_created_at ON annotations (created_at);
        """;
}
```

### 2.7 Create `src/Tracer.Storage.Annotations/SqliteAnnotationStore.cs`

Implement exactly as specified in `tracer_phase8_design.md §3.4`. Key points:
- Constructor: `(string dbPath, ILogger<SqliteAnnotationStore> logger)`
- `InitializeAsync(CancellationToken ct)` method (not part of `IAnnotationStore` — called separately on startup)
- `_writeLock = new SemaphoreSlim(1, 1)` — acquired for `CreateAsync`, `UpdateAsync`, `DeleteAsync`
- Read methods open with `Mode=ReadOnly` connection string suffix
- Write methods open with default connection string (read-write)
- `BuildSelectSql` returns `(string Sql, IReadOnlyList<(string Key, object? Value)> Parameters)` or similar
- `BindRecordParameters` binds all 13 parameters using `$` prefix (see design §3.4)
- `MapRecord` reads all columns by ordinal
- `Tags` stored as JSON text (`tags_json` column), deserialized via `System.Text.Json.JsonSerializer.Deserialize<List<string>>`
- `ExportAllForSessionAsync` calls `ListAsync(new AnnotationFilter { SessionId = sessionId, Limit = 100_000 }, ct)`
- For null handling in SQLite, use `(object?)value ?? DBNull.Value`

### 2.8 Create `src/Tracer.Storage.Annotations/BundleAnnotationStore.cs`

Implement exactly as specified in `tracer_phase8_design.md §3.6`. Key points:
- Constructor: `(string bundlePath)` — computes `_bundleAnnotationsPath = Path.Combine(bundlePath, "annotations", "annotations.json")`
- `_cache` field: `IReadOnlyList<AnnotationRecord>?`
- `LoadAsync` is private: check `_cache != null` first; if file absent, set `_cache = Array.Empty<AnnotationRecord>()`; else read + deserialize
- JSON deserialization: use `JsonSerializerOptions` with `PropertyNamingPolicy = JsonNamingPolicy.CamelCase` (so it matches how it will be exported)
- `ListAsync` filters in memory: session, kind, eventId, entityId, traceId, fromUtc, toUtc; orders by `CreatedAtUtc` descending; takes `filter.Limit`
- `CreateAsync`, `UpdateAsync`, `DeleteAsync`: `throw new InvalidOperationException("Bundle annotations are read-only")`
- `ExportAllForSessionAsync`: filter by sessionId in memory

---

## Step 3 — Add to Solution and Test Project

### Add to `Tracer.sln`

Run (from solution root):
```
dotnet sln d:\Work\Tracer\Tracer.sln add src\Tracer.Storage.Annotations\Tracer.Storage.Annotations.csproj
```

### Add reference in `tests/Tracer.Tests.Unit/Tracer.Tests.Unit.csproj`

```xml
<ProjectReference Include="..\..\src\Tracer.Storage.Annotations\Tracer.Storage.Annotations.csproj" />
```

---

## Step 4 — Tests

### 4.1 Create `tests/Tracer.Tests.Unit/Annotations/SqliteAnnotationStoreTests.cs`

Use `IDisposable` (not `IAsyncLifetime`) — temp file cleanup is synchronous. Create a `SqliteAnnotationStore` pointing at a temp path. Call `InitializeAsync()` in a shared setup or in each test.

**Helper method pattern:**
```csharp
private SqliteAnnotationStore CreateStore(out string dbPath)
{
    dbPath = Path.Combine(_tempDir, $"annot-{Guid.NewGuid():N}.db");
    return new SqliteAnnotationStore(dbPath, NullLogger<SqliteAnnotationStore>.Instance);
}

private static AnnotationRecord MakeRecord(string sessionId = "sess-1", AnnotationKind kind = AnnotationKind.Event) =>
    new AnnotationRecord
    {
        AnnotationId = "",
        SessionId = sessionId,
        Kind = kind,
        EventId = kind == AnnotationKind.Event ? "0000000000000001" : null,
        Body = "Test annotation",
        CreatedAtUtc = default,  // let store assign
    };
```

**Tests to write** (12 tests):

1. `InitializeAsync_CreatesSchemaAndIndexes` — call InitializeAsync; verify file exists; open connection; check `annotations` table and 5 indexes exist in sqlite_master
2. `InitializeAsync_IsIdempotent` — call InitializeAsync twice; no exception
3. `CreateAsync_GeneratesUlid_WhenIdEmpty` — create with `AnnotationId = ""`; assert result has 26-char non-empty ID
4. `CreateAsync_SetsCreatedAtUtc_WhenDefault` — create with `CreatedAtUtc = default`; assert result within 5s of UtcNow
5. `UpdateAsync_SetsModifiedAtUtc` — create record; then update copy; assert `result.ModifiedAtUtc >= result.CreatedAtUtc`
6. `UpdateAsync_UnknownId_ReturnsNull` — update with nonexistent ID; assert null
7. `DeleteAsync_UnknownId_ReturnsFalse` — delete nonexistent; assert false
8. `ListAsync_FilterBySessionId` — create 2 annotations for session-A, 1 for session-B; list with session-A filter; assert count == 2, all session-A
9. `ListAsync_OrdersByCreatedAtDesc` — create 3 annotations with distinct past `CreatedAtUtc` values (use explicit datetimes); list; assert descending order
10. `ListAsync_RespectsLimit` — create 5, list with Limit=2; assert 2 returned
11. `Tags_RoundTrip` — create with Tags=["alpha","beta"]; GetAsync; assert Tags matches
12. `AnnotationFilter_LimitDefaultIs500` — `new AnnotationFilter().Limit.Should().Be(500)` (this is the TRC-P8-001 SC-5 test)

**Also add as separate test class** (or add to the same file under a different class name):
```csharp
public class AnnotationsSchemaTests
{
    [Fact]
    public void AnnotationsSchema_ExecutesWithoutError()
    [Fact]
    public void AnnotationsSchema_IsIdempotent()
}
```
These open a SQLite in-memory connection (`Data Source=:memory:`).

**IMPORTANT**: For `AnnotationsSchemaTests`, open connection like:
```csharp
await using var conn = new SqliteConnection("Data Source=:memory:");
await conn.OpenAsync();
await using var cmd = conn.CreateCommand();
cmd.CommandText = AnnotationsSchema.CreateSql;
await cmd.ExecuteNonQueryAsync();
// Verify table exists
cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='annotations'";
var tableName = await cmd.ExecuteScalarAsync();
tableName.Should().Be("annotations");
```

**IMPORTANT**: For the `ListAsync_OrdersByCreatedAtDesc` test, use explicit past `DateTimeOffset` values (not `default`) so they're different. E.g.:
```csharp
var t1 = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
var t2 = t1.AddHours(1);
var t3 = t2.AddHours(1);
```

### 4.2 Create `tests/Tracer.Tests.Unit/Annotations/BundleAnnotationStoreTests.cs`

Use `IDisposable` with a temp directory.

**Helper to write test annotations.json:**
```csharp
private async Task WriteAnnotationsJsonAsync(string bundleDir, IEnumerable<AnnotationRecord> records)
{
    var annotDir = Path.Combine(bundleDir, "annotations");
    Directory.CreateDirectory(annotDir);
    var path = Path.Combine(annotDir, "annotations.json");
    await File.WriteAllTextAsync(path, JsonSerializer.Serialize(records,
        new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
}
```

**Tests to write** (10 tests):

1. `ListAsync_FileAbsent_ReturnsEmpty` — no annotations dir; assert empty list
2. `ListAsync_ValidFile_ReturnsParsedRecords` — write 2 records; list; assert count == 2
3. `GetAsync_MatchingId_ReturnsRecord` — write 1 record; GetAsync; assert non-null with matching AnnotationId
4. `GetAsync_UnknownId_ReturnsNull` — GetAsync unknown; assert null
5. `CreateAsync_ThrowsInvalidOperationException` — assert throws with "read-only" in message
6. `UpdateAsync_ThrowsInvalidOperationException` — assert throws with "read-only" in message
7. `DeleteAsync_ThrowsInvalidOperationException` — assert throws with "read-only" in message
8. `ExportAllForSessionAsync_FiltersBySessionId` — 3 records (2 session-A, 1 session-B); ExportAll for session-A; assert count == 2
9. `Cache_NotRefreshedOnSecondCall` — list (populates cache); overwrite file with different content; list again; assert same first-call data
10. `ListAsync_FilterByKind` — 2 Event records + 1 Trace record; list with Kind=Event filter; assert count == 2

**IMPORTANT**: `BundleAnnotationStore` constructor requires an existing record type. If `AnnotationRecord` has `required` properties, build records using `with` syntax or fully-populated constructors. For test JSON writing, include all `required` fields: `AnnotationId`, `SessionId`, `Kind`, `Body`, `CreatedAtUtc`.

**JSON serialization note**: The `BundleAnnotationStore.LoadAsync` uses `PropertyNamingPolicy = JsonNamingPolicy.CamelCase`. Ensure test helper writes JSON with the same policy so deserialization works.

For `AnnotationKind` enum serialization: `System.Text.Json` serializes enums as strings only if `JsonStringEnumConverter` is added to options. Otherwise it uses integers. **Use integers or add the converter consistently.** The simplest approach: add `Converters = { new JsonStringEnumConverter() }` to both the write and read options in `BundleAnnotationStore`. Then tests must use the same.

Actually, check how SqliteAnnotationStore stores Kind (as string via `.ToString()`). For consistency, in BundleAnnotationStore/test JSON, use string enum. Add `JsonStringEnumConverter` to both write options (test helper) and read options (LoadAsync).

---

## Step 5 — Verify

```powershell
# Build
dotnet build d:\Work\Tracer\Tracer.sln -c Release --no-incremental

# Run new Annotation tests
dotnet test d:\Work\Tracer\tests\Tracer.Tests.Unit -c Release --no-build --filter "FullyQualifiedName~Annotations"

# Run ALL unit tests (no regressions)
dotnet test d:\Work\Tracer\tests\Tracer.Tests.Unit -c Release --no-build --filter "FullyQualifiedName!~Publish_ProducesExpectedLayout"
```

Expected: 22+ new tests (12 SqliteAnnotationStore + 2 AnnotationsSchema + 10 BundleAnnotationStore) all passing.

---

## Constraints and Notes

- **Do NOT** create `LazyBundleAnnotationStore` — that's TRC-P8-005
- **Do NOT** wire DI registrations — that's TRC-P8-005
- **Do NOT** create the `AnnotationsExporter` — that's TRC-P8-009
- **No SQL injection**: all user data goes through `$parameter` bindings, never string-interpolated into SQL
- `SqliteAnnotationStore.InitializeAsync` is not part of `IAnnotationStore` — it's called explicitly at startup
- The `Ulid` package is already in `Directory.Packages.props` version 1.3.4 — use `Ulid.NewUlid().ToString()`
- `Microsoft.Data.Sqlite` uses `SqliteConnection`, `SqliteCommand`, `SqliteDataReader` from the `Microsoft.Data.Sqlite` namespace

---

## Report

Write report to: `d:\WORK\Tracer\.dev\tracer\reports\BATCH-42-REPORT.md`

Do NOT commit.
