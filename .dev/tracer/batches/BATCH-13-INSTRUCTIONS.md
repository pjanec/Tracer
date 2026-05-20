# BATCH-13 — Phase 4 Foundation: Bundle Format + MultiIntervalReader

**Tasks:** TRC-P4-001, TRC-P4-004  
**Batch type:** New assemblies + unit tests

---

## Context

Phase 3 is complete. Phase 4 begins. This batch adds two new assemblies that form the foundation of Phase 4:

1. **`Tracer.Bundle`** — pure data-model layer: the bundle manifest record, file layout constants, schema versioning, and safe filename helpers. No file I/O; no DuckDB; no HTTP.
2. **`Tracer.Storage.DuckDB.MultiInterval`** — extends the storage layer with the ability to attach multiple DuckDB files to a single in-memory connection and build UNION ALL queries across them.

These two assemblies are independent of each other and of all other Phase 4 tasks. Do them both in this batch.

---

## Prerequisites: Directory.Packages.props additions

Before creating any projects, add the following three package versions to `Directory.Packages.props` inside the existing `<ItemGroup>`:

```xml
<PackageVersion Include="System.CommandLine" Version="2.0.0-beta4.22272.1" />
<PackageVersion Include="System.IO.Compression.ZipFile" Version="4.3.0" />
<PackageVersion Include="Ulid" Version="1.3.4" />
```

---

## Task 1: TRC-P4-001 — Bundle Format

### 1.1 Create `src/Tracer.Bundle/Tracer.Bundle.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <AssemblyName>Tracer.Bundle</AssemblyName>
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
    <PackageReference Include="Ulid" />
  </ItemGroup>

</Project>
```

Add to `Tracer.sln`:
```
dotnet sln Tracer.sln add src/Tracer.Bundle/Tracer.Bundle.csproj
```

### 1.2 Create `src/Tracer.Bundle/Format/BundleManifest.cs`

`BundleManifest` is a `record` (with `init`-only properties) representing the full manifest schema from §3.2. All nested types are `record` types in the same file. The JSON serialization policy uses `JsonNamingPolicy.CamelCase`. Serialize/deserialize with:

```csharp
var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
```

Top-level fields (all `public`, all `init`):
- `string BundleId` — a 26-character ULID string generated via `Ulid.NewUlid().ToString()`
- `int SchemaVersion` — set to `BundleSchemaV1.CurrentVersion`
- `DateTimeOffset CreatedAtUtc`
- `string TracerVersion`
- `BundleWriterInfo Writer`
- `BundleTimeRange TimeRange`
- `BundleSessionContext SessionContext`
- `IReadOnlyList<string> ParticipatingNodes`
- `string FastStateScope` — "none" | "selected-entities" | "all"
- `IReadOnlyList<string> FastStateEntities`
- `BundleStatistics Statistics`
- `IReadOnlyList<BundleFileEntry> Files`

Nested records (in the same file):

```csharp
public record BundleWriterInfo
{
    public required string Tool { get; init; }
    public required string Version { get; init; }
    public required string Host { get; init; }
}

public record BundleTimeRange
{
    public required DateTimeOffset StartUtc { get; init; }
    public required DateTimeOffset EndUtc { get; init; }
}

public record BundleSessionContext
{
    public required string SessionId { get; init; }
    public required string ScenarioId { get; init; }
    public string? Label { get; init; }
}

public record BundleStatistics
{
    public required long TotalEvents { get; init; }
    public required long TotalSlowStateSamples { get; init; }
    public required long TotalFastStateRows { get; init; }
    public required long UncompressedBytes { get; init; }
}

public record BundleFileEntry
{
    public required string Path { get; init; }
    public required long SizeBytes { get; init; }
    public required string Sha256 { get; init; }
}
```

### 1.3 Create `src/Tracer.Bundle/Format/BundleSchemaV1.cs`

```csharp
namespace Tracer.Bundle.Format;

public static class BundleSchemaV1
{
    public const int CurrentVersion = 1;

    private static readonly IReadOnlySet<int> _recognized = new HashSet<int> { 1 };

    public static bool IsRecognized(int version) => _recognized.Contains(version);
}
```

### 1.4 Create `src/Tracer.Bundle/Format/BundleLayout.cs`

File path constants (all `public static readonly string`):

```csharp
namespace Tracer.Bundle.Format;

