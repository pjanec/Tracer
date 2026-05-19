# Tracer Phase 1 — Detailed Design
## Core Foundation: Interfaces, Storage, Mock Data Source, Test Harness

*Companion to `tracer_architecture_v1.md`*
*Phase 1 of the build sequence (architecture §18)*
*C# / .NET 8 · Windows · May 2026*

*Phase 1 establishes the data model, the storage layer, the first mock adapter, and the test scaffolding that all subsequent phases build on. At the end of Phase 1, no end-user-facing functionality exists yet — but a developer can run a test that generates synthetic events through the full ingestion path into DuckDB and queries them back.*

---

## 1. Phase 1 Scope and Goals

### 1.1 What Phase 1 Delivers

- **`Tracer.Core`** assembly: domain types, abstract interfaces, value objects. No dependencies on infrastructure.
- **`Tracer.Storage.DuckDB`** assembly: DuckDB schema, Appender-based bulk ingestion, basic typed queries.
- **`Tracer.Adapters.Mock`** assembly: `MockDataSource` driven by scenario scripts; `SimulatedClock`; deterministic synthetic event generation.
- **`Tracer.TestHarness`** assembly: `TracerStackFixture` for integration tests; scenario script DSL; first two scenarios (`Calm`, `CombatEngagement`).
- **`Tracer.Tests.Unit`** and **`Tracer.Tests.Integration`** projects: first wave of tests covering data model invariants, storage correctness, mock determinism, end-to-end record flow.

### 1.2 What Phase 1 Does NOT Deliver

- No TracerAgent (deferred to Phase 2)
- No TracerObserver, no web API (deferred to Phase 3)
- No bundle export/import (deferred to Phase 4)
- No fast-state Parquet handling (deferred to Phase 7)
- No real DDS, sync, or NAS adapters (deferred to Phase 11)
- No frontend code (deferred to Phase 3)
- No interval rotation logic (deferred to Phase 2)
- No multi-process concerns; everything is in-process
- No performance tuning beyond avoiding obvious mistakes (real perf tests start Phase 2)

### 1.3 Success Criteria

Phase 1 is complete when these are all true:

1. A test can construct a `MockDataSource` with a named scenario and seed, run it for a simulated duration, write all generated records to a DuckDB file, and query them back with results matching the scenario's declared expectations.
2. The same test, run twice with the same seed, produces byte-identical DuckDB output (modulo file metadata like timestamps).
3. Unit tests cover: record construction validity, trace context propagation rules, time semantics, query result shapes.
4. Integration tests cover: full ingestion path with `MockDataSource` → `DuckDBStorageWriter` → query.
5. CI runs all tests in under 30 seconds for the fast suite.
6. The code compiles with `<Nullable>enable</Nullable>` and `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` clean.
7. The code passes `dotnet format` and a configured analyzer ruleset (specified in §13).

### 1.4 Estimated Duration

Two calendar weeks for one developer with .NET 8 and DuckDB experience. Add a week if learning DuckDB or DuckDB.NET is part of the work.

---

## 2. Solution and Project Layout

### 2.1 Repository Structure

```
tracer/
  Tracer.sln
  src/
    Tracer.Core/
      Tracer.Core.csproj
      Records/
        DiagnosticRecord.cs
        EventRecord.cs
        StateSampleRecord.cs
      Identity/
        TraceId.cs
        EventId.cs
        AgentId.cs
      Time/
        IClock.cs
        WallclockTime.cs
      Queries/
        EventFilter.cs
        EventQuery.cs
        QueryBucket.cs
      Abstractions/
        IDiagnosticDataSource.cs
        IDiagnosticStorageWriter.cs
        IDiagnosticStorageReader.cs
      Domain/
        TopicName.cs
        EntityId.cs
        Severity.cs
        SessionMarker.cs
      Errors/
        TracerException.cs
    Tracer.Storage.DuckDB/
      Tracer.Storage.DuckDB.csproj
      DuckDbStorageWriter.cs
      DuckDbStorageReader.cs
      Schema/
        SchemaV1.cs
        IndexDefinitions.cs
      Ingestion/
        EventAppender.cs
        StateAppender.cs
        BatchBuffer.cs
      Queries/
        EventQueryBuilder.cs
        BucketAggregator.cs
      Internal/
        ConnectionPool.cs
        Mapping.cs
    Tracer.Adapters.Mock/
      Tracer.Adapters.Mock.csproj
      MockDataSource.cs
      SimulatedClock.cs
      Scenarios/
        IScenarioScript.cs
        ScenarioContext.cs
        ScenarioConfig.cs
        ScenarioRegistry.cs
        Scripts/
          CalmScenario.cs
          CombatEngagementScenario.cs
      Generation/
        TraceIdGenerator.cs
        SyntheticPayloadBuilder.cs
    Tracer.TestHarness/
      Tracer.TestHarness.csproj
      TracerStackFixture.cs
      InMemoryStackOptions.cs
      Assertions/
        EventAssertions.cs
        StorageAssertions.cs
      Diagnostics/
        TestLogSink.cs
  tests/
    Tracer.Tests.Unit/
      Tracer.Tests.Unit.csproj
      Core/
        RecordTests.cs
        TraceIdTests.cs
        TimeTests.cs
      Storage/
        SchemaTests.cs
        AppenderTests.cs
        QueryBuilderTests.cs
      Mock/
        DeterminismTests.cs
        ScenarioTests.cs
    Tracer.Tests.Integration/
      Tracer.Tests.Integration.csproj
      EndToEndTests.cs
      ScenarioRoundTripTests.cs
  Directory.Build.props
  Directory.Packages.props
  .editorconfig
  global.json
```

### 2.2 Project File Conventions

**`global.json`**: pin .NET SDK to a specific 8.x version.

```json
{
  "sdk": {
    "version": "8.0.100",
    "rollForward": "latestFeature"
  }
}
```

**`Directory.Build.props`** (root, applies to all projects):

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <LangVersion>12</LangVersion>
    <Nullable>enable</Nullable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <EnableNETAnalyzers>true</EnableNETAnalyzers>
    <AnalysisLevel>latest</AnalysisLevel>
    <ImplicitUsings>enable</ImplicitUsings>
    <InvariantGlobalization>true</InvariantGlobalization>
    <RootNamespace>$(MSBuildProjectName)</RootNamespace>
  </PropertyGroup>
</Project>
```

**`Directory.Packages.props`** (centralized package versions):

```xml
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
    <CentralPackageTransitivePinningEnabled>true</CentralPackageTransitivePinningEnabled>
  </PropertyGroup>
  <ItemGroup>
    <PackageVersion Include="DuckDB.NET.Data" Version="1.0.2" />
    <PackageVersion Include="Microsoft.Extensions.Logging.Abstractions" Version="8.0.0" />
    <PackageVersion Include="Microsoft.Extensions.Time.Testing" Version="8.0.0" />
    <PackageVersion Include="xunit" Version="2.6.6" />
    <PackageVersion Include="xunit.runner.visualstudio" Version="2.5.6" />
    <PackageVersion Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
    <PackageVersion Include="FluentAssertions" Version="6.12.0" />
  </ItemGroup>
</Project>
```

**Per-project `.csproj`** files list only `<PackageReference Include="..."/>` without versions; versions come from `Directory.Packages.props`.

### 2.3 Dependency Graph

```
Tracer.Core                        (no project deps)
    ↑
