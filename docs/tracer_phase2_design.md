# Tracer Phase 2 — Detailed Design
## TracerAgent, Interval Rotation, Fast State, FakeNode

*Companion to `tracer_architecture_v1.md` and `tracer_phase1_design.md`*
*Phase 2 of the build sequence (architecture §18)*
*C# / .NET 8 · Windows · May 2026*

*Phase 2 turns Phase 1's foundation into a long-running process. The TracerAgent becomes a real Windows service capable of capturing data continuously, rotating storage at interval boundaries, recovering from crashes, and handing completed intervals to a mock upload service. The FakeNode application combines agent and mock data source into a runnable development tool.*

---

## 1. Phase 2 Scope and Goals

### 1.1 What Phase 2 Delivers

- **`Tracer.Agent`** assembly and **`tracer-agent.exe`** runnable Windows service / console app
- **`Tracer.Storage.DuckDB.Parquet`** extension to the storage assembly for fast-state Parquet writers
- **`Tracer.FakeNode`** assembly and **`tracer-fakenode.exe`** runnable development tool
- **Interval rotation lifecycle**: open → write → close → finalize → emit upload request → open next
- **Recovery protocol**: detect orphaned intervals on startup; finalize them; continue
- **Manifest generation**: per-interval JSON metadata with session markers, gap reporting, counts
- **`IAgentTransport`** abstraction with `InProcessChannelTransport` (mock) implementation
- **`ITelemetryUploadService`** abstraction with `LocalFileSystemUploadService` (mock) implementation
- **Backpressure handling**: detect transport saturation; drop fast-state first, then slow-state, never events (until forced)
- **Structured logging** activated for the first time (Serilog, `LOG_FILE=` convention)
- **Graceful shutdown** via `IHostApplicationLifetime`
- Tests covering: rotation correctness, recovery from missing `_ready`, manifest accuracy, multi-interval scenarios

### 1.2 What Phase 2 Does NOT Deliver