public static class BundleLayout
{
    public static readonly string ManifestFile       = "manifest.json";
    public static readonly string ScenarioFile       = "scenario.json";
    public static readonly string TopologyFile       = "topology.json";
    public static readonly string SourceIntervalsFile = "source_intervals.json";
    public static readonly string EventsDb           = "events.duckdb";
    public static readonly string SlowStateDb        = "slow_state.duckdb";
    public static readonly string ChecksumsFile      = "checksums.txt";
    public static readonly string FastStateDirectory = "fast_state";
    public static readonly string AnnotationsDirectory = "annotations";
    public static readonly string AnnotationsKeepFile  = "annotations/.keep";
}
```

### 1.5 Create `src/Tracer.Bundle/Format/BundleNaming.cs`

```csharp
using System.Security.Cryptography;
using System.Text;

namespace Tracer.Bundle.Format;

public static class BundleNaming
{
    /// <summary>
    /// Returns a filesystem-safe version of <paramref name="input"/> by replacing
    /// every character not in [a-zA-Z0-9._-] with '_', then appending '_' and a
    /// 4-character lowercase hex hash derived from the original string to prevent
    /// collisions between distinct inputs that produce the same replaced form.
    /// </summary>
    public static string SafeFileName(string input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var sb = new StringBuilder(input.Length + 5);
        foreach (var c in input)
        {
            if (char.IsAsciiLetterOrDigit(c) || c == '.' || c == '_' || c == '-')
                sb.Append(c);
            else
                sb.Append('_');
        }

        // 4-char hex hash of original input to prevent collision
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        var suffix = Convert.ToHexString(hashBytes).ToLowerInvariant()[..4];

        sb.Append('_');
        sb.Append(suffix);

        return sb.ToString();
    }
}
```

### 1.6 Create unit tests

**File:** `tests/Tracer.Tests.Unit/Bundle/BundleManifestTests.cs`

Add a `<ProjectReference>` for `Tracer.Bundle` to `tests/Tracer.Tests.Unit/Tracer.Tests.Unit.csproj`:
```xml
<ProjectReference Include="..\..\src\Tracer.Bundle\Tracer.Bundle.csproj" />
```

Test methods (7):

1. **`BundleManifest_RoundTripsViaJsonSerializer`** — create a fully populated `BundleManifest` (all fields non-null/non-default), serialize to JSON, deserialize back, assert the two records are equal.

2. **`BundleManifest_CamelCaseJson_ContainsBundleIdKey`** — serialize a `BundleManifest`; assert `json.Contains("\"bundleId\"")` is `true` and `json.Contains("\"BundleId\"")` is `false`.

3. **`BundleSchemaV1_CurrentVersionIsOne`** — `BundleSchemaV1.CurrentVersion.Should().Be(1)`.

4. **`BundleSchemaV1_IsRecognized_TrueForOne_FalseForNinetyNine`** — `IsRecognized(1)` is `true`; `IsRecognized(99)` is `false`.

5. **`BundleNaming_SafeFileName_ReplacesColons`** — `BundleNaming.SafeFileName("a:b")` should not contain `':'`.

6. **`BundleNaming_SafeFileName_DistinctInputs_ProduceDifferentOutputs`** — `BundleNaming.SafeFileName("x:y")` should not equal `BundleNaming.SafeFileName("x_y")` (collision prevention via hash suffix).

7. **`BundleLayout_AllPathConstants_AreNonEmpty`** — use reflection to get all `public static` fields on `BundleLayout` of type `string`; assert each is non-null and non-empty.

---

## Task 2: TRC-P4-004 — MultiIntervalReader

### 2.1 Create `src/Tracer.Storage.DuckDB.MultiInterval/Tracer.Storage.DuckDB.MultiInterval.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <AssemblyName>Tracer.Storage.DuckDB.MultiInterval</AssemblyName>
  </PropertyGroup>

  <ItemGroup>
    <AssemblyAttribute Include="System.Runtime.CompilerServices.InternalsVisibleToAttribute">
      <_Parameter1>Tracer.Tests.Unit</_Parameter1>
    </AssemblyAttribute>
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\Tracer.Core\Tracer.Core.csproj" />
    <ProjectReference Include="..\Tracer.Storage.DuckDB\Tracer.Storage.DuckDB.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="DuckDB.NET.Data" />
    <PackageReference Include="DuckDB.NET.Bindings.Full" />
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" />
  </ItemGroup>

</Project>
```

Add to solution:
```
dotnet sln Tracer.sln add src/Tracer.Storage.DuckDB.MultiInterval/Tracer.Storage.DuckDB.MultiInterval.csproj
```

Add a `<ProjectReference>` for `Tracer.Storage.DuckDB.MultiInterval` to `tests/Tracer.Tests.Unit/Tracer.Tests.Unit.csproj`.

### 2.2 Create `src/Tracer.Storage.DuckDB.MultiInterval/IntervalDbFile.cs`

```csharp
namespace Tracer.Storage.DuckDB.MultiInterval;