Tracer.Storage.DuckDB              (deps: Tracer.Core, DuckDB.NET.Data)
    ↑
Tracer.Adapters.Mock               (deps: Tracer.Core)
    ↑
Tracer.TestHarness                 (deps: Tracer.Core, Tracer.Storage.DuckDB, Tracer.Adapters.Mock)
    ↑
Tracer.Tests.Unit                  (deps: Tracer.Core, Tracer.Storage.DuckDB, Tracer.Adapters.Mock, xunit, FluentAssertions)
Tracer.Tests.Integration           (deps: Tracer.TestHarness, xunit, FluentAssertions)
```

**Hard rule**: `Tracer.Core` references no infrastructure packages (no DuckDB, no Serilog, no ASP.NET). Only standard .NET 8 BCL. Verified by a CI check that fails the build if `Tracer.Core.csproj` gains a third-party package reference.

---

## 3. Tracer.Core: Domain Types and Interfaces

The Core assembly defines the *vocabulary* of Tracer. Every other component speaks in terms of these types.

### 3.1 Record Types

```csharp
namespace Tracer.Core.Records;

public abstract record DiagnosticRecord
{
    public required ulong SequenceNumber { get; init; }
    public required WallclockTime PublishWallclock { get; init; }
    public required WallclockTime ReceiveWallclock { get; init; }
    public required AgentId PublisherNode { get; init; }
    public required AgentId SubscriberNode { get; init; }
    public required TopicName Topic { get; init; }
}

public sealed record EventRecord : DiagnosticRecord
{
    public required EventId EventId { get; init; }
    public required TraceId TraceId { get; init; }
    public EventId? ParentEventId { get; init; }
    public EntityId? EntityId { get; init; }
    public string? OwningPlayerId { get; init; }
    public string? ScenarioPhase { get; init; }
    public Severity? Severity { get; init; }
    public string? NotableLabel { get; init; }
    public required string PayloadJson { get; init; }
}

public sealed record StateSampleRecord : DiagnosticRecord
{
    public required string InstanceKey { get; init; }
    public TraceId? TraceId { get; init; }
    public required string PayloadJson { get; init; }
    public required StateSampleRate Rate { get; init; }  // Slow or Fast
}

public enum StateSampleRate { Slow, Fast }
```

**Design notes:**

- `DiagnosticRecord` is `abstract` and `record`. Subclasses `sealed` to prevent accidental third subtype.
- All identity-bearing fields are `required` — construction without them fails at compile time.
- Nullable references for optional domain fields (entity, player, severity, etc.) — explicit absence rather than empty strings.
- `PayloadJson` is the full original payload as JSON. The promoted columns (`EntityId`, `OwningPlayerId`, etc.) are extracted at ingest by the agent (Phase 2); in Phase 1 the mock data source sets them directly.
- `ulong SequenceNumber` is per-publisher per-topic, used for gap detection in later phases.

### 3.2 Identity Types

All identity values are strongly typed to prevent confusion at API boundaries.

```csharp
namespace Tracer.Core.Identity;

public readonly record struct TraceId(ulong Value)
{
    public static TraceId None => new(0);
    public bool IsNone => Value == 0;
    public override string ToString() => Value.ToString("X16");
}

public readonly record struct EventId(ulong Value)
{
    public static EventId None => new(0);
    public bool IsNone => Value == 0;
    public override string ToString() => Value.ToString("X16");
}

public readonly record struct AgentId(string Value)
{
    public AgentId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("AgentId cannot be empty", nameof(value));
        if (value.Length > 64)
            throw new ArgumentException("AgentId max length 64", nameof(value));
        Value = value;
    }
    public override string ToString() => Value;
}
```

```csharp
namespace Tracer.Core.Domain;

public readonly record struct TopicName(string Value)
{
    public TopicName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("TopicName cannot be empty", nameof(value));
        Value = value;
    }
    public override string ToString() => Value;
}

public readonly record struct EntityId(string Value)
{
    public EntityId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("EntityId cannot be empty", nameof(value));
        Value = value;
    }
    public override string ToString() => Value;
}

public enum Severity { Info, Warning, Error }
```

**Rationale for value types**:

- Prevents `string entityId` from being passed where `string agentId` is expected.
- Zero allocation cost — `readonly record struct` is `Equals`/`GetHashCode`-correct by default.
- `ToString` overrides give readable diagnostic output.
- Validation in constructor — invalid values cannot exist.

### 3.3 Time Types

The cluster's synchronized wall-clock is the time reference. We model it as a strong type, not raw `DateTime`/`DateTimeOffset`, so that mistakes mixing OS clock with wall-clock are caught at compile time.

```csharp
namespace Tracer.Core.Time;

/// <summary>
/// A point in time on the cluster's synchronized wall-clock.
/// Stored as nanoseconds since Unix epoch UTC.
/// </summary>
public readonly record struct WallclockTime(long NanosecondsSinceEpoch)
    : IComparable<WallclockTime>
{
    public static WallclockTime Zero => new(0);
    public static WallclockTime MaxValue => new(long.MaxValue);

    public static WallclockTime FromUnixNanoseconds(long ns) => new(ns);

    public static WallclockTime FromDateTimeOffset(DateTimeOffset dto)
    {
        long ticks = dto.UtcTicks - DateTime.UnixEpoch.Ticks;
        return new WallclockTime(ticks * 100L);
    }

    public DateTimeOffset ToDateTimeOffset()
    {
        long ticks = NanosecondsSinceEpoch / 100L;
        return new DateTimeOffset(DateTime.UnixEpoch.AddTicks(ticks), TimeSpan.Zero);
    }

    public TimeSpan operator -(WallclockTime a, WallclockTime b)
        => TimeSpan.FromTicks((a.NanosecondsSinceEpoch - b.NanosecondsSinceEpoch) / 100L);

    public WallclockTime operator +(WallclockTime t, TimeSpan d)
        => new(t.NanosecondsSinceEpoch + d.Ticks * 100L);

    public int CompareTo(WallclockTime other) => NanosecondsSinceEpoch.CompareTo(other.NanosecondsSinceEpoch);

    public override string ToString() => ToDateTimeOffset().ToString("O");
}
```

```csharp
namespace Tracer.Core.Time;

/// <summary>
/// Abstraction for "current wallclock time" — allows test substitution.
/// </summary>
public interface IClock
{
    WallclockTime Now { get; }
}
```

**Production implementation lives in another assembly** (Tracer.Storage.DuckDB or wherever — Phase 1 just needs the interface). The mock implementation is `SimulatedClock` in `Tracer.Adapters.Mock`.

**Why ns since epoch as `long`**: matches DuckDB's `TIMESTAMP_NS` type exactly; round-trip is lossless; `long` range covers ±290 years from epoch which is plenty.

### 3.4 Filters and Queries

```csharp
namespace Tracer.Core.Queries;

public sealed record EventFilter
{
    public WallclockTime? From { get; init; }
    public WallclockTime? To { get; init; }
    public TopicName? Topic { get; init; }
    public AgentId? PublisherNode { get; init; }
    public AgentId? SubscriberNode { get; init; }
    public TraceId? TraceId { get; init; }
    public EntityId? EntityId { get; init; }
    public string? OwningPlayerId { get; init; }
    public Severity? MinSeverity { get; init; }
    public string? PayloadSearch { get; init; }

    public static EventFilter All => new();
    public static EventFilter ForTrace(TraceId traceId) => new() { TraceId = traceId };
    public static EventFilter ForEntity(EntityId entityId) => new() { EntityId = entityId };
}