- No central observer (Phase 3)
- No web API or frontend (Phase 3)
- No bundle export (Phase 4)
- No real sync system integration (Phase 11) — uses `LocalFileSystemUploadService` mock
- No real DDS adapter — agent receives records from `IAgentTransport` only
- No multi-process concerns yet beyond agent ↔ mock-simulation; shared memory is Phase 11
- No reading from completed intervals (the agent only writes; reading is the aggregator's job in Phase 4)

### 1.3 Success Criteria

Phase 2 is complete when all of the following are true:

1. **`tracer-agent.exe` runs as a Windows service or as a console app**, captures data from a configured transport, rotates intervals at wall-clock boundaries, generates valid manifests with `_ready` sentinels.
2. **Killing the agent mid-interval** (Process.Kill or power loss simulation) and restarting it produces correct recovery: the orphaned interval is finalized with appropriate `captureGaps`, capture continues into a new interval.
3. **`tracer-fakenode.exe` runs end-to-end**: spawns mock data source, feeds the agent via in-process transport, writes intervals to disk, hands them to mock upload service. A multi-hour simulated scenario completes without data loss in healthy conditions.
4. **Backpressure tests pass**: when configured ingestion rate exceeds writer capacity, fast-state samples drop first, slow-state drops next, events drop last, and all drops are reported in `captureGaps`.
5. **Logs follow conventions**: `LOG_FILE=` is the first stdout line; logs are valid JSON, one event per line; structured fields are present for state machine transitions.
6. **All Phase 1 tests still pass**.
7. **New tests pass**: rotation, recovery, manifest correctness, backpressure, FakeNode end-to-end.
8. **No mid-rotation data loss** in healthy operation, verified by a test that counts records published vs records captured across an interval boundary.

### 1.4 Estimated Duration

Two to three calendar weeks for one developer. The "extra week" buffer compared to Phase 1 reflects:
- First long-running process needs care around lifecycle and error handling
- Parquet writing has subtleties Phase 1 doesn't cover
- Recovery testing requires deliberately corrupting state and validating the recovery path

---

## 2. Project Layout Additions

Building on the Phase 1 layout, Phase 2 adds:

```
tracer/
  src/
    Tracer.Core/                         (unchanged from Phase 1, plus minor additions)
      Abstractions/
        IAgentTransport.cs               NEW
        ITelemetryUploadService.cs       NEW
      Domain/
        IntervalTimestamp.cs             NEW
        SessionMarker.cs                 EXPANDED (record schema)
        CaptureGap.cs                    NEW
        IntervalManifest.cs              NEW
    Tracer.Storage.DuckDB/
      Tracer.Storage.DuckDB.csproj
      Parquet/                           NEW folder
        FastStateParquetWriter.cs
        ParquetSchemas.cs
        ColumnExtractor.cs
    Tracer.Agent/                        NEW assembly
      Tracer.Agent.csproj
      Program.cs                         entrypoint, builds Host
      AgentHostBuilder.cs                DI composition
      Configuration/
        AgentConfig.cs
        AgentConfigLoader.cs
        ConfigValidation.cs
      Lifecycle/
        AgentHostedService.cs            IHostedService implementation
        IntervalScheduler.cs             computes interval boundaries
        IntervalRotator.cs               executes rotation
        StartupRecoveryService.cs        scans for orphaned intervals
        ShutdownCoordinator.cs           orderly drain on stop
      Ingestion/
        IngestionPipeline.cs             receives records from transport
        RecordRouter.cs                  routes by type to writers
        BackpressureMonitor.cs           detects saturation
        DropPolicy.cs                    decides what to drop
      Storage/
        IntervalDirectory.cs             owns one interval's files
        ManifestWriter.cs
        RetentionManager.cs              applies keepLastN, disk watermark
      Upload/
        UploadIntentDispatcher.cs        hands ready intervals to ITelemetryUploadService
      Diagnostics/
        AgentStateReporter.cs            internal health for tests
    Tracer.Adapters.Mock/
      (additions)
      Transport/
        InProcessChannelTransport.cs     NEW
      Upload/
        LocalFileSystemUploadService.cs  NEW
    Tracer.FakeNode/                     NEW assembly
      Tracer.FakeNode.csproj
      Program.cs
      FakeNodeOrchestrator.cs
      Configuration/
        FakeNodeConfig.cs
    Tracer.TestHarness/
      (additions)
      TracerAgentFixture.cs              NEW
      FakeNodeFixture.cs                 NEW
      ClockControl/
        TestableIntervalScheduler.cs     allows tests to trigger rotation
  tests/
    Tracer.Tests.Unit/
      Agent/
        IntervalSchedulerTests.cs        NEW
        IntervalRotatorTests.cs          NEW
        RecordRouterTests.cs             NEW
        DropPolicyTests.cs               NEW
        ManifestWriterTests.cs           NEW
        StartupRecoveryTests.cs          NEW
      Storage/
        FastStateParquetWriterTests.cs   NEW
    Tracer.Tests.Integration/
      AgentIntervalLifecycleTests.cs     NEW
      AgentRecoveryTests.cs              NEW
      AgentBackpressureTests.cs          NEW
      FakeNodeEndToEndTests.cs           NEW
```

### 2.1 Updated Dependency Graph

```
Tracer.Core                        (unchanged: no project deps)
    ↑
Tracer.Storage.DuckDB              (deps: Tracer.Core, DuckDB.NET.Data, Parquet.Net)
    ↑
Tracer.Adapters.Mock               (deps: Tracer.Core)
    ↑
Tracer.Agent                       (deps: Tracer.Core, Tracer.Storage.DuckDB, M.E.Hosting, M.E.Configuration, Serilog)
    ↑
Tracer.FakeNode                    (deps: Tracer.Core, Tracer.Agent, Tracer.Adapters.Mock, M.E.Hosting)
    ↑
Tracer.TestHarness                 (deps: all of the above)
```

**New NuGet packages** (added to `Directory.Packages.props`):

```xml
<PackageVersion Include="Parquet.Net" Version="4.24.0" />
<PackageVersion Include="Microsoft.Extensions.Hosting" Version="8.0.0" />
<PackageVersion Include="Microsoft.Extensions.Hosting.WindowsServices" Version="8.0.0" />
<PackageVersion Include="Microsoft.Extensions.Configuration" Version="8.0.0" />
<PackageVersion Include="Microsoft.Extensions.Configuration.Json" Version="8.0.0" />
<PackageVersion Include="Microsoft.Extensions.Options" Version="8.0.0" />
<PackageVersion Include="Microsoft.Extensions.Options.DataAnnotations" Version="8.0.0" />
<PackageVersion Include="Serilog" Version="3.1.1" />
<PackageVersion Include="Serilog.Extensions.Hosting" Version="8.0.0" />
<PackageVersion Include="Serilog.Sinks.Console" Version="5.0.1" />
<PackageVersion Include="Serilog.Sinks.File" Version="5.0.0" />
<PackageVersion Include="Serilog.Formatting.Compact" Version="2.0.0" />
```

**Note on Parquet.Net**: this is the mainstream .NET Parquet library, MIT-licensed, actively maintained. It writes correctly-formatted Parquet that DuckDB can natively read via `read_parquet()`. Version 4.x has the cleanest API for streaming row-group writes which is what the agent needs.

---

## 3. New Core Abstractions

These abstractions live in `Tracer.Core/Abstractions/` and represent the new seams between the agent and its environment.

### 3.1 IAgentTransport

The seam between data producer (simulation in production, mock in development) and the agent.

```csharp
namespace Tracer.Core.Abstractions;

/// <summary>
/// Transport carrying records from data producer to TracerAgent.
/// Production: shared memory ring (Phase 11). Development: in-process channel.
/// </summary>
public interface IAgentTransport : IAsyncDisposable
{
    /// <summary>
    /// Read records as they arrive. Completes when the transport is closed.
    /// </summary>
    IAsyncEnumerable<DiagnosticRecord> ReadAsync(CancellationToken ct);

    /// <summary>
    /// Snapshot of current transport health for diagnostics.
    /// </summary>
    TransportHealth GetHealth();
}

public sealed record TransportHealth
{
    public required int PendingCount { get; init; }
    public required int Capacity { get; init; }
    public required long TotalReceived { get; init; }
    public required long TotalDropped { get; init; }
    public required WallclockTime LastReceivedAt { get; init; }
}
```

**Important**: this interface is **read-only from the agent's perspective**. The transport is fed externally — by `Tracer.Adapters.Mock` in development, by a shared-memory writer in production. The agent doesn't know how records arrive; it just reads them.

### 3.2 ITelemetryUploadService

The seam between the agent and the sync system (or its mock).

```csharp
namespace Tracer.Core.Abstractions;

/// <summary>
/// Hands completed intervals to the upload pipeline.
/// Production: HTTP calls to sync master. Development: local filesystem copy.
/// </summary>
public interface ITelemetryUploadService
{
    /// <summary>
    /// Request that the named interval be uploaded.
    /// Returns when the request is queued (not when upload completes).
    /// </summary>
    Task<UploadIntentId> RequestUploadAsync(
        UploadRequest request, CancellationToken ct);

    /// <summary>
    /// Check the status of a previously-requested upload.
    /// </summary>
    Task<UploadStatus> GetStatusAsync(
        UploadIntentId intentId, CancellationToken ct);
}

public sealed record UploadRequest
{
    public required AgentId NodeId { get; init; }
    public required IntervalTimestamp Interval { get; init; }
    public required WallclockTime IntervalStartUtc { get; init; }
    public required WallclockTime IntervalEndUtc { get; init; }
    public required IReadOnlyList<FileToUpload> Files { get; init; }
}

public sealed record FileToUpload
{
    public required string Path { get; init; }  // absolute path
    public required long SizeBytes { get; init; }
    public string? Description { get; init; }   // optional, for logs
}

public readonly record struct UploadIntentId(string Value);

public enum UploadStatus
{
    Unknown,
    Pending,
    InProgress,
    Complete,
    Failed
}
```

The agent fires upload requests at interval rotation and **does not wait** for completion. The upload service handles its own queue, retry, and persistence. The agent's interval files remain on disk (per retention policy) until either the upload service confirms success and retention evicts them, or the operator manually clears them.

### 3.3 IntervalTimestamp and IntervalManifest

```csharp
namespace Tracer.Core.Domain;

/// <summary>
/// Wall-clock-aligned interval identifier in ISO 8601 basic format: YYYYMMDDTHHMMSSZ.
/// Always UTC. Always wall-clock-aligned (no fractional seconds).
/// </summary>
public readonly record struct IntervalTimestamp
{
    public string Value { get; }

    public IntervalTimestamp(string value)
    {
        if (!IsValid(value))
            throw new ArgumentException(
                $"Invalid interval timestamp: '{value}'. Expected YYYYMMDDTHHMMSSZ.",
                nameof(value));
        Value = value;
    }

    public static IntervalTimestamp FromUtc(DateTimeOffset utc)
    {
        if (utc.Offset != TimeSpan.Zero)
            throw new ArgumentException("IntervalTimestamp must be UTC", nameof(utc));
        return new IntervalTimestamp(utc.ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture));
    }

    public DateTimeOffset ToDateTimeOffset()
    {
        return DateTimeOffset.ParseExact(
            Value, "yyyyMMddTHHmmssZ", 
            CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
    }

    private static bool IsValid(string value)
    {
        if (value is null || value.Length != 16) return false;
        return DateTimeOffset.TryParseExact(
            value, "yyyyMMddTHHmmssZ", 
            CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out _);
    }

    public override string ToString() => Value;
}
```

```csharp
namespace Tracer.Core.Domain;

public sealed record IntervalManifest
{
    public required IntervalTimestamp IntervalStart { get; init; }
    public required IntervalTimestamp IntervalEnd { get; init; }
    public required AgentId NodeId { get; init; }
    public required string TracerVersion { get; init; }
    public required int SchemaVersion { get; init; }
    public required long EventCount { get; init; }
    public required long SlowStateCount { get; init; }
    public required IReadOnlyList<string> FastStateTopics { get; init; }
    public required IReadOnlyList<CaptureGap> CaptureGaps { get; init; }
    public required IReadOnlyList<SessionMarker> SessionMarkers { get; init; }
    public required WallclockTime FinalizedAt { get; init; }
    public required ManifestFinalizationReason FinalizationReason { get; init; }
}

public enum ManifestFinalizationReason
{
    ScheduledRotation,
    GracefulShutdown,
    RecoveryAfterCrash
}

public sealed record CaptureGap
{
    public required WallclockTime StartUtc { get; init; }
    public required WallclockTime EndUtc { get; init; }
    public required CaptureGapReason Reason { get; init; }
    public required long DroppedRecordCount { get; init; }
    public string? Detail { get; init; }
}

public enum CaptureGapReason
{
    BackpressureFastStateDropped,
    BackpressureSlowStateDropped,
    BackpressureEventsDropped,
    UnrecoveredCrashGap,
    TransportDisconnected
}

public sealed record SessionMarker
{
    public required string SessionId { get; init; }
    public required SessionMarkerType Type { get; init; }
    public required WallclockTime Wallclock { get; init; }
    public string? Label { get; init; }
}

public enum SessionMarkerType { Start, End }
```

### 3.4 Phase 2 Additions to `IDiagnosticStorageWriter`

The Phase 1 writer interface gains an additional method for fast-state samples:

```csharp
public interface IDiagnosticStorageWriter : IAsyncDisposable
{
    Task AppendEventAsync(EventRecord record, CancellationToken ct);
    Task AppendStateAsync(StateSampleRecord record, CancellationToken ct);   // for Slow
    Task AppendFastStateAsync(StateSampleRecord record, CancellationToken ct); // NEW for Fast
    Task AppendBatchAsync(IReadOnlyList<DiagnosticRecord> records, CancellationToken ct);
    Task FlushAsync(CancellationToken ct);
}
```

Phase 1's writer threw `NotSupportedException` on fast state. Phase 2 implements it via Parquet.

---

## 4. The Storage Side: Parquet for Fast State

### 4.1 Why Parquet, and How It Plugs In

Architecture §5.3 specifies fast state stored as Parquet files per topic per interval. Reasoning recap:

- Fast state is voluminous (potentially 100K samples/sec cluster-wide)
- Fast state is queried rarely (only on entity-history drill-down)
- Storing it in DuckDB tables alongside events would pollute hot query paths
- Parquet's columnar compression is excellent for numeric time-series (5-10x vs JSON-in-DuckDB-row)
- DuckDB queries Parquet natively via `read_parquet()` so no separate query engine needed

### 4.2 Topic-Specific Schemas

Each fast-state topic has a known schema declared at agent startup. The agent extracts payload fields to typed Parquet columns at ingest.

**Phase 2 supports a fixed registry of topic schemas.** Phase 7 (entity history) may generalize this to dynamic discovery, but Phase 2 keeps it static and explicit.

```csharp
namespace Tracer.Storage.DuckDB.Parquet;

public sealed record ParquetTopicSchema
{
    public required string TopicName { get; init; }
    public required IReadOnlyList<ParquetColumn> Columns { get; init; }
}

public sealed record ParquetColumn
{
    public required string Name { get; init; }
    public required ParquetType Type { get; init; }
    public bool Nullable { get; init; } = false;
    /// <summary>JSON path within payload to extract this column from (e.g., "$.position.x")</summary>
    public required string JsonPath { get; init; }
}

public enum ParquetType
{
    Int32, Int64, UInt64,
    Float, Double,
    Bool,
    String,
    TimestampNs
}
```

Standard columns present in **every** fast-state Parquet (added automatically, not from the schema):

- `publish_wallclock` (TimestampNs, not nullable)
- `receive_wallclock` (TimestampNs, not nullable)
- `publisher_node` (String, not nullable)
- `instance_key` (String, not nullable)
- `sequence_number` (UInt64, not nullable)

The schema declared in `ParquetTopicSchema.Columns` is the **payload-specific** columns, on top of these standards.

**Example registry for the Phase 2 FakeNode**:

```csharp
public static class WellKnownTopicSchemas
{
    public static readonly ParquetTopicSchema Transforms = new()
    {
        TopicName = "topic.transforms",
        Columns = new[]
        {
            new ParquetColumn { Name = "pos_x", Type = ParquetType.Float, JsonPath = "$.position.x" },
            new ParquetColumn { Name = "pos_y", Type = ParquetType.Float, JsonPath = "$.position.y" },
            new ParquetColumn { Name = "pos_z", Type = ParquetType.Float, JsonPath = "$.position.z" },
            new ParquetColumn { Name = "quat_w", Type = ParquetType.Float, JsonPath = "$.orientation.w" },
            new ParquetColumn { Name = "quat_x", Type = ParquetType.Float, JsonPath = "$.orientation.x" },
            new ParquetColumn { Name = "quat_y", Type = ParquetType.Float, JsonPath = "$.orientation.y" },
            new ParquetColumn { Name = "quat_z", Type = ParquetType.Float, JsonPath = "$.orientation.z" },
        }
    };
}
```

### 4.3 FastStateParquetWriter

```csharp
namespace Tracer.Storage.DuckDB.Parquet;

public sealed class FastStateParquetWriter : IAsyncDisposable
{
    private readonly string _outputPath;
    private readonly ParquetTopicSchema _schema;
    private readonly ILogger<FastStateParquetWriter> _logger;
    
    private ParquetWriter? _writer;
    private readonly List<ParquetRow> _rowBuffer;
    private const int RowGroupFlushThreshold = 10_000;   // rows per group
    private long _totalRowsWritten;
    private bool _disposed;
    private readonly object _lock = new();

    public static async Task<FastStateParquetWriter> CreateAsync(
        string outputPath,
        ParquetTopicSchema schema,
        ILogger<FastStateParquetWriter> logger,
        CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        var writer = new FastStateParquetWriter(outputPath, schema, logger);
        await writer.InitializeAsync(ct);
        return writer;
    }

    private FastStateParquetWriter(string outputPath, ParquetTopicSchema schema, ILogger<FastStateParquetWriter> logger)
    {
        _outputPath = outputPath;
        _schema = schema;
        _logger = logger;
        _rowBuffer = new List<ParquetRow>(RowGroupFlushThreshold);
    }

    private async Task InitializeAsync(CancellationToken ct)
    {
        var parquetSchema = ParquetSchemaBuilder.BuildSchema(_schema);
        var stream = File.Create(_outputPath);
        _writer = await ParquetWriter.CreateAsync(parquetSchema, stream, cancellationToken: ct);
        // Parquet.Net specifics on creating writer; pseudocode shown
    }

    public Task AppendAsync(StateSampleRecord record, CancellationToken ct)
    {
        ThrowIfDisposed();
        lock (_lock)
        {
            var row = ColumnExtractor.ExtractRow(record, _schema);
            _rowBuffer.Add(row);
            if (_rowBuffer.Count >= RowGroupFlushThreshold)
                FlushRowGroup();
        }
        return Task.CompletedTask;
    }

    public long TotalRowsWritten 
    { 
        get { lock (_lock) { return _totalRowsWritten; } }
    }

    private void FlushRowGroup()
    {
        // Pseudocode — Parquet.Net specifics vary
        // 1. Group rows into columns
        // 2. Write each column as a Parquet column chunk
        // 3. Close the row group
        _totalRowsWritten += _rowBuffer.Count;
        _rowBuffer.Clear();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        lock (_lock)
        {
            if (_rowBuffer.Count > 0)
                FlushRowGroup();
        }
        if (_writer is not null)
            await _writer.DisposeAsync();
    }

    private void ThrowIfDisposed() { /* ... */ }
}
```

**Implementation notes for the implementer:**

- **Parquet.Net's actual API will differ**. The pseudocode above expresses intent. Specifically, `ParquetWriter.CreateAsync`, `CreateRowGroup`, column writing, and disposal patterns need to be verified against current Parquet.Net docs.
- **Row groups vs rows**: Parquet's natural write unit is a "row group" (a batch of rows that get columnarized together). Writing one row at a time isn't efficient. The flush threshold of 10K rows balances memory use vs row group efficiency.
- **No partial row groups on crash**: a row group is either fully written or not present in the file. Rows in `_rowBuffer` not yet flushed are lost on crash. Acceptable — recovery records this in `captureGaps`.
- **One writer per topic per interval**: writer is created lazily on first sample, finalized at interval close.

### 4.4 Updated DuckDbStorageWriter

The Phase 1 writer is extended:

```csharp
namespace Tracer.Storage.DuckDB;

public sealed class DuckDbStorageWriter : IDiagnosticStorageWriter
{
    // ... Phase 1 fields ...
    
    private readonly Dictionary<string, FastStateParquetWriter> _fastStateWriters = new();
    private readonly string _fastStateDirectory;
    private readonly IReadOnlyDictionary<string, ParquetTopicSchema> _fastStateSchemas;
    
    public static async Task<DuckDbStorageWriter> CreateAsync(
        string intervalDirectory,
        IReadOnlyDictionary<string, ParquetTopicSchema> fastStateSchemas,
        ILogger<DuckDbStorageWriter> logger,
        CancellationToken ct)
    {
        // Phase 1 setup for events.duckdb and slow_state.duckdb
        // Plus: ensure fastStateDirectory exists
        // Schemas are passed in, not stored as files
    }
    
    public async Task AppendFastStateAsync(StateSampleRecord record, CancellationToken ct)
    {
        ThrowIfDisposed();
        if (record.Rate != StateSampleRate.Fast)
            throw new ArgumentException("Expected fast state sample", nameof(record));
        
        var writer = await GetOrCreateFastStateWriterAsync(record.Topic.Value, ct);
        await writer.AppendAsync(record, ct);
    }
    
    private async Task<FastStateParquetWriter> GetOrCreateFastStateWriterAsync(
        string topic, CancellationToken ct)
    {
        if (_fastStateWriters.TryGetValue(topic, out var existing))
            return existing;
        
        if (!_fastStateSchemas.TryGetValue(topic, out var schema))
        {
            // No registered schema for this topic — skip with warning
            _logger.LogWarning(
                "Fast state sample for unregistered topic '{Topic}' will be dropped",
                topic);
            return _nullWriter ??= NullFastStateWriter.Instance;
        }
        
        var path = Path.Combine(_fastStateDirectory, $"{SafeFileName(topic)}.parquet");
        var writer = await FastStateParquetWriter.CreateAsync(path, schema, _logger, ct);
        _fastStateWriters[topic] = writer;
        return writer;
    }
    
    public override async ValueTask DisposeAsync()
    {
        // ... close DuckDB appenders ...
        foreach (var fsw in _fastStateWriters.Values)
            await fsw.DisposeAsync();
        // ... base disposal ...
    }
}
```

A `NullFastStateWriter` silently drops samples for unregistered topics. Logged once per topic; subsequent drops on the same topic are silent. Phase 7 may add dynamic schema discovery.

---

## 5. The TracerAgent: Architecture

The agent is a single-process application built on `Microsoft.Extensions.Hosting`. Its responsibilities, in execution order:

1. **Bootstrap**: load config, set up logging, validate paths
2. **Startup recovery**: scan `intervals/` for orphans, finalize them
3. **Open current interval**: create writer for the wall-clock-aligned current interval
4. **Start ingestion**: pull records from `IAgentTransport`, route to writers
5. **Schedule rotation**: wake at next interval boundary, rotate, repeat
6. **Handle backpressure**: drop fast-state when saturated, escalate as needed
7. **Apply retention**: evict old intervals when watermark approached
8. **Shutdown gracefully**: drain, finalize current interval, exit

### 5.1 Process Lifecycle

```
┌──────────────────────────────────────────────────────────────┐
│  tracer-agent.exe (console mode or Windows service)          │
│                                                              │
│  ┌──────────────────────────────────────────────────────┐    │
│  │  Generic Host (IHost)                                │    │
│  │                                                      │    │
│  │  Singleton services:                                 │    │
│  │   - IClock (SystemClock prod, SimulatedClock test)   │    │
│  │   - AgentConfig (from agent.json)                    │    │
│  │   - IAgentTransport (per config)                     │    │
│  │   - ITelemetryUploadService (per config)             │    │
│  │   - RetentionManager                                 │    │
│  │   - BackpressureMonitor                              │    │
│  │   - IntervalScheduler                                │    │
│  │                                                      │    │
│  │  IHostedService:                                     │    │
│  │   - AgentHostedService                               │    │
│  │     ├─ StartupRecoveryService.RecoverAsync()         │    │
│  │     ├─ IntervalRotator.OpenCurrentAsync()            │    │
│  │     ├─ IngestionPipeline.RunAsync() ────loops────┐   │    │
│  │     ├─ RotationLoop ───────────────────loops────┤   │    │
│  │     └─ RetentionLoop ──────────────────loops────┘   │    │
│  │                                                      │    │
│  │  IHostApplicationLifetime:                           │    │
│  │   - graceful shutdown on Ctrl-C, SIGTERM, service-   │    │
│  │     stop, or POST /api/test/shutdown (TESTING_ENABLED)│   │
│  └──────────────────────────────────────────────────────┘    │
└──────────────────────────────────────────────────────────────┘
```

### 5.2 Program.cs

```csharp
namespace Tracer.Agent;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            var host = AgentHostBuilder.Build(args);

            // LOG_FILE convention — emit before any logging starts
            var config = host.Services.GetRequiredService<AgentConfig>();
            var logFilePath = LoggingPaths.GetCurrentLogFilePath(config.LogsRoot);
            Console.WriteLine($"LOG_FILE={logFilePath}");

            await host.RunAsync();
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"FATAL: {ex}");
            return 1;
        }
    }
}
```

### 5.3 AgentHostBuilder

```csharp
namespace Tracer.Agent;

public static class AgentHostBuilder
{
    public static IHost Build(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        // Configuration
        var configPath = ResolveConfigPath(args);
        builder.Configuration.AddJsonFile(configPath, optional: false, reloadOnChange: false);

        builder.Services.Configure<AgentConfig>(builder.Configuration.GetSection("Agent"));
        builder.Services.AddSingleton<AgentConfig>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<AgentConfig>>().Value;
            ConfigValidation.Validate(options);
            return options;
        });

        // Logging
        builder.Services.AddSerilog((sp, lc) =>
        {
            var cfg = sp.GetRequiredService<AgentConfig>();
            lc.MinimumLevel.Information()
              .MinimumLevel.Override("Tracer", LogEventLevel.Debug)
              .Enrich.FromLogContext()
              .Enrich.WithProperty("Service", "TracerAgent")
              .Enrich.WithProperty("NodeId", cfg.NodeId)
              .WriteTo.File(
                  new CompactJsonFormatter(),
                  Path.Combine(cfg.LogsRoot, "tracer-agent-.json"),
                  rollingInterval: RollingInterval.Day,
                  retainedFileCountLimit: 14);
            if (cfg.LogToConsole)
                lc.WriteTo.Console(new CompactJsonFormatter());
        });

        // Time
        builder.Services.AddSingleton<IClock, SystemClock>();

        // Transport — selected by config
        builder.Services.AddSingleton<IAgentTransport>(sp =>
            TransportFactory.Create(sp.GetRequiredService<AgentConfig>(), sp));

        // Upload service — selected by config
        builder.Services.AddSingleton<ITelemetryUploadService>(sp =>
            UploadServiceFactory.Create(sp.GetRequiredService<AgentConfig>(), sp));

        // Storage — fast state schemas registered statically
        builder.Services.AddSingleton<IReadOnlyDictionary<string, ParquetTopicSchema>>(
            WellKnownTopicSchemas.ToDictionary());

        // Agent components
        builder.Services.AddSingleton<IntervalScheduler>();
        builder.Services.AddSingleton<IntervalRotator>();
        builder.Services.AddSingleton<StartupRecoveryService>();
        builder.Services.AddSingleton<IngestionPipeline>();
        builder.Services.AddSingleton<RecordRouter>();
        builder.Services.AddSingleton<BackpressureMonitor>();
        builder.Services.AddSingleton<DropPolicy>();
        builder.Services.AddSingleton<RetentionManager>();
        builder.Services.AddSingleton<ManifestWriter>();
        builder.Services.AddSingleton<UploadIntentDispatcher>();
        builder.Services.AddSingleton<AgentStateReporter>();

        // The hosted service that ties it all together
        builder.Services.AddHostedService<AgentHostedService>();

        // Windows service support
        builder.Services.AddWindowsService(options =>
        {
            options.ServiceName = "TracerAgent";
        });

        return builder.Build();
    }

    private static string ResolveConfigPath(string[] args)
    {
        // Convention: --config <absolute-path>
        // Fallback: %PROGRAMDATA%\Tracer\agent\config.json
        // Hard rule: absolute path only
        var idx = Array.IndexOf(args, "--config");
        if (idx >= 0 && idx + 1 < args.Length)
        {
            var path = args[idx + 1];
            if (!Path.IsPathFullyQualified(path))
                throw new ArgumentException($"--config must be absolute: '{path}'");
            return path;
        }
        var defaultPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Tracer", "agent", "config.json");
        return defaultPath;
    }
}
```

### 5.4 AgentConfig

```csharp
namespace Tracer.Agent.Configuration;

public sealed class AgentConfig
{
    [Required]
    public required string NodeId { get; set; }

    [Required]
    public required string DataRoot { get; set; }     // absolute path

    [Required]
    public required string LogsRoot { get; set; }     // absolute path

    public TimeSpan IntervalDuration { get; set; } = TimeSpan.FromHours(1);

    public int KeepLastNIntervals { get; set; } = 24;

    public int DiskWatermarkPercent { get; set; } = 10;

    public bool LogToConsole { get; set; } = false;

    public TransportConfig Transport { get; set; } = new();

    public UploadServiceConfig UploadService { get; set; } = new();

    public BackpressureConfig Backpressure { get; set; } = new();
}

public sealed class TransportConfig
{
    /// <summary>Either "InProcessChannel" or "SharedMemory" (Phase 11)</summary>
    public string Kind { get; set; } = "InProcessChannel";

    public int CapacityRecords { get; set; } = 100_000;

    public string? SharedMemoryName { get; set; }   // Phase 11 only
}

public sealed class UploadServiceConfig
{
    /// <summary>Either "LocalFileSystem" or "SyncSystem" (Phase 11)</summary>
    public string Kind { get; set; } = "LocalFileSystem";

    public string? LocalFileSystemRoot { get; set; }   // absolute path for mock

    public string? SyncMasterUrl { get; set; }         // Phase 11 only
}

public sealed class BackpressureConfig
{
    public int InflightThresholdRecords { get; set; } = 50_000;
    public int FastStateDropThresholdRecords { get; set; } = 70_000;
    public int SlowStateDropThresholdRecords { get; set; } = 90_000;
    public int EventsDropThresholdRecords { get; set; } = 98_000;
}
```

### 5.5 Example agent.json

```json
{
  "Agent": {
    "NodeId": "blue-cmd-01",
    "DataRoot": "C:/ProgramData/Tracer/agent",
    "LogsRoot": "C:/ProgramData/Tracer/agent/logs",
    "IntervalDuration": "01:00:00",
    "KeepLastNIntervals": 24,
    "DiskWatermarkPercent": 10,
    "LogToConsole": false,
    "Transport": {
      "Kind": "InProcessChannel",
      "CapacityRecords": 100000
    },
    "UploadService": {
      "Kind": "LocalFileSystem",
      "LocalFileSystemRoot": "C:/ProgramData/Tracer/mock-nas/telemetry"
    },
    "Backpressure": {
      "InflightThresholdRecords": 50000,
      "FastStateDropThresholdRecords": 70000,
      "SlowStateDropThresholdRecords": 90000,
      "EventsDropThresholdRecords": 98000
    }
  }
}
```

---

## 6. Interval Lifecycle: The Heart of Phase 2

This section describes the rotation protocol in detail. It is the most operationally important code in Phase 2.

### 6.1 Interval Boundaries

Interval boundaries are aligned to wall-clock based on the configured `IntervalDuration`. For 1-hour intervals: 14:00:00Z, 15:00:00Z, 16:00:00Z, etc. For 30-minute intervals: 14:00:00Z, 14:30:00Z, 15:00:00Z, etc.

`IntervalScheduler` computes the boundaries:

```csharp
namespace Tracer.Agent.Lifecycle;

public sealed class IntervalScheduler
{
    private readonly TimeSpan _duration;
    private readonly IClock _clock;

    public IntervalScheduler(AgentConfig config, IClock clock)
    {
        _duration = config.IntervalDuration;
        _clock = clock;
        if (_duration < TimeSpan.FromMinutes(1) || _duration > TimeSpan.FromHours(24))
            throw new ArgumentOutOfRangeException("IntervalDuration must be between 1 minute and 24 hours");
        if (TimeSpan.FromDays(1).Ticks % _duration.Ticks != 0)
            throw new ArgumentException("IntervalDuration must divide a day evenly");
    }

    public IntervalTimestamp CurrentIntervalStart()
    {
        var nowDt = _clock.Now.ToDateTimeOffset();
        return AlignDown(nowDt);
    }

    public WallclockTime NextIntervalBoundary()
    {
        var current = CurrentIntervalStart();
        var next = current.ToDateTimeOffset() + _duration;
        return WallclockTime.FromDateTimeOffset(next);
    }

    public TimeSpan TimeUntilNextBoundary()
    {
        var next = NextIntervalBoundary();
        return next - _clock.Now;
    }

    private IntervalTimestamp AlignDown(DateTimeOffset dt)
    {
        long durationTicks = _duration.Ticks;
        long alignedTicks = (dt.UtcTicks / durationTicks) * durationTicks;
        var alignedUtc = new DateTimeOffset(alignedTicks, TimeSpan.Zero);
        return IntervalTimestamp.FromUtc(alignedUtc);
    }
}
```

**Why "divides a day evenly"**: prevents misalignment across day boundaries. `IntervalDuration` values like 11 minutes would create intervals that drift relative to wall-clock days, which makes operator queries ("show me intervals from 14:00 to 15:00 today") behave oddly.

### 6.2 The IntervalDirectory

A single interval lives in one folder. The `IntervalDirectory` class owns it:

```csharp
namespace Tracer.Agent.Storage;

public sealed class IntervalDirectory
{
    public IntervalTimestamp Timestamp { get; }
    public string RootPath { get; }
    public string EventsDbPath => Path.Combine(RootPath, "events.duckdb");
    public string SlowStateDbPath => Path.Combine(RootPath, "slow_state.duckdb");
    public string FastStateDirectory => Path.Combine(RootPath, "fast_state");
    public string ManifestPath => Path.Combine(RootPath, "manifest.json");
    public string ReadySentinelPath => Path.Combine(RootPath, "_ready");

    public IntervalDirectory(string dataRoot, IntervalTimestamp timestamp)
    {
        Timestamp = timestamp;
        RootPath = Path.Combine(dataRoot, "intervals", timestamp.Value);
    }

    public bool Exists => Directory.Exists(RootPath);
    public bool IsReady => File.Exists(ReadySentinelPath);
    public bool HasManifest => File.Exists(ManifestPath);

    public void CreateIfMissing()
    {
        Directory.CreateDirectory(FastStateDirectory);
    }

    public void WriteReadySentinel()
    {
        File.WriteAllBytes(ReadySentinelPath, Array.Empty<byte>());
    }

    public IReadOnlyList<FileToUpload> EnumerateFiles()
    {
        var result = new List<FileToUpload>();
        result.Add(new FileToUpload { Path = EventsDbPath, SizeBytes = new FileInfo(EventsDbPath).Length, Description = "events.duckdb" });
        result.Add(new FileToUpload { Path = SlowStateDbPath, SizeBytes = new FileInfo(SlowStateDbPath).Length, Description = "slow_state.duckdb" });
        foreach (var f in Directory.EnumerateFiles(FastStateDirectory, "*.parquet"))
            result.Add(new FileToUpload { Path = f, SizeBytes = new FileInfo(f).Length, Description = Path.GetFileName(f) });
        result.Add(new FileToUpload { Path = ManifestPath, SizeBytes = new FileInfo(ManifestPath).Length, Description = "manifest.json" });
        result.Add(new FileToUpload { Path = ReadySentinelPath, SizeBytes = 0, Description = "_ready" });
        return result;
    }

    public long ComputeTotalBytes()
    {
        if (!Directory.Exists(RootPath)) return 0;
        long total = 0;
        foreach (var f in Directory.EnumerateFiles(RootPath, "*", SearchOption.AllDirectories))
            total += new FileInfo(f).Length;
        return total;
    }
}
```

### 6.3 IntervalRotator

The rotator opens, finalizes, and replaces the current interval's writer. It is the only component that mutates the "current interval" state.

```csharp
namespace Tracer.Agent.Lifecycle;

public sealed class IntervalRotator : IAsyncDisposable
{
    private readonly AgentConfig _config;
    private readonly IClock _clock;
    private readonly IntervalScheduler _scheduler;
    private readonly ManifestWriter _manifestWriter;
    private readonly UploadIntentDispatcher _uploadDispatcher;
    private readonly IReadOnlyDictionary<string, ParquetTopicSchema> _fastStateSchemas;
    private readonly ILogger<IntervalRotator> _logger;
    private readonly SemaphoreSlim _rotationLock = new(1, 1);

    private IntervalDirectory? _currentDirectory;
    private DuckDbStorageWriter? _currentWriter;
    private readonly Channel<DiagnosticRecord> _bridgeBuffer;  // bridge during rotation
    private readonly List<SessionMarker> _sessionMarkersInCurrent = new();
    private readonly List<CaptureGap> _gapsInCurrent = new();
    private long _eventCountInCurrent;
    private long _slowStateCountInCurrent;
    private readonly HashSet<string> _fastStateTopicsInCurrent = new();

    public IntervalRotator(
        AgentConfig config, IClock clock, IntervalScheduler scheduler,
        ManifestWriter manifestWriter, UploadIntentDispatcher uploadDispatcher,
        IReadOnlyDictionary<string, ParquetTopicSchema> fastStateSchemas,
        ILogger<IntervalRotator> logger)
    {
        _config = config;
        _clock = clock;
        _scheduler = scheduler;
        _manifestWriter = manifestWriter;
        _uploadDispatcher = uploadDispatcher;
        _fastStateSchemas = fastStateSchemas;
        _logger = logger;
        _bridgeBuffer = Channel.CreateUnbounded<DiagnosticRecord>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
    }

    public IntervalDirectory? CurrentDirectory => _currentDirectory;
    public DuckDbStorageWriter? CurrentWriter => _currentWriter;

    public async Task OpenCurrentAsync(CancellationToken ct)
    {
        await _rotationLock.WaitAsync(ct);
        try
        {
            if (_currentDirectory is not null)
                throw new InvalidOperationException("Current interval already open");

            var ts = _scheduler.CurrentIntervalStart();
            await OpenInternalAsync(ts, ct);
        }
        finally { _rotationLock.Release(); }
    }

    /// <summary>
    /// Closes the current interval, finalizes it, opens the next one.
    /// Returns immediately if rotation is already in progress (caller scheduled too eagerly).
    /// </summary>
    public async Task RotateAsync(ManifestFinalizationReason reason, CancellationToken ct)
    {
        if (!await _rotationLock.WaitAsync(TimeSpan.Zero, ct))
        {
            _logger.LogDebug("Rotation already in progress; skipping");
            return;
        }
        try
        {
            if (_currentDirectory is null)
            {
                _logger.LogWarning("RotateAsync called with no current interval; opening fresh");
                await OpenInternalAsync(_scheduler.CurrentIntervalStart(), ct);
                return;
            }

            var closing = _currentDirectory;
            var closingWriter = _currentWriter!;

            _logger.LogInformation(
                "Beginning rotation: closing interval {Interval}, reason {Reason}",
                closing.Timestamp.Value, reason);

            // Take snapshot of stats before closing writers
            var snapshot = SnapshotCurrentStats();

            // The IngestionPipeline buffers records during rotation; see §6.4.
            // We don't need to coordinate further here — pipeline switches its
            // write target after we install the new writer.

            // Close current writer (this is the time-consuming part)
            await closingWriter.FlushAsync(ct);
            await closingWriter.DisposeAsync();

            // Finalize the closing interval: write manifest, then _ready
            var manifest = BuildManifest(closing.Timestamp, snapshot, reason);
            await _manifestWriter.WriteAsync(closing.ManifestPath, manifest, ct);
            closing.WriteReadySentinel();

            _logger.LogInformation(
                "Closed interval {Interval}: events={EventCount} slowState={SlowStateCount} fastStateTopics={FastTopics} gaps={Gaps}",
                closing.Timestamp.Value, snapshot.EventCount, snapshot.SlowStateCount, 
                snapshot.FastStateTopics.Count, snapshot.CaptureGaps.Count);

            // Hand to upload service (fire-and-forget; service handles its own queue)
            await _uploadDispatcher.DispatchAsync(closing, manifest, ct);

            // Open the next interval
            var nextTs = _scheduler.CurrentIntervalStart();
            // (after closing & finalize, real clock may have advanced into the next interval)
            await OpenInternalAsync(nextTs, ct);
        }
        finally { _rotationLock.Release(); }
    }

    private async Task OpenInternalAsync(IntervalTimestamp ts, CancellationToken ct)
    {
        var directory = new IntervalDirectory(_config.DataRoot, ts);
        directory.CreateIfMissing();

        var writer = await DuckDbStorageWriter.CreateAsync(
            directory.RootPath, _fastStateSchemas, _logger, ct);

        _currentDirectory = directory;
        _currentWriter = writer;
        _sessionMarkersInCurrent.Clear();
        _gapsInCurrent.Clear();
        _eventCountInCurrent = 0;
        _slowStateCountInCurrent = 0;
        _fastStateTopicsInCurrent.Clear();

        _logger.LogInformation("Opened interval {Interval}", ts.Value);
    }

    /// <summary>
    /// Called by IngestionPipeline when a record is written through the current writer.
    /// Maintains stats and detects session markers.
    /// </summary>
    public void NotifyRecordWritten(DiagnosticRecord record)
    {
        switch (record)
        {
            case EventRecord ev:
                _eventCountInCurrent++;
                MaybeRecordSessionMarker(ev);
                break;
            case StateSampleRecord ss when ss.Rate == StateSampleRate.Slow:
                _slowStateCountInCurrent++;
                break;
            case StateSampleRecord ss when ss.Rate == StateSampleRate.Fast:
                _fastStateTopicsInCurrent.Add(ss.Topic.Value);
                break;
        }
    }

    public void NotifyCaptureGap(CaptureGap gap)
    {
        _gapsInCurrent.Add(gap);
    }

    private void MaybeRecordSessionMarker(EventRecord ev)
    {
        if (ev.Topic.Value == "system.session_start")
        {
            var sid = ExtractSessionId(ev);
            if (sid is not null)
                _sessionMarkersInCurrent.Add(new SessionMarker
                {
                    SessionId = sid,
                    Type = SessionMarkerType.Start,
                    Wallclock = ev.PublishWallclock,
                    Label = ev.NotableLabel
                });
        }
        else if (ev.Topic.Value == "system.session_end")
        {
            // analogous
        }
    }

    private static string? ExtractSessionId(EventRecord ev)
    {
        // parse payload JSON for sessionId field
        try
        {
            using var doc = JsonDocument.Parse(ev.PayloadJson);
            if (doc.RootElement.TryGetProperty("sessionId", out var sid))
                return sid.GetString();
        }
        catch { /* malformed payload — skip */ }
        return null;
    }

    private CurrentStatsSnapshot SnapshotCurrentStats() => new()
    {
        EventCount = _eventCountInCurrent,
        SlowStateCount = _slowStateCountInCurrent,
        FastStateTopics = _fastStateTopicsInCurrent.ToList(),
        SessionMarkers = _sessionMarkersInCurrent.ToList(),
        CaptureGaps = _gapsInCurrent.ToList()
    };

    private IntervalManifest BuildManifest(
        IntervalTimestamp ts, CurrentStatsSnapshot snap, ManifestFinalizationReason reason)
    {
        var nextTs = IntervalTimestamp.FromUtc(
            ts.ToDateTimeOffset() + _config.IntervalDuration);
        return new IntervalManifest
        {
            IntervalStart = ts,
            IntervalEnd = nextTs,
            NodeId = new AgentId(_config.NodeId),
            TracerVersion = TracerVersion.Current,
            SchemaVersion = SchemaV1.Version,
            EventCount = snap.EventCount,
            SlowStateCount = snap.SlowStateCount,
            FastStateTopics = snap.FastStateTopics,
            CaptureGaps = snap.CaptureGaps,
            SessionMarkers = snap.SessionMarkers,
            FinalizedAt = _clock.Now,
            FinalizationReason = reason
        };
    }

    public async ValueTask DisposeAsync()
    {
        if (_currentWriter is not null)
        {
            // Final rotation on disposal: graceful shutdown reason
            await RotateAsync(ManifestFinalizationReason.GracefulShutdown, CancellationToken.None);
        }
        _rotationLock.Dispose();
    }

    private sealed record CurrentStatsSnapshot
    {
        public required long EventCount { get; init; }
        public required long SlowStateCount { get; init; }
        public required IReadOnlyList<string> FastStateTopics { get; init; }
        public required IReadOnlyList<SessionMarker> SessionMarkers { get; init; }
        public required IReadOnlyList<CaptureGap> CaptureGaps { get; init; }
    }
}
```

### 6.4 The IngestionPipeline

The pipeline reads from `IAgentTransport` and routes records to the current writer. It's the workhorse of the agent.

```csharp
namespace Tracer.Agent.Ingestion;

public sealed class IngestionPipeline
{
    private readonly IAgentTransport _transport;
    private readonly IntervalRotator _rotator;
    private readonly BackpressureMonitor _backpressure;
    private readonly DropPolicy _dropPolicy;
    private readonly ILogger<IngestionPipeline> _logger;

    public IngestionPipeline(
        IAgentTransport transport, IntervalRotator rotator,
        BackpressureMonitor backpressure, DropPolicy dropPolicy,
        ILogger<IngestionPipeline> logger)
    {
        _transport = transport;
        _rotator = rotator;
        _backpressure = backpressure;
        _dropPolicy = dropPolicy;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        _logger.LogInformation("Ingestion pipeline starting");
        try
        {
            await foreach (var record in _transport.ReadAsync(ct).WithCancellation(ct))
            {
                await ProcessOneAsync(record, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogInformation("Ingestion pipeline stopping (cancelled)");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ingestion pipeline failed");
            throw;
        }
    }

    private async Task ProcessOneAsync(DiagnosticRecord record, CancellationToken ct)
    {
        var writer = _rotator.CurrentWriter;
        if (writer is null)
        {
            _logger.LogWarning("No current writer; dropping record");
            _rotator.NotifyCaptureGap(new CaptureGap
            {
                StartUtc = record.PublishWallclock,
                EndUtc = record.PublishWallclock,
                Reason = CaptureGapReason.TransportDisconnected,
                DroppedRecordCount = 1
            });
            return;
        }

        var level = _backpressure.CurrentLevel();
        if (_dropPolicy.ShouldDrop(record, level, out var reason))
        {
            _rotator.NotifyCaptureGap(new CaptureGap
            {
                StartUtc = record.PublishWallclock,
                EndUtc = record.PublishWallclock,
                Reason = reason,
                DroppedRecordCount = 1
            });
            return;
        }

        try
        {
            switch (record)
            {
                case EventRecord ev:
                    await writer.AppendEventAsync(ev, ct);
                    break;
                case StateSampleRecord ss when ss.Rate == StateSampleRate.Slow:
                    await writer.AppendStateAsync(ss, ct);
                    break;
                case StateSampleRecord ss when ss.Rate == StateSampleRate.Fast:
                    await writer.AppendFastStateAsync(ss, ct);
                    break;
            }
            _rotator.NotifyRecordWritten(record);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to write record from {Publisher} on topic {Topic}",
                record.PublisherNode, record.Topic);
            // Don't propagate — we want the pipeline to keep running
            _rotator.NotifyCaptureGap(new CaptureGap
            {
                StartUtc = record.PublishWallclock,
                EndUtc = record.PublishWallclock,
                Reason = CaptureGapReason.TransportDisconnected,  // misnomer; "internal error"
                DroppedRecordCount = 1,
                Detail = ex.Message
            });
        }
    }
}
```

**Key design choices**:

- **One reader, one writer**: the pipeline reads from the transport sequentially and writes to the current writer. No internal parallelism. DuckDB Appender and Parquet writer are single-threaded; complicating the pipeline with parallelism would just add lock contention.
- **Rotation is transparent**: `_rotator.CurrentWriter` returns whatever the current writer is. If a rotation happens between reading a record and writing it, the next record goes to the new writer. The brief stall during writer close → reopen is absorbed by the transport's buffer.
- **No exception escape**: any error writing a single record becomes a capture gap, not a pipeline crash. The pipeline keeps running. The only way to stop it is cancellation.

### 6.5 BackpressureMonitor and DropPolicy

```csharp
namespace Tracer.Agent.Ingestion;

public enum BackpressureLevel
{
    Healthy,
    FastStateAtRisk,    // fast state will be dropped
    SlowStateAtRisk,    // fast and slow state dropped
    EventsAtRisk,       // everything but events dropped
    Saturated           // events dropping too
}

public sealed class BackpressureMonitor
{
    private readonly IAgentTransport _transport;
    private readonly BackpressureConfig _config;

    public BackpressureMonitor(IAgentTransport transport, AgentConfig agentConfig)
    {
        _transport = transport;
        _config = agentConfig.Backpressure;
    }

    public BackpressureLevel CurrentLevel()
    {
        var health = _transport.GetHealth();
        var inflight = health.PendingCount;
        if (inflight >= _config.EventsDropThresholdRecords) return BackpressureLevel.Saturated;
        if (inflight >= _config.SlowStateDropThresholdRecords) return BackpressureLevel.EventsAtRisk;
        if (inflight >= _config.FastStateDropThresholdRecords) return BackpressureLevel.SlowStateAtRisk;
        if (inflight >= _config.InflightThresholdRecords) return BackpressureLevel.FastStateAtRisk;
        return BackpressureLevel.Healthy;
    }
}

public sealed class DropPolicy
{
    public bool ShouldDrop(DiagnosticRecord record, BackpressureLevel level, out CaptureGapReason reason)
    {
        reason = default;
        if (level == BackpressureLevel.Healthy) return false;

        if (record is StateSampleRecord ss)
        {
            if (ss.Rate == StateSampleRate.Fast && level >= BackpressureLevel.FastStateAtRisk)
            {
                reason = CaptureGapReason.BackpressureFastStateDropped;
                return true;
            }
            if (ss.Rate == StateSampleRate.Slow && level >= BackpressureLevel.SlowStateAtRisk)
            {
                reason = CaptureGapReason.BackpressureSlowStateDropped;
                return true;
            }
        }
        else if (record is EventRecord && level >= BackpressureLevel.Saturated)
        {
            reason = CaptureGapReason.BackpressureEventsDropped;
            return true;
        }
        return false;
    }
}
```

**The escalation policy**:
- **FastStateAtRisk** (50K pending): drop incoming fast state, accept events & slow state
- **SlowStateAtRisk** (70K): drop fast + slow state, accept events
- **EventsAtRisk** (90K): same as SlowStateAtRisk; warning is logged
- **Saturated** (98K): everything drops including events

Events are the most valuable diagnostic data; they drop only as a last resort. Each drop is recorded as a `CaptureGap` so the post-hoc analysis knows there was data loss.

### 6.6 AgentHostedService — The Loop

```csharp
namespace Tracer.Agent.Lifecycle;

public sealed class AgentHostedService : BackgroundService
{
    private readonly StartupRecoveryService _recovery;
    private readonly IntervalRotator _rotator;
    private readonly IntervalScheduler _scheduler;
    private readonly IngestionPipeline _ingestion;
    private readonly RetentionManager _retention;
    private readonly IClock _clock;
    private readonly ILogger<AgentHostedService> _logger;

    public AgentHostedService(
        StartupRecoveryService recovery, IntervalRotator rotator,
        IntervalScheduler scheduler, IngestionPipeline ingestion,
        RetentionManager retention, IClock clock,
        ILogger<AgentHostedService> logger)
    {
        _recovery = recovery;
        _rotator = rotator;
        _scheduler = scheduler;
        _ingestion = ingestion;
        _retention = retention;
        _clock = clock;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("TracerAgent starting");

        // 1. Recovery
        await _recovery.RecoverAsync(stoppingToken);

        // 2. Open the current interval
        await _rotator.OpenCurrentAsync(stoppingToken);

        // 3. Start ingestion in background
        var ingestionTask = _ingestion.RunAsync(stoppingToken);

        // 4. Start retention loop in background
        var retentionTask = RetentionLoopAsync(stoppingToken);

        // 5. Rotation loop runs on this task
        await RotationLoopAsync(stoppingToken);

        // 6. On shutdown: cancellation propagates to ingestionTask
        await Task.WhenAll(ingestionTask, retentionTask);

        // 7. Final rotation to close current interval
        await _rotator.RotateAsync(ManifestFinalizationReason.GracefulShutdown, CancellationToken.None);

        _logger.LogInformation("TracerAgent stopped");
    }

    private async Task RotationLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var timeUntilBoundary = _scheduler.TimeUntilNextBoundary();
            if (timeUntilBoundary > TimeSpan.Zero)
            {
                try { await Task.Delay(timeUntilBoundary, ct); }
                catch (OperationCanceledException) { return; }
            }
            await _rotator.RotateAsync(ManifestFinalizationReason.ScheduledRotation, ct);
        }
    }

    private async Task RetentionLoopAsync(CancellationToken ct)
    {
        var interval = TimeSpan.FromMinutes(5);  // check every 5 minutes
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await _retention.ApplyAsync(ct);
                await Task.Delay(interval, ct);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Retention pass failed; continuing");
                try { await Task.Delay(interval, ct); }
                catch (OperationCanceledException) { return; }
            }
        }
    }
}
```

---

## 7. Startup Recovery

When the agent starts, it scans `intervals/` for orphaned intervals (folders lacking `_ready`). For each orphan, it tries to finalize it.

### 7.1 StartupRecoveryService

```csharp
namespace Tracer.Agent.Lifecycle;

public sealed class StartupRecoveryService
{
    private readonly AgentConfig _config;
    private readonly ManifestWriter _manifestWriter;
    private readonly UploadIntentDispatcher _uploadDispatcher;
    private readonly IClock _clock;
    private readonly ILogger<StartupRecoveryService> _logger;

    public async Task RecoverAsync(CancellationToken ct)
    {
        var intervalsRoot = Path.Combine(_config.DataRoot, "intervals");
        if (!Directory.Exists(intervalsRoot))
        {
            Directory.CreateDirectory(intervalsRoot);
            return;
        }

        var orphans = new List<IntervalDirectory>();
        foreach (var folder in Directory.EnumerateDirectories(intervalsRoot))
        {
            var name = Path.GetFileName(folder);
            if (!IntervalTimestamp.TryParse(name, out var ts)) continue;
            var dir = new IntervalDirectory(_config.DataRoot, ts);
            if (!dir.IsReady)
                orphans.Add(dir);
        }

        if (orphans.Count == 0)
        {
            _logger.LogInformation("Startup recovery: no orphaned intervals");
            return;
        }

        _logger.LogWarning(
            "Startup recovery: found {Count} orphaned interval(s)", orphans.Count);

        foreach (var orphan in orphans.OrderBy(o => o.Timestamp.Value))
        {
            await TryFinalizeAsync(orphan, ct);
        }
    }

    private async Task TryFinalizeAsync(IntervalDirectory orphan, CancellationToken ct)
    {
        _logger.LogWarning("Finalizing orphaned interval {Interval}", orphan.Timestamp.Value);

        // Try to count what's in the DuckDB files (best effort — they may be partial)
        long eventCount = 0;
        long slowStateCount = 0;
        var fastStateTopics = new List<string>();

        try
        {
            await using var reader = await DuckDbStorageReader.OpenAsync(orphan.EventsDbPath, 
                NullLogger<DuckDbStorageReader>.Instance, ct);
            eventCount = await reader.CountEventsAsync(EventFilter.All, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read events.duckdb from orphan {Interval}", orphan.Timestamp.Value);
        }

        // (analogous for slow_state.duckdb)

        if (Directory.Exists(orphan.FastStateDirectory))
        {
            foreach (var f in Directory.EnumerateFiles(orphan.FastStateDirectory, "*.parquet"))
                fastStateTopics.Add(Path.GetFileNameWithoutExtension(f));
        }

        // Compute the gap — we don't know exactly when the crash happened.
        // Use interval start to interval end as the worst case.
        var gap = new CaptureGap
        {
            StartUtc = WallclockTime.FromDateTimeOffset(orphan.Timestamp.ToDateTimeOffset()),
            EndUtc = WallclockTime.FromDateTimeOffset(
                orphan.Timestamp.ToDateTimeOffset() + _config.IntervalDuration),
            Reason = CaptureGapReason.UnrecoveredCrashGap,
            DroppedRecordCount = 0,  // unknown
            Detail = "Interval finalized during startup recovery; some data may be lost"
        };

        var manifest = new IntervalManifest
        {
            IntervalStart = orphan.Timestamp,
            IntervalEnd = IntervalTimestamp.FromUtc(
                orphan.Timestamp.ToDateTimeOffset() + _config.IntervalDuration),
            NodeId = new AgentId(_config.NodeId),
            TracerVersion = TracerVersion.Current,
            SchemaVersion = SchemaV1.Version,
            EventCount = eventCount,
            SlowStateCount = slowStateCount,
            FastStateTopics = fastStateTopics,
            CaptureGaps = new[] { gap },
            SessionMarkers = Array.Empty<SessionMarker>(),  // we don't try to scan for these on recovery
            FinalizedAt = _clock.Now,
            FinalizationReason = ManifestFinalizationReason.RecoveryAfterCrash
        };

        await _manifestWriter.WriteAsync(orphan.ManifestPath, manifest, ct);
        orphan.WriteReadySentinel();

        _logger.LogInformation(
            "Finalized recovered interval {Interval}: events={Events} (with crash gap)",
            orphan.Timestamp.Value, eventCount);

        // Dispatch upload — sync system idempotency handles re-uploads
        await _uploadDispatcher.DispatchAsync(orphan, manifest, ct);
    }
}
```

**Recovery design notes**:

- **DuckDB's WAL handles most of the work**: when we open a partially-written DuckDB, it auto-recovers to the last checkpoint. We get whatever data was there at last checkpoint.
- **Parquet's row-group atomicity handles fast state**: complete row groups are valid; incomplete groups simply don't appear in the file. Parquet files left after crash are typically slightly smaller than they would have been but contain only valid rows.
- **The crash gap is conservatively reported as the entire interval**: we can't know precisely when the crash happened. Downstream analysis treats this as "this interval has uncertain completeness."
- **Session markers from recovered intervals are not extracted**: scanning DuckDB for them after recovery is possible but not worth the complexity. Tools that need session boundaries from recovered intervals can re-scan downstream.

### 7.2 Determining "Last Checkpoint" Data Visibility

DuckDB checkpoints persist WAL to the main file on commit. The agent issues an explicit `CHECKPOINT` periodically (default: every 60s, configurable). On crash, data between last checkpoint and crash is recoverable from the WAL but only if the WAL itself wasn't corrupted.

**Phase 2 acceptance**: data loss bounded by the checkpoint interval is acceptable. Tests verify the bound.

---

## 8. Mock Adapters for Phase 2

### 8.1 InProcessChannelTransport

```csharp
namespace Tracer.Adapters.Mock.Transport;

/// <summary>
/// In-process transport using System.Threading.Channels.
/// Producers (test code, mock data source) push records via WriteAsync.
/// The agent (sole consumer) pulls via ReadAsync.
/// </summary>
public sealed class InProcessChannelTransport : IAgentTransport
{
    private readonly Channel<DiagnosticRecord> _channel;
    private readonly int _capacity;
    private long _totalReceived;
    private long _totalDropped;
    private WallclockTime _lastReceivedAt = WallclockTime.Zero;
    private readonly object _statsLock = new();

    public InProcessChannelTransport(int capacityRecords)
    {
        _capacity = capacityRecords;
        _channel = Channel.CreateBounded<DiagnosticRecord>(new BoundedChannelOptions(capacityRecords)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false  // producers may be multi-threaded
        });
    }

    /// <summary>Called by mock producers (or by the simulation in production).</summary>
    public async ValueTask WriteAsync(DiagnosticRecord record, CancellationToken ct)
    {
        await _channel.Writer.WriteAsync(record, ct);
        lock (_statsLock)
        {
            _totalReceived++;
            _lastReceivedAt = record.PublishWallclock;
        }
    }

    public IAsyncEnumerable<DiagnosticRecord> ReadAsync(CancellationToken ct)
        => _channel.Reader.ReadAllAsync(ct);

    public TransportHealth GetHealth()
    {
        lock (_statsLock)
        {
            return new TransportHealth
            {
                PendingCount = _channel.Reader.Count,
                Capacity = _capacity,
                TotalReceived = _totalReceived,
                TotalDropped = _totalDropped,
                LastReceivedAt = _lastReceivedAt
            };
        }
    }

    public void Complete() => _channel.Writer.TryComplete();

    public ValueTask DisposeAsync()
    {
        Complete();
        return ValueTask.CompletedTask;
    }
}
```

### 8.2 LocalFileSystemUploadService

```csharp
namespace Tracer.Adapters.Mock.Upload;

/// <summary>
/// Mock upload service that copies interval files to a local "fake NAS" directory.
/// Synchronous, in-process, deterministic — good for tests.
/// </summary>
public sealed class LocalFileSystemUploadService : ITelemetryUploadService
{
    private readonly string _fakeNasRoot;
    private readonly ILogger<LocalFileSystemUploadService> _logger;
    private readonly ConcurrentDictionary<UploadIntentId, UploadStatus> _statuses = new();

    public LocalFileSystemUploadService(string fakeNasRoot, ILogger<LocalFileSystemUploadService> logger)
    {
        if (!Path.IsPathFullyQualified(fakeNasRoot))
            throw new ArgumentException("fakeNasRoot must be absolute", nameof(fakeNasRoot));
        _fakeNasRoot = fakeNasRoot;
        _logger = logger;
    }

    public async Task<UploadIntentId> RequestUploadAsync(UploadRequest request, CancellationToken ct)
    {
        var id = new UploadIntentId(Guid.NewGuid().ToString("N"));
        _statuses[id] = UploadStatus.InProgress;

        // Compute target path: {fakeNasRoot}/{nodeId}/{intervalTimestamp}.zip
        var targetDir = Path.Combine(_fakeNasRoot, request.NodeId.Value);
        Directory.CreateDirectory(targetDir);
        var targetZipPath = Path.Combine(targetDir, $"{request.Interval.Value}.zip");

        try
        {
            await Task.Run(() =>
            {
                if (File.Exists(targetZipPath))
                    File.Delete(targetZipPath);

                using var zipFs = File.Create(targetZipPath);
                using var archive = new ZipArchive(zipFs, ZipArchiveMode.Create);

                foreach (var file in request.Files)
                {
                    var entryName = Path.GetFileName(file.Path);
                    if (file.Path.Contains("fast_state"))
                        entryName = "fast_state/" + entryName;

                    var compression = file.Path.EndsWith(".parquet", StringComparison.OrdinalIgnoreCase)
                        ? CompressionLevel.NoCompression
                        : CompressionLevel.Optimal;

                    var entry = archive.CreateEntry(entryName, compression);
                    using var entryStream = entry.Open();
                    using var fileStream = File.OpenRead(file.Path);
                    fileStream.CopyTo(entryStream);
                }
            }, ct);

            _statuses[id] = UploadStatus.Complete;
            _logger.LogInformation(
                "Mock upload complete: {NodeId}/{Interval} -> {Target}",
                request.NodeId, request.Interval, targetZipPath);
        }
        catch (Exception ex)
        {
            _statuses[id] = UploadStatus.Failed;
            _logger.LogError(ex, "Mock upload failed: {NodeId}/{Interval}",
                request.NodeId, request.Interval);
        }

        return id;
    }

    public Task<UploadStatus> GetStatusAsync(UploadIntentId intentId, CancellationToken ct)
    {
        return Task.FromResult(_statuses.GetValueOrDefault(intentId, UploadStatus.Unknown));
    }
}
```

**This is what makes Phase 2 self-contained**: the agent doesn't know it's talking to a mock. It calls `ITelemetryUploadService.RequestUploadAsync` and the result is a zip on disk at the "fake NAS" location. Phase 4 (aggregator) reads from the same fake NAS location. Phase 11 swaps in the real sync system implementation.

---

## 9. FakeNode Application

`tracer-fakenode.exe` is the development workhorse — combines mock data source, in-process transport, agent, and upload service into one runnable process.

### 9.1 Purpose

- Run a complete agent stack locally without any external dependencies
- Drive synthetic scenarios end-to-end
- Provide a target for integration tests (`FakeNodeFixture`)
- Demo Tracer to non-developers before any real adapters exist
- Validate the agent against realistic data shapes

### 9.2 Program.cs

```csharp
namespace Tracer.FakeNode;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            var config = FakeNodeConfigLoader.Load(args);
            
            var builder = Host.CreateApplicationBuilder(args);
            
            // Log file convention
            var logFilePath = Path.Combine(config.AgentConfig.LogsRoot, "tracer-fakenode.json");
            Console.WriteLine($"LOG_FILE={logFilePath}");
            
            // Set up everything in one process
            builder.Services.AddSingleton(config);
            builder.Services.AddSingleton(config.AgentConfig);
            
            // Mock data source
            builder.Services.AddSingleton<MockDataSource>(sp =>
                new MockDataSource(config.ScenarioName, config.ScenarioConfig));
            
            // Mock transport — both writer (from data source) and reader (agent)
            builder.Services.AddSingleton<InProcessChannelTransport>(sp =>
                new InProcessChannelTransport(config.AgentConfig.Transport.CapacityRecords));
            builder.Services.AddSingleton<IAgentTransport>(sp =>
                sp.GetRequiredService<InProcessChannelTransport>());
            
            // Mock upload
            builder.Services.AddSingleton<ITelemetryUploadService>(sp =>
                new LocalFileSystemUploadService(
                    config.AgentConfig.UploadService.LocalFileSystemRoot!,
                    sp.GetRequiredService<ILogger<LocalFileSystemUploadService>>()));
            
            // Clock — wall-clock in FakeNode mode (real-time scenario execution)
            builder.Services.AddSingleton<IClock, SystemClock>();
            
            // Agent components (same as tracer-agent.exe)
            AddAgentServices(builder.Services);
            
            // The orchestrator: drives the mock data source into the transport
            builder.Services.AddHostedService<FakeNodeOrchestrator>();
            
            // Logging
            builder.Services.AddSerilog((sp, lc) => ConfigureSerilog(lc, config));
            
            var host = builder.Build();
            await host.RunAsync();
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"FATAL: {ex}");
            return 1;
        }
    }
    
    private static void AddAgentServices(IServiceCollection services)
    {
        services.AddSingleton<IntervalScheduler>();
        services.AddSingleton<IntervalRotator>();
        services.AddSingleton<StartupRecoveryService>();
        services.AddSingleton<IngestionPipeline>();
        services.AddSingleton<BackpressureMonitor>();
        services.AddSingleton<DropPolicy>();
        services.AddSingleton<RetentionManager>();
        services.AddSingleton<ManifestWriter>();
        services.AddSingleton<UploadIntentDispatcher>();
        services.AddSingleton<IReadOnlyDictionary<string, ParquetTopicSchema>>(
            _ => WellKnownTopicSchemas.ToDictionary());
        services.AddHostedService<AgentHostedService>();
    }
}
```

### 9.3 FakeNodeOrchestrator

The orchestrator drives records from the mock data source into the transport. In production this role is filled by DDS subscribers; in FakeNode it's a scenario script playing back.

```csharp
namespace Tracer.FakeNode;

public sealed class FakeNodeOrchestrator : BackgroundService
{
    private readonly MockDataSource _dataSource;
    private readonly InProcessChannelTransport _transport;
    private readonly ILogger<FakeNodeOrchestrator> _logger;
    private readonly FakeNodeConfig _config;

    public FakeNodeOrchestrator(
        MockDataSource dataSource,
        InProcessChannelTransport transport,
        FakeNodeConfig config,
        ILogger<FakeNodeOrchestrator> logger)
    {
        _dataSource = dataSource;
        _transport = transport;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "FakeNode orchestrator starting scenario {Scenario}",
            _config.ScenarioName);

        try
        {
            await foreach (var record in _dataSource.ReadAsync(stoppingToken))
            {
                await _transport.WriteAsync(record, stoppingToken);
            }
            _logger.LogInformation("Scenario {Scenario} completed", _config.ScenarioName);

            // Signal transport completion so the agent's ingestion ends
            _transport.Complete();
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("FakeNode orchestrator stopping");
        }
    }
}
```

### 9.4 FakeNodeConfig

```csharp
namespace Tracer.FakeNode.Configuration;

public sealed class FakeNodeConfig
{
    public required string ScenarioName { get; init; }
    public required ScenarioConfig ScenarioConfig { get; init; }
    public required AgentConfig AgentConfig { get; init; }
}
```

### 9.5 Example fakenode.json

```json
{
  "FakeNode": {
    "ScenarioName": "CombatEngagement",
    "ScenarioConfig": {
      "Duration": "00:30:00",
      "NodeCount": 4,
      "EntityCount": 20,
      "EventsPerSecond": 200,
      "Seed": 42,
      "StartTime": "2026-05-19T14:00:00Z"
    },
    "AgentConfig": {
      "NodeId": "fakenode-01",
      "DataRoot": "C:/Tracer/fakenode/agent",
      "LogsRoot": "C:/Tracer/fakenode/logs",
      "IntervalDuration": "00:15:00",
      "KeepLastNIntervals": 4,
      "Transport": { "Kind": "InProcessChannel", "CapacityRecords": 100000 },
      "UploadService": { "Kind": "LocalFileSystem", "LocalFileSystemRoot": "C:/Tracer/fakenode/mock-nas/telemetry" },
      "Backpressure": {
        "InflightThresholdRecords": 50000,
        "FastStateDropThresholdRecords": 70000,
        "SlowStateDropThresholdRecords": 90000,
        "EventsDropThresholdRecords": 98000
      }
    }
  }
}
```

**Running**: `tracer-fakenode.exe --config C:/Tracer/fakenode/config.json`. The process runs until the scenario duration completes, then shuts down. Multiple FakeNode instances can run on one machine for multi-node testing — give them different NodeIds, different DataRoots, but they can share the same fake-nas root.

---

## 10. Test Plan for Phase 2

### 10.1 Unit Tests

**Agent/IntervalSchedulerTests.cs**
- `CurrentIntervalStart` aligns to wall-clock boundary
- `NextIntervalBoundary` is exactly current + duration
- `TimeUntilNextBoundary` decreases as `IClock` advances
- 11-minute interval throws (doesn't divide a day)
- 24-hour interval is allowed
- Less than 1-minute interval throws

**Agent/IntervalRotatorTests.cs**
- `OpenCurrentAsync` creates the interval directory and a writer
- `OpenCurrentAsync` twice throws (already open)
- `RotateAsync` closes the current writer, writes manifest+_ready, opens next
- `RotateAsync` while already rotating returns immediately (lock held)
- `NotifyRecordWritten` increments counters by type
- `NotifyCaptureGap` accumulates gaps
- Session-start event is captured into manifest's sessionMarkers
- `DisposeAsync` triggers final rotation with `GracefulShutdown` reason

**Agent/RecordRouterTests.cs**
- (If routing logic ends up centralized in a separate class)
- Event → events.duckdb append
- Slow state → slow_state.duckdb append
- Fast state → Parquet append
- Each routes via `IDiagnosticStorageWriter` interface

**Agent/DropPolicyTests.cs**
- Healthy: nothing dropped
- FastStateAtRisk: fast state dropped, events/slow accepted
- SlowStateAtRisk: fast and slow dropped, events accepted
- Saturated: events dropped
- Drop reason matches the level

**Agent/ManifestWriterTests.cs**
- Manifest JSON written matches expected schema
- Round-trip: write then read produces equal `IntervalManifest`
- Wallclock times serialize as ISO 8601 with ns precision

**Agent/StartupRecoveryTests.cs**
- No orphans: recovery is a no-op
- One orphan: recovery writes manifest with crash gap, writes _ready
- Multiple orphans: all finalized
- Orphan with valid `events.duckdb`: event count read correctly from recovered DB
- Orphan with corrupted DuckDB: recovery logs warning, sets count to 0, still completes finalization

**Storage/FastStateParquetWriterTests.cs**
- Empty writer disposes without writing rows
- 10K rows written and read back match
- Multiple row groups flushed correctly
- Crash mid-row-group: file is valid (previous row groups), incomplete data lost

### 10.2 Integration Tests

**AgentIntervalLifecycleTests.cs**
- Run 3 simulated intervals → 3 completed interval directories on disk, each with `_ready`
- Each interval's manifest has correct counts matching what was sent
- Mock upload service received 3 upload requests
- No data loss in healthy conditions (events sent == events in DuckDBs across intervals)

**AgentRecoveryTests.cs**
- Start agent, write 1000 events, kill mid-flight (no graceful shutdown)
- Restart agent → finds 1 orphan, finalizes it
- Manifest has `RecoveryAfterCrash` reason and a `UnrecoveredCrashGap`
- DuckDB readable, contains data up to last checkpoint
- Second interval opens normally and accepts more records

**AgentBackpressureTests.cs**
- Configure transport with capacity 1000, backpressure thresholds proportionally lowered
- Sustained ingest at 10x writer throughput
- Verify: fast state drops first, then slow state, then events
- Verify: drops appear in `captureGaps`
- Verify: every dropped record has a corresponding gap entry

**FakeNodeEndToEndTests.cs**
- Run FakeNode with `CombatEngagement` scenario, 1-hour duration, 15-minute intervals → 4 intervals on disk
- Each interval zipped to fake NAS
- Total event count across intervals matches scenario's expected event count
- No `captureGaps` in healthy mode
- Session-start markers found in intervals where session events occurred

### 10.3 Performance Tests

Run nightly, not on every PR.

- Sustained 100K events/sec for 60 seconds: zero drops, completes interval cleanly
- Sustained 10K fast-state samples/sec across 5 topics: all written to Parquet, file sizes reasonable
- 24-hour FakeNode run: storage retention evicts oldest, disk stable

### 10.4 Test Fixtures

**TracerAgentFixture** spins up an agent in-process with mock dependencies:

```csharp
public sealed class TracerAgentFixture : IAsyncDisposable
{
    public IHost Host { get; private set; } = null!;
    public InProcessChannelTransport Transport { get; private set; } = null!;
    public string DataRoot { get; private set; } = null!;
    public string FakeNasRoot { get; private set; } = null!;
    public IntervalRotator Rotator => Host.Services.GetRequiredService<IntervalRotator>();
    public ITelemetryUploadService UploadService => Host.Services.GetRequiredService<ITelemetryUploadService>();
    public SimulatedClock? SimulatedClock { get; private set; }

    public static async Task<TracerAgentFixture> CreateAsync(
        AgentFixtureOptions? options = null,
        CancellationToken ct = default)
    {
        // ... constructs Host with overrides, returns fixture ...
    }

    /// <summary>Push a record into the agent's transport.</summary>
    public ValueTask PushAsync(DiagnosticRecord record, CancellationToken ct = default)
        => Transport.WriteAsync(record, ct);

    /// <summary>Trigger a rotation immediately (testing only).</summary>
    public Task ForceRotationAsync(CancellationToken ct = default)
        => Rotator.RotateAsync(ManifestFinalizationReason.ScheduledRotation, ct);

    /// <summary>Advance simulated clock past next boundary, triggering normal rotation.</summary>
    public async Task AdvanceToNextBoundaryAsync(CancellationToken ct = default) { /* ... */ }

    public async ValueTask DisposeAsync() { /* graceful host stop, cleanup */ }
}
```

**FakeNodeFixture** runs the full FakeNode in-process:

```csharp
public sealed class FakeNodeFixture : IAsyncDisposable
{
    public static async Task<FakeNodeFixture> RunScenarioAsync(
        string scenarioName, ScenarioConfig scenarioConfig, AgentConfig agentConfig,
        CancellationToken ct = default)
    {
        // Constructs full FakeNode host, runs to completion, returns fixture for inspection
    }

    public string FakeNasRoot { get; }
    public IReadOnlyList<string> IntervalZipPaths { get; }   // all intervals uploaded
    public IReadOnlyList<IntervalManifest> Manifests { get; } // parsed manifests

    public async ValueTask DisposeAsync() { /* ... */ }
}
```

---

## 11. Phase 2 Risks and Mitigations

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| Parquet.Net API quirks slow down implementation | High | Medium | Day 1 spike: write 10K rows of transforms to Parquet, verify DuckDB reads them via `read_parquet()`. Adjust schema design if needed. |
| Interval rotation has race conditions under high ingestion load | Medium | High | The rotator uses a semaphore for mutual exclusion. The pipeline reads `CurrentWriter` per record; brief stalls during rotation are acceptable. Tests stress this with high-rate ingestion across rotations. |
| DuckDB Appender close/reopen pattern is slow | Medium | Medium | Profile rotation latency in spike work. If >100ms, investigate Appender flush semantics. Worst case: keep writer open across intervals and tag rows with interval — but this complicates the storage model significantly. |
| Recovery loses too much data (DuckDB checkpoint interval too long) | Low | Medium | Default checkpoint every 60s. Configurable. Tests verify the bound. |
| Backpressure tests are flaky due to async timing | Medium | Low | Use `SimulatedClock` and synchronous test patterns. Backpressure thresholds in tests should be tiny (100 records) to make saturation deterministic. |
| Windows service install/run friction during dev | Low | Low | Console mode is the default during dev. Windows service mode is tested but not the daily-driver. |
| Logging volume overwhelms disk in long runs | Low | Low | Serilog file sink already has rolling + retention. Default retains 14 days. |
| Multiple FakeNode instances on one machine collide on fake NAS | Low | Low | Each FakeNode has its own NodeId; fake NAS root is shared but per-node subdirectories prevent collision. |

---

## 12. Definition of Done for Phase 2

- [ ] `tracer-agent.exe` builds, runs as console app, runs as Windows service
- [ ] `tracer-fakenode.exe` builds and runs to completion for `CalmScenario` and `CombatEngagement` scenarios
- [ ] All Phase 1 tests still pass
- [ ] New unit tests pass (target: 30+ new test methods)
- [ ] New integration tests pass (target: 8+ new test methods)
- [ ] Recovery test passes: kill mid-write, restart, orphan finalized
- [ ] Backpressure test passes: drop order is fast → slow → events
- [ ] No-data-loss test passes: in healthy mode, every record sent appears in either the current or a completed interval
- [ ] Logs follow `LOG_FILE=` convention; logs are valid JSON one event per line
- [ ] Manifest schema matches architecture §8.1
- [ ] Fast-state Parquet files are readable by DuckDB via `read_parquet()`
- [ ] FakeNode can run 1-hour simulated scenario with 15-min intervals, producing 4 valid interval zips on the fake NAS
- [ ] Documentation update: README explains how to run FakeNode and inspect outputs

---

## 13. Handoff to Phase 3

What Phase 3 inherits from Phase 2:

- **Long-running process patterns**: `Microsoft.Extensions.Hosting`, Serilog, `LOG_FILE=` convention, graceful shutdown — Phase 3 reuses for the observer.
- **`IAgentTransport` abstraction**: the observer subscribes to the same kind of data source. In production it's a DDS subscriber; in development it's a `MockDataSource` adapter wrapped in transport. The observer uses the same `IngestionPipeline`-style pattern.
- **Storage layout per interval**: the observer applies the same interval rotation to its central DuckDB. The viewer queries across multiple completed intervals.
- **`LocalFileSystemUploadService`**: the observer doesn't upload to NAS, but the FakeNode + agent + upload service stack is in place for Phase 4 (aggregator) to consume.

What Phase 3 must address that Phase 2 deferred:

- HTTP server (ASP.NET Core)
- REST endpoints for event/session queries
- SSE streaming for live updates
- Vue project scaffolding and the first user-facing view (Scenario View)
- Multi-process scenarios (observer running on one machine, FakeNodes simulating production nodes)

Phase 2's contribution is the **complete per-node data capture stack**. After Phase 2, telemetry data flows end-to-end from a mock source through a real agent to mock NAS storage. Phase 3 makes this data visible to humans for the first time.