/// <summary>A DuckDB file to attach to a MultiIntervalReader.</summary>
/// <param name="FilePath">Absolute path to the .duckdb file.</param>
/// <param name="AliasHint">Human-readable hint used as a prefix when generating the SQL alias.
/// Should be a short identifier like a node name or interval timestamp slug.</param>
public record IntervalDbFile(string FilePath, string AliasHint);
```

### 2.3 Create `src/Tracer.Storage.DuckDB.MultiInterval/AttachedDatabaseManager.cs`

Responsibilities:
- Attaches and detaches read-only DuckDB files to a caller-supplied `DuckDBConnection`.
- Generates unique, SQL-safe aliases: `db_<normalized_hint>_<6hex>` where `<normalized_hint>` replaces all non-`[a-z0-9]` characters with `_` (lowercased) and `<6hex>` is 6 random lowercase hex characters.
- Tracks live attachments in `public IReadOnlyDictionary<string, string> Attachments` (alias → file path).
- `DetachAsync(string alias, CancellationToken ct)` executes `DETACH <alias>` and removes it from `Attachments`.
- `DisposeAsync` detaches all live attachments (best-effort; swallows individual exceptions).

```csharp
public sealed class AttachedDatabaseManager : IAsyncDisposable
{
    private readonly DuckDBConnection _connection;
    private readonly Dictionary<string, string> _attachments = new();

    public AttachedDatabaseManager(DuckDBConnection connection) { ... }

    public IReadOnlyDictionary<string, string> Attachments => _attachments;

    public async Task<string> AttachAsync(IntervalDbFile file, CancellationToken ct = default)
    {
        // Generate alias: db_{normalized_hint}_{6hex}
        // ATTACH 'filepath' AS alias (READ_ONLY)
        // Add to _attachments
        // Return alias
    }

    public async Task DetachAsync(string alias, CancellationToken ct = default)
    {
        // DETACH alias
        // Remove from _attachments
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var alias in _attachments.Keys.ToList())
        {
            try { await DetachAsync(alias); } catch { /* best-effort */ }
        }
    }
}
```

**Alias generation rules** (important for test assertions):
- Convert `file.AliasHint` to lowercase.
- Replace every character that is not `[a-z0-9]` with `_`.
- Prepend `db_` and append `_` + 6 lowercase random hex characters.
- Resulting alias must match regex `^db_[a-z0-9_]+_[0-9a-f]{6}$`.

**Collision prevention**: the 6-character random hex suffix makes collisions extremely unlikely; no de-duplication loop is needed.

### 2.4 Create `src/Tracer.Storage.DuckDB.MultiInterval/MultiIntervalReader.cs`

```csharp
public sealed class MultiIntervalReader : IAsyncDisposable
{
    private readonly AttachedDatabaseManager _manager;
    private readonly DuckDBConnection _connection; // in-memory primary

    private MultiIntervalReader(DuckDBConnection connection, AttachedDatabaseManager manager)
    {
        _connection = connection;
        _manager = manager;
    }

    public IReadOnlyDictionary<string, string> Attachments => _manager.Attachments;

    /// <summary>
    /// Opens an in-memory DuckDB connection, attaches all provided files, and returns
    /// a ready MultiIntervalReader.
    /// </summary>
    public static async Task<MultiIntervalReader> CreateAsync(
        IEnumerable<IntervalDbFile> files,
        CancellationToken ct = default)
    {
        var connection = new DuckDBConnection("DataSource=:memory:");
        await connection.OpenAsync(ct);
        var manager = new AttachedDatabaseManager(connection);
        foreach (var file in files)
            await manager.AttachAsync(file, ct);
        return new MultiIntervalReader(connection, manager);
    }

    /// <summary>
    /// Builds a UNION ALL SQL string selecting all columns + '__source_alias' sentinel
    /// from each attached database's events table.
    /// Returns the sentinel "SELECT NULL WHERE FALSE" when no files are attached.
    /// </summary>
    public string BuildEventsUnionSql()
    {
        if (_manager.Attachments.Count == 0)
            return "SELECT NULL WHERE FALSE";

        var parts = _manager.Attachments.Keys.Select(alias =>
            $"SELECT *, '{alias}' AS __source_alias FROM {alias}.events");
        return string.Join("\nUNION ALL\n", parts);
    }