public sealed record EventQuery
{
    public required EventFilter Filter { get; init; }
    public int Limit { get; init; } = 1000;
    public int Offset { get; init; } = 0;
    public QueryOrder Order { get; init; } = QueryOrder.PublishTimeAscending;
}

public enum QueryOrder
{
    PublishTimeAscending,
    PublishTimeDescending,
    SequenceNumberAscending
}

public readonly record struct QueryBucket(TimeSpan Width)
{
    public static QueryBucket FiveMinutes => new(TimeSpan.FromMinutes(5));
    public static QueryBucket ThirtySeconds => new(TimeSpan.FromSeconds(30));
    public static QueryBucket FiveSeconds => new(TimeSpan.FromSeconds(5));
}
```

### 3.5 Core Abstractions (Interfaces)

```csharp
namespace Tracer.Core.Abstractions;

/// <summary>
/// A source of diagnostic records. Implementations include DDS subscribers
/// (production) and mock scenario generators (development/test).
/// </summary>
public interface IDiagnosticDataSource
{
    IAsyncEnumerable<DiagnosticRecord> ReadAsync(CancellationToken ct);
}

/// <summary>
/// Writes diagnostic records to durable storage.
/// </summary>
public interface IDiagnosticStorageWriter : IAsyncDisposable
{
    Task AppendEventAsync(EventRecord record, CancellationToken ct);
    Task AppendStateAsync(StateSampleRecord record, CancellationToken ct);
    Task AppendBatchAsync(IReadOnlyList<DiagnosticRecord> records, CancellationToken ct);
    Task FlushAsync(CancellationToken ct);
}

/// <summary>
/// Reads diagnostic records from storage. Query-oriented.
/// </summary>
public interface IDiagnosticStorageReader : IAsyncDisposable
{
    Task<IReadOnlyList<EventRecord>> QueryEventsAsync(EventQuery query, CancellationToken ct);
    Task<EventRecord?> GetEventAsync(EventId eventId, CancellationToken ct);
    Task<long> CountEventsAsync(EventFilter filter, CancellationToken ct);
}
```

**Phase 1 boundary**: these three interfaces are enough for end-to-end testing. More interfaces (causal-tree queries, entity history, aggregation) are added in later phases. Don't over-design now.

---

## 4. Tracer.Storage.DuckDB: Persistence

### 4.1 DuckDB Version and Library

- DuckDB native version: pin to **v1.0.2** (or whichever is current at start of work)
- C# binding: `DuckDB.NET.Data` v1.0.2+
- DuckDB native DLL: bundled with `DuckDB.NET.Data` NuGet; verify Windows x64 binary is included.

Pinning the DuckDB version is important because file format compatibility across versions is not guaranteed. Phase 1 establishes "we use this version"; upgrades are explicit project decisions.

### 4.2 Schema (Version 1)

```csharp
namespace Tracer.Storage.DuckDB.Schema;

internal static class SchemaV1
{
    public const int Version = 1;

    public const string CreateEventsTable = """
        CREATE TABLE IF NOT EXISTS events (
            event_id            UBIGINT NOT NULL,
            trace_id            UBIGINT NOT NULL,
            parent_event_id     UBIGINT,
            sequence_number     UBIGINT NOT NULL,
            publish_wallclock   TIMESTAMP_NS NOT NULL,
            receive_wallclock   TIMESTAMP_NS NOT NULL,
            publisher_node      VARCHAR NOT NULL,
            subscriber_node     VARCHAR NOT NULL,
            topic               VARCHAR NOT NULL,
            entity_id           VARCHAR,
            owning_player_id    VARCHAR,
            scenario_phase      VARCHAR,
            severity            VARCHAR,
            notable_label       VARCHAR,
            payload             JSON NOT NULL
        );
        """;

    public const string CreateSlowStateTable = """
        CREATE TABLE IF NOT EXISTS slow_state (
            sequence_number     UBIGINT NOT NULL,
            publish_wallclock   TIMESTAMP_NS NOT NULL,
            receive_wallclock   TIMESTAMP_NS NOT NULL,
            publisher_node      VARCHAR NOT NULL,
            subscriber_node     VARCHAR NOT NULL,
            topic               VARCHAR NOT NULL,
            instance_key        VARCHAR NOT NULL,
            trace_id            UBIGINT,
            payload             JSON NOT NULL
        );
        """;

    public const string CreateSchemaMetaTable = """
        CREATE TABLE IF NOT EXISTS _schema_meta (
            schema_version  INTEGER NOT NULL,
            tracer_version  VARCHAR NOT NULL,
            created_at      TIMESTAMP_NS NOT NULL
        );
        """;

    public const string CreateIndexes = """
        CREATE INDEX IF NOT EXISTS idx_events_trace ON events(trace_id);
        CREATE INDEX IF NOT EXISTS idx_events_parent ON events(parent_event_id);
        CREATE INDEX IF NOT EXISTS idx_events_entity ON events(entity_id);
        CREATE INDEX IF NOT EXISTS idx_events_player ON events(owning_player_id);
        CREATE INDEX IF NOT EXISTS idx_events_topic_time ON events(topic, publish_wallclock);
        CREATE INDEX IF NOT EXISTS idx_state_instance_time ON slow_state(instance_key, publish_wallclock);
        CREATE INDEX IF NOT EXISTS idx_state_topic ON slow_state(topic);
        """;
}
```

**Indexes**: only on high-frequency point lookups. Time-range queries rely on DuckDB's zone maps and the time-ordered insertion. The `topic + publish_wallclock` index supports per-topic time-range queries which are common.

**No fast state table in Phase 1.** Fast state goes to Parquet in Phase 7. The mock data source in Phase 1 generates only events and slow state.

**Schema version stored in `_schema_meta`** as a single row. Future migrations check this row first.

### 4.3 DuckDbStorageWriter Implementation

```csharp
namespace Tracer.Storage.DuckDB;

public sealed class DuckDbStorageWriter : IDiagnosticStorageWriter
{
    private readonly DuckDBConnection _connection;
    private readonly DuckDBAppender _eventAppender;
    private readonly DuckDBAppender _stateAppender;
    private readonly BatchBuffer _buffer;
    private readonly object _lock = new();
    private bool _disposed;

    public static async Task<DuckDbStorageWriter> CreateAsync(
        string dbPath, 
        ILogger<DuckDbStorageWriter> logger,
        CancellationToken ct)
    {
        var connection = new DuckDBConnection($"Data Source={dbPath}");
        await connection.OpenAsync(ct);

        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = SchemaV1.CreateEventsTable;
            await cmd.ExecuteNonQueryAsync(ct);

            cmd.CommandText = SchemaV1.CreateSlowStateTable;
            await cmd.ExecuteNonQueryAsync(ct);

            cmd.CommandText = SchemaV1.CreateSchemaMetaTable;
            await cmd.ExecuteNonQueryAsync(ct);

            cmd.CommandText = SchemaV1.CreateIndexes;
            await cmd.ExecuteNonQueryAsync(ct);
        }

        // Insert schema meta if empty
        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = """
                INSERT INTO _schema_meta (schema_version, tracer_version, created_at)
                SELECT $v, $tv, $t
                WHERE NOT EXISTS (SELECT 1 FROM _schema_meta);
                """;
            cmd.Parameters.Add(new DuckDBParameter("v", SchemaV1.Version));
            cmd.Parameters.Add(new DuckDBParameter("tv", TracerVersion.Current));
            cmd.Parameters.Add(new DuckDBParameter("t", DateTimeOffset.UtcNow));
            await cmd.ExecuteNonQueryAsync(ct);
        }

        var eventAppender = connection.CreateAppender("events");
        var stateAppender = connection.CreateAppender("slow_state");

        return new DuckDbStorageWriter(connection, eventAppender, stateAppender, logger);
    }

    private DuckDbStorageWriter(
        DuckDBConnection connection,
        DuckDBAppender eventAppender,
        DuckDBAppender stateAppender,
        ILogger<DuckDbStorageWriter> logger)
    {
        _connection = connection;
        _eventAppender = eventAppender;
        _stateAppender = stateAppender;
        _buffer = new BatchBuffer(maxRecords: 10_000, maxAge: TimeSpan.FromMilliseconds(100));
    }

    public Task AppendEventAsync(EventRecord record, CancellationToken ct)
    {
        lock (_lock)
        {
            ThrowIfDisposed();
            WriteEventToAppender(record);
        }
        return Task.CompletedTask;
    }

    public Task AppendStateAsync(StateSampleRecord record, CancellationToken ct)
    {
        if (record.Rate == StateSampleRate.Fast)
            throw new NotSupportedException("Fast state not supported in Phase 1");

        lock (_lock)
        {
            ThrowIfDisposed();
            WriteStateToAppender(record);
        }
        return Task.CompletedTask;
    }

    public Task AppendBatchAsync(IReadOnlyList<DiagnosticRecord> records, CancellationToken ct)
    {
        lock (_lock)
        {
            ThrowIfDisposed();
            foreach (var record in records)
            {
                switch (record)
                {
                    case EventRecord ev:
                        WriteEventToAppender(ev);
                        break;
                    case StateSampleRecord st when st.Rate == StateSampleRate.Slow:
                        WriteStateToAppender(st);
                        break;
                    case StateSampleRecord st when st.Rate == StateSampleRate.Fast:
                        // Silently skipped in Phase 1; Phase 7 will route to Parquet
                        break;
                }
            }
        }
        return Task.CompletedTask;
    }

    public async Task FlushAsync(CancellationToken ct)
    {
        lock (_lock)
        {
            ThrowIfDisposed();
            _eventAppender.Close();
            _stateAppender.Close();
        }
        // Re-open appenders for continued writing
        // (DuckDB appender close is final; we recreate)
        // NOTE: this implementation is simplified; real production would
        // batch flush without close/reopen using DuckDB's flush semantics.
        await Task.CompletedTask;
    }

    private void WriteEventToAppender(EventRecord r)
    {
        var row = _eventAppender.CreateRow();
        row.AppendValue(r.EventId.Value);
        row.AppendValue(r.TraceId.Value);
        row.AppendNullableValue(r.ParentEventId?.Value);
        row.AppendValue(r.SequenceNumber);
        row.AppendValue(r.PublishWallclock.ToDateTimeOffset());
        row.AppendValue(r.ReceiveWallclock.ToDateTimeOffset());
        row.AppendValue(r.PublisherNode.Value);
        row.AppendValue(r.SubscriberNode.Value);
        row.AppendValue(r.Topic.Value);
        row.AppendNullableValue(r.EntityId?.Value);
        row.AppendNullableValue(r.OwningPlayerId);
        row.AppendNullableValue(r.ScenarioPhase);
        row.AppendNullableValue(r.Severity?.ToString());
        row.AppendNullableValue(r.NotableLabel);
        row.AppendValue(r.PayloadJson);
        row.EndRow();
    }

    private void WriteStateToAppender(StateSampleRecord r) { /* analogous */ }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(DuckDbStorageWriter));
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _eventAppender.Close();
        _stateAppender.Close();
        await _connection.DisposeAsync();
    }
}
```

**Important implementation notes (call out for the implementer):**

- **DuckDB.NET's Appender API specifics may differ** from what I've shown above. The method names (`CreateRow`, `AppendValue`, `EndRow`) and the appender lifecycle (Close behavior, flush semantics) are based on DuckDB.NET docs but may need adjustment when actually implementing. Treat the code shown as design intent, not literal API calls.
- **The `lock` around appender writes** is necessary because DuckDB appenders are not thread-safe. Phase 1 writers are single-producer, but the lock protects against test code accidentally driving the appender from multiple threads.
- **`FlushAsync` semantics need refinement**. The simplified close/reopen pattern shown is wasteful. Real implementation should use `_appender.Flush()` if DuckDB.NET exposes one, or accept that flush happens on dispose.
- **Connection per writer**, not pooled. Phase 1 is single-writer per file. Connection pooling becomes relevant in Phase 3 when the web API serves many concurrent readers against the same file.

### 4.4 DuckDbStorageReader Implementation

```csharp
namespace Tracer.Storage.DuckDB;

public sealed class DuckDbStorageReader : IDiagnosticStorageReader
{
    private readonly DuckDBConnection _connection;
    private readonly ILogger<DuckDbStorageReader> _logger;
    private bool _disposed;

    public static async Task<DuckDbStorageReader> OpenAsync(
        string dbPath,
        ILogger<DuckDbStorageReader> logger,
        CancellationToken ct)
    {
        var connection = new DuckDBConnection($"Data Source={dbPath};ACCESS_MODE=READ_ONLY");
        await connection.OpenAsync(ct);
        return new DuckDbStorageReader(connection, logger);
    }

    private DuckDbStorageReader(DuckDBConnection connection, ILogger<DuckDbStorageReader> logger)
    {
        _connection = connection;
        _logger = logger;
    }

    public async Task<IReadOnlyList<EventRecord>> QueryEventsAsync(
        EventQuery query, CancellationToken ct)
    {
        ThrowIfDisposed();
        var (sql, parameters) = EventQueryBuilder.Build(query);

        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters)
            cmd.Parameters.Add(new DuckDBParameter(name, value));

        var results = new List<EventRecord>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(Mapping.MapEventRecord(reader));
        }
        return results;
    }

    public async Task<EventRecord?> GetEventAsync(EventId eventId, CancellationToken ct)
    {
        ThrowIfDisposed();
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM events WHERE event_id = $eid LIMIT 1";
        cmd.Parameters.Add(new DuckDBParameter("eid", eventId.Value));

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
            return Mapping.MapEventRecord(reader);
        return null;
    }

    public async Task<long> CountEventsAsync(EventFilter filter, CancellationToken ct)
    {
        ThrowIfDisposed();
        var (sql, parameters) = EventQueryBuilder.BuildCount(filter);

        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters)
            cmd.Parameters.Add(new DuckDBParameter(name, value));

        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt64(result);
    }

    private void ThrowIfDisposed() { /* ... */ }
    public async ValueTask DisposeAsync() { /* ... */ }
}
```

### 4.5 EventQueryBuilder

Builds parameterized SQL from `EventQuery`. Critically: **never string-concatenate user input**; always use parameters.

```csharp
namespace Tracer.Storage.DuckDB.Queries;