    public async ValueTask DisposeAsync()
    {
        await _manager.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
```

### 2.5 Create unit tests

**File:** `tests/Tracer.Tests.Unit/MultiInterval/AttachedDatabaseManagerTests.cs`

Test methods (5):

1. **`AttachAsync_ProducesAliasMatchingPattern`** — create an in-memory DuckDB, attach a real temp DuckDB file; assert alias matches `^db_[a-z0-9_]+_[0-9a-f]{6}$` via `Regex.IsMatch`.

2. **`AttachAsync_SameHint_TwiceProducesDistinctAliases`** — attach two different files with the same `AliasHint`; assert the two returned aliases are not equal.

3. **`DetachAsync_RemovesAliasFromAttachments`** — attach a file, detach it; assert `manager.Attachments.ContainsKey(alias)` is `false`.

4. **`DisposeAsync_DetachesAllAttachments`** — attach 3 files; `await using` scope ends; assert `manager.Attachments.Count` is 0 after disposal.

5. **`AliasGeneration_ProducesValidSqlIdentifier`** — attach a file with `AliasHint = "my-node:01/test"` (contains special characters); assert alias matches `^[a-zA-Z_][a-zA-Z0-9_]*$`.

**Helper**: create a real temp DuckDB file for test use by opening a `DuckDBConnection` to a temp path and running `CREATE TABLE events (id INTEGER)`:

```csharp
private static async Task<string> CreateTempDuckDbAsync()
{
    var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"{Guid.NewGuid()}.duckdb");
    await using var conn = new DuckDBConnection($"DataSource={path}");
    await conn.OpenAsync();
    await using var cmd = conn.CreateCommand();
    cmd.CommandText = "CREATE TABLE events (id INTEGER)";
    await cmd.ExecuteNonQueryAsync();
    return path;
}
```

---

**File:** `tests/Tracer.Tests.Unit/MultiInterval/MultiIntervalReaderTests.cs`

Test methods (5):

1. **`CreateWithZeroFiles_BuildEventsUnionSql_ReturnsEmptySentinel`** — `CreateAsync(Enumerable.Empty<IntervalDbFile>())`; `BuildEventsUnionSql()` returns `"SELECT NULL WHERE FALSE"`.

2. **`CreateWithOneFile_SqlReferencesAlias`** — create one temp DuckDB, `CreateAsync([file])`; SQL from `BuildEventsUnionSql()` contains the generated alias.

3. **`CreateWithTwoFiles_SqlContainsOneUnionAll`** — create two temp DuckDBs; `BuildEventsUnionSql()` contains exactly one `"UNION ALL"` substring.

4. **`SourceAliasColumn_PresentInResults`** — create a temp DuckDB with `CREATE TABLE events (id INTEGER)` and `INSERT INTO events VALUES (1)`; build UNION ALL SQL; execute it against the reader's connection; assert result rows contain `__source_alias`.

   To execute a query on the reader's connection, the test must use the `internal` connection (exposed via `InternalsVisibleTo`). Add an `internal DuckDBConnection Connection => _connection;` property to `MultiIntervalReader`.

5. **`DisposeAsync_CompletesWithoutThrowing`** — create reader with 2 files; `await reader.DisposeAsync()`; no exception thrown. (A second dispose call also should not throw.)

---

## Build & Test Validation

After completing both tasks:

1. `dotnet build Tracer.sln --configuration Release` — must exit 0 with 0 errors, 0 warnings (warnings are errors).
2. `dotnet test Tracer.sln --configuration Release` — all existing 224 tests still pass; the new 12 tests (7 Bundle + 5 MultiInterval) also pass; total is 236.
3. Confirm new test file locations:
   - `tests/Tracer.Tests.Unit/Bundle/BundleManifestTests.cs`
   - `tests/Tracer.Tests.Unit/MultiInterval/AttachedDatabaseManagerTests.cs`
   - `tests/Tracer.Tests.Unit/MultiInterval/MultiIntervalReaderTests.cs`

---

## Suggested Commit Message

```
feat(bundle,multi-interval): implement Bundle Format and MultiIntervalReader assemblies (TRC-P4-001, TRC-P4-004)

TRC-P4-001 — Tracer.Bundle:
- Add src/Tracer.Bundle/ assembly (BundleManifest, BundleLayout, BundleSchemaV1, BundleNaming)
- Add tests/Tracer.Tests.Unit/Bundle/BundleManifestTests.cs (7 tests)

TRC-P4-004 — Tracer.Storage.DuckDB.MultiInterval:
- Add src/Tracer.Storage.DuckDB.MultiInterval/ assembly (IntervalDbFile, AttachedDatabaseManager, MultiIntervalReader)
- Add tests/Tracer.Tests.Unit/MultiInterval/AttachedDatabaseManagerTests.cs (5 tests)
- Add tests/Tracer.Tests.Unit/MultiInterval/MultiIntervalReaderTests.cs (5 tests)

Totals: 236 tests (224 existing + 12 new) — 0 failures. Build: exit 0.
```