internal static class EventQueryBuilder
{
    public static (string Sql, List<(string Name, object Value)> Parameters) Build(EventQuery query)
    {
        var sb = new StringBuilder("SELECT * FROM events WHERE 1=1");
        var parameters = new List<(string, object)>();

        AppendFilters(sb, parameters, query.Filter);

        sb.Append(query.Order switch
        {
            QueryOrder.PublishTimeAscending => " ORDER BY publish_wallclock ASC",
            QueryOrder.PublishTimeDescending => " ORDER BY publish_wallclock DESC",
            QueryOrder.SequenceNumberAscending => " ORDER BY publisher_node ASC, sequence_number ASC",
            _ => " ORDER BY publish_wallclock ASC"
        });

        sb.Append(" LIMIT $limit OFFSET $offset");
        parameters.Add(("limit", query.Limit));
        parameters.Add(("offset", query.Offset));

        return (sb.ToString(), parameters);
    }

    public static (string, List<(string, object)>) BuildCount(EventFilter filter)
    {
        var sb = new StringBuilder("SELECT COUNT(*) FROM events WHERE 1=1");
        var parameters = new List<(string, object)>();
        AppendFilters(sb, parameters, filter);
        return (sb.ToString(), parameters);
    }

    private static void AppendFilters(StringBuilder sb, List<(string, object)> parameters, EventFilter f)
    {
        if (f.From.HasValue)
        {
            sb.Append(" AND publish_wallclock >= $from");
            parameters.Add(("from", f.From.Value.ToDateTimeOffset()));
        }
        if (f.To.HasValue)
        {
            sb.Append(" AND publish_wallclock < $to");
            parameters.Add(("to", f.To.Value.ToDateTimeOffset()));
        }
        if (f.Topic.HasValue)
        {
            sb.Append(" AND topic = $topic");
            parameters.Add(("topic", f.Topic.Value.Value));
        }
        if (f.PublisherNode.HasValue)
        {
            sb.Append(" AND publisher_node = $pub");
            parameters.Add(("pub", f.PublisherNode.Value.Value));
        }
        if (f.SubscriberNode.HasValue)
        {
            sb.Append(" AND subscriber_node = $sub");
            parameters.Add(("sub", f.SubscriberNode.Value.Value));
        }
        if (f.TraceId.HasValue)
        {
            sb.Append(" AND trace_id = $tid");
            parameters.Add(("tid", f.TraceId.Value.Value));
        }
        if (f.EntityId.HasValue)
        {
            sb.Append(" AND entity_id = $eid");
            parameters.Add(("eid", f.EntityId.Value.Value));
        }
        if (f.OwningPlayerId is not null)
        {
            sb.Append(" AND owning_player_id = $pid");
            parameters.Add(("pid", f.OwningPlayerId));
        }
        if (f.MinSeverity.HasValue)
        {
            // Severity ordering: Info < Warning < Error
            sb.Append(" AND severity IN (");
            var sevs = ((Severity[])Enum.GetValues(typeof(Severity)))
                .Where(s => s >= f.MinSeverity.Value)
                .Select(s => s.ToString())
                .ToList();
            for (int i = 0; i < sevs.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append('$').Append("sev").Append(i);
                parameters.Add(($"sev{i}", sevs[i]));
            }
            sb.Append(')');
        }
        if (f.PayloadSearch is not null)
        {
            // Slow path — full text search across JSON
            sb.Append(" AND payload::VARCHAR LIKE $search");
            parameters.Add(("search", $"%{EscapeLike(f.PayloadSearch)}%"));
        }
    }

    private static string EscapeLike(string s) => s.Replace("%", "\\%").Replace("_", "\\_");
}
```

### 4.6 BatchBuffer

Buffers records for batch flush. Phase 1 keeps this simple:

```csharp
namespace Tracer.Storage.DuckDB.Ingestion;

internal sealed class BatchBuffer
{
    private readonly int _maxRecords;
    private readonly TimeSpan _maxAge;
    private readonly List<DiagnosticRecord> _records;
    private DateTime _firstAddedAt;

    public BatchBuffer(int maxRecords, TimeSpan maxAge)
    {
        _maxRecords = maxRecords;
        _maxAge = maxAge;
        _records = new List<DiagnosticRecord>(maxRecords);
    }

    public bool ShouldFlush => _records.Count >= _maxRecords
        || (_records.Count > 0 && DateTime.UtcNow - _firstAddedAt >= _maxAge);

    public void Add(DiagnosticRecord r)
    {
        if (_records.Count == 0) _firstAddedAt = DateTime.UtcNow;
        _records.Add(r);
    }

    public IReadOnlyList<DiagnosticRecord> DrainAll()
    {
        var copy = _records.ToArray();
        _records.Clear();
        return copy;
    }
}
```

In Phase 1 this is used inside the writer for grouping records before Appender flush. Phase 2 elaborates with proper async backpressure.

---

## 5. Tracer.Adapters.Mock: The Mock Data Source

The mock data source is the development workhorse of Phase 1. Everything is tested against it before any real adapter exists.

### 5.1 Design Principles

1. **Deterministic given a seed**. Two runs with the same `(scenarioName, seed)` produce identical record sequences.
2. **Scenario-shaped**, not noise. Records reflect realistic causal structures, entity lifecycles, and trace propagation.
3. **Time-controllable via SimulatedClock**. Tests can run "8 hours of simulation" in milliseconds.
4. **Composable scenarios**. The DSL lets new scenarios be written as small declarative scripts.
5. **In-process only** for Phase 1. No DDS, no network, no inter-process concerns.

### 5.2 SimulatedClock

```csharp
namespace Tracer.Adapters.Mock;

public sealed class SimulatedClock : IClock
{
    private long _nanosSinceEpoch;
    private readonly object _lock = new();

    public SimulatedClock(WallclockTime initial)
    {
        _nanosSinceEpoch = initial.NanosecondsSinceEpoch;
    }

    public WallclockTime Now
    {
        get
        {
            lock (_lock)
                return new WallclockTime(_nanosSinceEpoch);
        }
    }

    public void Advance(TimeSpan delta)
    {
        lock (_lock)
            _nanosSinceEpoch += delta.Ticks * 100L;
    }

    public void Set(WallclockTime time)
    {
        lock (_lock)
            _nanosSinceEpoch = time.NanosecondsSinceEpoch;
    }
}
```

### 5.3 Scenario Script Abstraction

```csharp
namespace Tracer.Adapters.Mock.Scenarios;

public interface IScenarioScript
{
    string Name { get; }
    IAsyncEnumerable<DiagnosticRecord> ExecuteAsync(
        ScenarioContext context, CancellationToken ct);
}

public sealed class ScenarioContext
{
    public required SimulatedClock Clock { get; init; }
    public required Random Random { get; init; }       // seeded, deterministic
    public required ScenarioConfig Config { get; init; }
    public required TraceIdGenerator TraceIdGen { get; init; }
}

public sealed record ScenarioConfig
{
    public TimeSpan Duration { get; init; } = TimeSpan.FromMinutes(5);
    public int NodeCount { get; init; } = 3;
    public int EntityCount { get; init; } = 10;
    public double EventsPerSecond { get; init; } = 100;
    public int Seed { get; init; } = 42;
    public WallclockTime StartTime { get; init; } 
        = WallclockTime.FromDateTimeOffset(new DateTimeOffset(2026, 5, 19, 14, 0, 0, TimeSpan.Zero));
}
```

### 5.4 TraceIdGenerator (Deterministic)

```csharp
namespace Tracer.Adapters.Mock.Generation;

public sealed class TraceIdGenerator
{
    private readonly Random _random;
    private ulong _nextEventId = 1;

    public TraceIdGenerator(Random seededRandom)
    {
        _random = seededRandom;
    }

    public TraceId NewTrace()
    {
        ulong v;
        do
        {
            byte[] bytes = new byte[8];
            _random.NextBytes(bytes);
            v = BitConverter.ToUInt64(bytes, 0);
        } while (v == 0);
        return new TraceId(v);
    }

    public EventId NewEvent()
    {
        return new EventId(_nextEventId++);
    }
}
```

**Deterministic design**: `Random` is constructed from the scenario's seed, and `_nextEventId` is sequential. Two runs with the same seed produce identical event IDs and trace IDs. This is essential for tests asserting specific properties of generated data.

Production trace ID generation (in the simulation, not in Tracer) uses cryptographic randomness for global uniqueness — but the mock can be deterministic because tests need reproducibility.

### 5.5 First Scenarios

**CalmScenario** — minimal load, single phase, baseline performance reference.

```csharp
namespace Tracer.Adapters.Mock.Scenarios.Scripts;

public sealed class CalmScenario : IScenarioScript
{
    public string Name => "Calm";

    public async IAsyncEnumerable<DiagnosticRecord> ExecuteAsync(
        ScenarioContext ctx, [EnumeratorCancellation] CancellationToken ct)
    {
        var endTime = ctx.Clock.Now + ctx.Config.Duration;
        var nodes = Enumerable.Range(0, ctx.Config.NodeCount)
            .Select(i => new AgentId($"node-{i:D2}"))
            .ToArray();
        var entities = Enumerable.Range(0, ctx.Config.EntityCount)
            .Select(i => new EntityId($"entity:{i:D3}"))
            .ToArray();

        ulong sequence = 0;
        var intervalSec = 1.0 / ctx.Config.EventsPerSecond;

        // Session start event
        yield return MakeSessionStartEvent(ctx, sequence++, nodes[0]);

        while (ctx.Clock.Now < endTime && !ct.IsCancellationRequested)
        {
            var node = nodes[ctx.Random.Next(nodes.Length)];
            var entity = entities[ctx.Random.Next(entities.Length)];

            yield return new EventRecord
            {
                EventId = ctx.TraceIdGen.NewEvent(),
                TraceId = ctx.TraceIdGen.NewTrace(),
                ParentEventId = null,
                SequenceNumber = sequence++,
                PublishWallclock = ctx.Clock.Now,
                ReceiveWallclock = ctx.Clock.Now + TimeSpan.FromMilliseconds(1),
                PublisherNode = node,
                SubscriberNode = node,
                Topic = new TopicName("scenario.heartbeat"),
                EntityId = entity,
                OwningPlayerId = null,
                ScenarioPhase = "calm",
                Severity = null,
                NotableLabel = null,
                PayloadJson = $$"""{ "kind": "heartbeat", "node": "{{node}}" }"""
            };

            ctx.Clock.Advance(TimeSpan.FromSeconds(intervalSec));
            await Task.Yield();
        }
    }

    private static EventRecord MakeSessionStartEvent(ScenarioContext ctx, ulong seq, AgentId node) =>
        new()
        {
            EventId = ctx.TraceIdGen.NewEvent(),
            TraceId = ctx.TraceIdGen.NewTrace(),
            SequenceNumber = seq,
            PublishWallclock = ctx.Clock.Now,
            ReceiveWallclock = ctx.Clock.Now,
            PublisherNode = node,
            SubscriberNode = node,
            Topic = new TopicName("system.session_start"),
            ScenarioPhase = "calm",
            NotableLabel = "Calm session started",
            PayloadJson = """{ "scenarioId": "calm", "label": "Calm scenario test session" }"""
        };
}
```

**CombatEngagementScenario** — bursts of events with causal trees from player actions.

This is the first scenario that exercises trace_id and parent_event_id propagation. Sketch:

```csharp
public sealed class CombatEngagementScenario : IScenarioScript
{
    public string Name => "CombatEngagement";

    public async IAsyncEnumerable<DiagnosticRecord> ExecuteAsync(
        ScenarioContext ctx, [EnumeratorCancellation] CancellationToken ct)
    {
        // Cluster setup: 4 nodes — blue-cmd, blue-veh, red-cmd, red-veh
        // Entities: 5 blue vehicles, 5 red vehicles
        // Phase: "approach" → "engagement" → "withdrawal"
        // 
        // During engagement, each "shot fired" event from a player triggers a chain:
        //   shot_fired (root, on shooter's node)
        //     → projectile_spawn (child, on shooter's node)
        //       → projectile_impact (child, on target's node) 
        //         → damage_applied (child, on target's node)
        //           → state_change to vehicle damage state (slow state with same trace_id)
        //         → effect_spawn × 3 (visual effects, on target's node)
        //
        // ~50 shots over the engagement phase, each producing ~7 events
        // Plus background heartbeat traffic at lower rate
        
        // Implementation details omitted but follow the same yield-returning pattern
        // as CalmScenario, with the additional complexity of trace context propagation
        // and multi-node generation.
        
        yield break; // placeholder
    }
}
```

**Both scenarios are deterministic given the seed.** A test that runs `CombatEngagementScenario` with seed 42 always generates exactly the same events in the same order with the same trace IDs.

### 5.6 ScenarioRegistry

```csharp
namespace Tracer.Adapters.Mock.Scenarios;

public static class ScenarioRegistry
{
    private static readonly Dictionary<string, Func<IScenarioScript>> _scenarios = new()
    {
        ["Calm"] = () => new Scripts.CalmScenario(),
        ["CombatEngagement"] = () => new Scripts.CombatEngagementScenario(),
    };

    public static IScenarioScript Get(string name)
    {
        if (!_scenarios.TryGetValue(name, out var factory))
            throw new ArgumentException($"Unknown scenario: {name}", nameof(name));
        return factory();
    }

    public static IReadOnlyCollection<string> AvailableScenarios => _scenarios.Keys;
}
```

### 5.7 MockDataSource

```csharp
namespace Tracer.Adapters.Mock;

public sealed class MockDataSource : IDiagnosticDataSource
{
    private readonly IScenarioScript _script;
    private readonly ScenarioContext _context;

    public MockDataSource(string scenarioName, ScenarioConfig config)
    {
        _script = ScenarioRegistry.Get(scenarioName);
        var clock = new SimulatedClock(config.StartTime);
        var random = new Random(config.Seed);
        var traceGen = new TraceIdGenerator(random);
        _context = new ScenarioContext
        {
            Clock = clock,
            Random = random,
            Config = config,
            TraceIdGen = traceGen
        };
    }

    public SimulatedClock Clock => _context.Clock;

    public IAsyncEnumerable<DiagnosticRecord> ReadAsync(CancellationToken ct)
        => _script.ExecuteAsync(_context, ct);
}
```

---

## 6. Tracer.TestHarness

The harness provides the integration testing primitives all subsequent phases build on.

### 6.1 TracerStackFixture

```csharp
namespace Tracer.TestHarness;

public sealed class TracerStackFixture : IAsyncDisposable
{
    public MockDataSource DataSource { get; private set; } = null!;
    public DuckDbStorageWriter Writer { get; private set; } = null!;
    public DuckDbStorageReader Reader { get; private set; } = null!;
    public string DbPath { get; private set; } = null!;

    private string _tempDir = null!;
    private CancellationTokenSource _cts = null!;

    public static async Task<TracerStackFixture> CreateAsync(
        string scenarioName,
        int seed = 42,
        TimeSpan? duration = null,
        InMemoryStackOptions? options = null,
        CancellationToken ct = default)
    {
        options ??= new InMemoryStackOptions();
        var fixture = new TracerStackFixture
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"tracer-test-{Guid.NewGuid():N}"),
            _cts = new CancellationTokenSource()
        };
        Directory.CreateDirectory(fixture._tempDir);

        fixture.DbPath = Path.Combine(fixture._tempDir, "events.duckdb");

        var config = new ScenarioConfig
        {
            Seed = seed,
            Duration = duration ?? TimeSpan.FromMinutes(5),
            NodeCount = options.NodeCount,
            EntityCount = options.EntityCount,
            EventsPerSecond = options.EventsPerSecond
        };

        fixture.DataSource = new MockDataSource(scenarioName, config);
        fixture.Writer = await DuckDbStorageWriter.CreateAsync(
            fixture.DbPath, 
            NullLogger<DuckDbStorageWriter>.Instance, 
            ct);

        return fixture;
    }

    public async Task RunScenarioAsync(CancellationToken ct = default)
    {
        await foreach (var record in DataSource.ReadAsync(ct))
        {
            switch (record)
            {
                case EventRecord ev:
                    await Writer.AppendEventAsync(ev, ct);
                    break;
                case StateSampleRecord state when state.Rate == StateSampleRate.Slow:
                    await Writer.AppendStateAsync(state, ct);
                    break;
            }
        }
        await Writer.FlushAsync(ct);
        
        // Open reader after writer is flushed
        Reader = await DuckDbStorageReader.OpenAsync(
            DbPath,
            NullLogger<DuckDbStorageReader>.Instance,
            ct);
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        if (Reader is not null) await Reader.DisposeAsync();
        if (Writer is not null) await Writer.DisposeAsync();
        try
        {
            Directory.Delete(_tempDir, recursive: true);
        }
        catch { /* best effort cleanup */ }
        _cts.Dispose();
    }
}

public sealed record InMemoryStackOptions
{
    public int NodeCount { get; init; } = 3;
    public int EntityCount { get; init; } = 10;
    public double EventsPerSecond { get; init; } = 100;
}
```

### 6.2 Fluent Assertions Extensions

```csharp
namespace Tracer.TestHarness.Assertions;

public static class EventAssertions
{
    public static void ShouldFormValidTrace(this IEnumerable<EventRecord> events)
    {
        var list = events.ToList();
        var traceIds = list.Select(e => e.TraceId).Distinct().ToList();
        traceIds.Count.Should().Be(1, "all events should share one trace_id");

        var eventIds = list.Select(e => e.EventId).ToHashSet();
        foreach (var e in list)
        {
            if (e.ParentEventId is { } parent)
            {
                eventIds.Should().Contain(parent, 
                    $"event {e.EventId} has parent {parent} but parent not in trace");
            }
        }
    }

    public static void ShouldBeTimeOrdered(this IEnumerable<EventRecord> events)
    {
        var list = events.ToList();
        for (int i = 1; i < list.Count; i++)
        {
            list[i].PublishWallclock.Should().BeGreaterThanOrEqualTo(
                list[i - 1].PublishWallclock,
                $"event at index {i} should not have earlier publish_wallclock than predecessor");
        }
    }
}
```

---

## 7. Test Plan

### 7.1 Unit Tests (Tracer.Tests.Unit)

**Core/RecordTests.cs**
- `EventRecord` construction requires all `required` fields
- `EventRecord` with `ParentEventId = null` is valid (root event)
- `TraceId.None` and `EventId.None` behave correctly
- Records with same fields are equal (record semantics)
- `WallclockTime` arithmetic: subtraction yields `TimeSpan`, addition with `TimeSpan` works
- `WallclockTime` round-trips through `DateTimeOffset` losslessly at ns precision

**Core/TraceIdTests.cs**
- `TraceId(0)` is `None`
- `TraceId` formats as 16-char uppercase hex
- Equality works across construction paths
- `EntityId`, `AgentId`, `TopicName` reject empty / null

**Core/TimeTests.cs**
- `SimulatedClock` advances exactly when told; never spontaneously
- Two `SimulatedClock` instances at same initial time return same `Now`
- `WallclockTime` comparisons consistent with `long.CompareTo`

**Storage/SchemaTests.cs**
- Creating a fresh DB writes schema and meta row
- Opening existing DB doesn't recreate
- Schema version stored is `SchemaV1.Version`
- All indexes created

**Storage/AppenderTests.cs**
- 1000 events written and read back match exactly
- Null fields written as NULL, read back as null
- Concurrent writes from same writer block on lock (no corruption)
- Writer disposes cleanly
- Reader sees data only after writer flush

**Storage/QueryBuilderTests.cs**
- Each filter field produces expected SQL fragment
- Multiple filters combine with AND
- Time range applied to `publish_wallclock`
- `MinSeverity` filter expands to IN clause
- Parameterization prevents SQL injection (test with malicious-looking inputs)
- LIMIT and OFFSET applied

**Mock/DeterminismTests.cs**
- Two `MockDataSource` instances with same `(scenario, seed)` produce identical record sequences (byte-equal payloads, equal IDs)
- Different seeds produce different sequences
- `SimulatedClock` advances match across runs

**Mock/ScenarioTests.cs**
- `CalmScenario` produces ≥1 event per second target rate (within tolerance)
- `CalmScenario` ends within `Duration` ± one event interval
- `CombatEngagementScenario` produces causally valid traces (every parent_event_id refers to an existing event in the same trace)
- Session-start event is always first in any scenario output

### 7.2 Integration Tests (Tracer.Tests.Integration)

**EndToEndTests.cs**
- `Calm` scenario for 1 simulated minute → run through fixture → query returns expected event count
- `CombatEngagement` for 30 simulated seconds → query by `trace_id` returns events forming valid causal trees
- Query with `EventFilter.ForEntity` returns only events for that entity
- Query with time range returns only events in range
- Query with `Limit` respects limit
- `GetEventAsync` returns the specific event
- `CountEventsAsync` matches actual event count from full query

**ScenarioRoundTripTests.cs**
- Run scenario, write to DB, close, reopen, query → results identical
- Run scenario twice with same seed, write to two separate DBs, query both → results identical (modulo timestamps)

### 7.3 Test Performance

All unit tests should complete in **under 10 seconds total**. Integration tests should complete in **under 30 seconds total**. Anything slower is a bug — either the test is doing too much or the implementation is too slow.

`SimulatedClock` means "1 minute of simulation" doesn't take 1 minute wall-clock. It takes however long generating and processing the events takes — typically a few hundred milliseconds.

### 7.4 CI Configuration

```yaml
# .github/workflows/ci.yml (or equivalent for your CI)
jobs:
  build-and-test:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '8.0.x'
      - run: dotnet restore
      - run: dotnet build --no-restore --configuration Release
      - run: dotnet test --no-build --configuration Release --logger trx
      - uses: dorny/test-reporter@v1
        with:
          name: Tests
          path: '**/*.trx'
          reporter: dotnet-trx
```

`windows-latest` because DuckDB.NET ships Windows binaries and Phase 1 targets Windows. Phase 2 may add Linux for CI portability.

---

## 8. Coding Standards and Conventions

### 8.1 General Conventions

- **C# 12 features used freely**: primary constructors, collection expressions, alias any type
- **`var` for locals when type is obvious from RHS**; explicit type when not
- **Async all the way**: no `.Result`, no `.Wait()`, no `Task.Run` to fake async over sync
- **`CancellationToken` parameter on every async public method**, accepted positionally last
- **No `Task.FromResult` in hot paths**; prefer `ValueTask` where micro-allocations matter (deferred — start with `Task`, optimize if needed)
- **Logging via `ILogger<T>` injected**; never `Console.WriteLine` outside `Main`
- **Exceptions for exceptional cases only**; expected outcomes return result types

### 8.2 Naming

- Interfaces start with `I` (e.g., `IDiagnosticDataSource`)
- Async methods end with `Async`
- Private fields start with `_` and lower camel case
- Public properties are PascalCase
- Constants are PascalCase

### 8.3 File and Folder Organization

- One public type per file (with allowance for small related types)
- Folder structure matches namespace
- Internal types prefixed `Internal` or in `Internal/` folder

### 8.4 Analyzer Configuration

`.editorconfig` enables:
- `CA1051` (Do not declare visible instance fields) — error
- `CA1062` (Validate arguments of public methods) — error
- `CA2007` (ConfigureAwait) — disabled (this is a library application, not a library; default sync context is fine)
- `IDE0079` (Remove unnecessary suppression) — error

### 8.5 Forbidden Patterns

- **No `dynamic`** anywhere
- **No reflection** outside test code
- **No singletons or static mutable state** outside well-scoped utilities (scenario registry is acceptable; anything that holds runtime state is not)
- **No `Thread.Sleep`** in production code; use `Task.Delay` or `IClock` advancement
- **No `DateTime.Now` / `DateTimeOffset.Now`** outside log labeling; use `IClock` injection
- **No `System.Random` shared statically** in tests; always seed and pass

---

## 9. Configuration

Phase 1 has minimal configuration needs because nothing is yet a long-running process. Configuration is per-test-fixture, set in code.

For Phase 2 and beyond, configuration files will exist. Phase 1 defines the patterns:

- Configuration via `Microsoft.Extensions.Configuration` (in later phases)
- JSON files only, no XML, no INI
- Absolute paths only (no relative path inference)
- Per-process configuration files: `agent.json`, `observer.json`, etc.

For Phase 1, the fixture's `InMemoryStackOptions` is the only "configuration."

---

## 10. Logging

Phase 1 uses `NullLogger<T>` from `Microsoft.Extensions.Logging.Abstractions` as the default — components log to a null sink, tests can substitute test sinks if they want to assert logging behavior.

Real logging configuration (Serilog, JSON sinks, `LOG_FILE=` first-line convention) is set up in Phase 2 when the first long-running process exists.

The discipline established now: every component takes `ILogger<TSelf>` via constructor. Even when the default is `NullLogger`, the parameter is there. This makes Phase 2's logging activation a configuration change, not a code change.

---

## 11. Error Handling

### 11.1 Exception Types

```csharp
namespace Tracer.Core.Errors;

public class TracerException : Exception
{
    public TracerException(string message) : base(message) { }
    public TracerException(string message, Exception inner) : base(message, inner) { }
}

public sealed class TracerStorageException : TracerException
{
    public TracerStorageException(string message) : base(message) { }
    public TracerStorageException(string message, Exception inner) : base(message, inner) { }
}

public sealed class TracerScenarioException : TracerException
{
    public TracerScenarioException(string message) : base(message) { }
}
```

Specific exception types for specific failure domains. Catchers can target precisely.

### 11.2 Argument Validation

Public methods validate arguments at the boundary:

```csharp
public Task AppendEventAsync(EventRecord record, CancellationToken ct)
{
    ArgumentNullException.ThrowIfNull(record);
    ct.ThrowIfCancellationRequested();
    // ...
}
```

`required` properties on records mean construction already validates presence; method-level checks cover null references for non-required value-typed inputs.

### 11.3 No Silent Failures

Phase 1 has no "log and continue" patterns. Every error either:
- Throws (propagates to caller)
- Returns a Result type (deferred — Phase 1 uses exceptions for simplicity)

If a record fails to write, it throws. The fixture's `RunScenarioAsync` propagates the exception. Tests see the failure immediately.

---

## 12. Phase 1 Risks and Mitigations

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| DuckDB.NET Appender API behaves differently than documented | Medium | Medium | Spike day 1: write 10K events, read back, verify behavior. Adjust design if needed before deeper implementation. |
| `TIMESTAMP_NS` precision not actually nanoseconds in DuckDB.NET binding | Low | Medium | Spike: round-trip a `WallclockTime` through DuckDB, verify ns precision preserved. Fall back to `TIMESTAMP` (microsecond) if needed. |
| `SimulatedClock` causes hidden ordering bugs in tests | Low | Low | Document clearly: `Clock.Now` is only safe to call from one thread per scenario; scenarios advance clock explicitly. |
| Mock scenarios become a maintenance burden | Medium | Low | Keep scenarios small (one file each). Start with two. Add more only as later phases demand them. |
| Test fixture leaks temp directories on failure | High | Low | Best-effort cleanup in `DisposeAsync`; CI runner cleans temp at end. Not worth heroic effort. |
| Schema design wrong, requires migration in Phase 2 | Medium | Medium | Schema versioning is in place from day 1. A v1 → v2 migration is a defined later task. Don't try to predict future schema. |

---

## 13. Definition of Done for Phase 1

Phase 1 is shipped when all of the following are true:

- [ ] All projects compile clean with `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`
- [ ] `Tracer.Core.csproj` has no third-party `<PackageReference>` (verified by CI check)
- [ ] All unit tests pass (target: 50+ test methods)
- [ ] All integration tests pass (target: 5+ test methods)
- [ ] Full test suite runs in under 30 seconds on CI
- [ ] `dotnet format --verify-no-changes` passes
- [ ] `CalmScenario` and `CombatEngagementScenario` are implemented and round-trip through fixture
- [ ] `CombatEngagementScenario` events form valid causal trees (verified by `ShouldFormValidTrace`)
- [ ] Two runs of same `(scenario, seed)` produce identical record sequences (determinism test passes)
- [ ] `README.md` for Tracer project explaining: what Phase 1 covers, how to build, how to run tests, the example query of "load Calm scenario into fixture, count events"
- [ ] At least one code review pass by another developer
- [ ] No `TODO` comments without a tracked issue link

---

## 14. Handoff to Phase 2

What Phase 2 inherits from Phase 1:

- **`Tracer.Core`** is the stable vocabulary. Phase 2 may add new abstractions (e.g., `IAgentTransport`) but does not modify existing ones except to make additive changes.
- **`Tracer.Storage.DuckDB`** is the storage implementation. Phase 2 extends it with interval-folder-based directory layout and rotation logic.
- **`Tracer.Adapters.Mock`** is the test data source. Phase 2 uses it heavily for testing the agent without needing real DDS.
- **`Tracer.TestHarness`** is the integration test scaffolding. Phase 2 builds `TracerAgentFixture` and `MultiNodeFixture` on top of it.

What Phase 2 must address that Phase 1 deferred:

- Long-running process model (`IHostedService`, `IHostApplicationLifetime`)
- Real logging configuration (Serilog, `LOG_FILE=` convention)
- Configuration files (`agent.json`)
- Interval rotation logic (the central new piece in Phase 2)
- Recovery from missing `_ready` sentinel
- The shared-memory ingestion transport (or its mock)
- Per-interval directory layout

Phase 1's job is to make Phase 2 a focused effort: when the agent process is built, the data model, storage, and test scaffolding are already solid.
