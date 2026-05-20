# BATCH-04 Instructions

**Batch:** BATCH-04  
**Phase:** Phase 2 — TracerAgent, Interval Rotation, Fast State, FakeNode (Part 2 of 3)  
**Tasks:** TRC-P2-006, TRC-P2-007, TRC-P2-008, TRC-P2-009  
**Previous review:** `.dev/tracer/reviews/BATCH-03-REVIEW.md`  
**Design reference:** `docs/tracer_phase2_design.md` (§7, §8, §9)  
**Task detail reference:** `docs/TASK-DETAIL.md` (TRC-P2-006 through TRC-P2-009)

---

## Context

BATCH-03 is committed (commit `6ebadd2`). Current state:
- 97 tests passing (87 unit + 10 integration)
- `RetentionManager.ApplyAsync` and `StartupRecoveryService.RecoverAsync` are stubs (no-ops)
- `LocalFileSystemUploadService.RequestUploadAsync` does NOT yet create ZIP archives
- `Tracer.FakeNode` project does not yet exist
- `TracerStackFixture` works with the updated `DuckDbStorageWriter.CreateAsync` signature

This batch replaces the two stubs, completes the upload service with ZIP behaviour, adds mock transport tests, and creates the FakeNode executable.

---

## Tasks

### TRC-P2-006 — Startup Recovery

**Reference:** `docs/TASK-DETAIL.md#trc-p2-006` for exact success conditions (SC1-SC9), `docs/tracer_phase2_design.md §7`.

**File to fully implement (replacing stub):**

`src/Tracer.Agent/Lifecycle/StartupRecoveryService.cs`

**Implementation per §7.1:**

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
        // If directory doesn't exist, create it and return
        // Enumerate subfolders; for each: TryParse name as IntervalTimestamp
        // If already IsReady, skip
        // Otherwise treat as orphan and call TryFinalizeAsync
        // Process in ascending timestamp order
        // Per-orphan failures are caught, logged as Warning, do NOT abort remaining
    }

    private async Task TryFinalizeAsync(IntervalDirectory orphan, CancellationToken ct)
    {
        // 1. Try to open events.duckdb read-only and count events
        //    On any exception: eventCount = 0, log Warning, continue
        // 2. Try to open slow_state.duckdb analogously
        // 3. Enumerate fast_state/*.parquet for topic names
        // 4. Build IntervalManifest:
        //    - FinalizationReason = RecoveryAfterCrash
        //    - CaptureGaps = [one gap spanning full interval with UnrecoveredCrashGap]
        //    - SessionMarkers = []
        // 5. Write manifest, write _ready
        // 6. Dispatch upload
    }
}
```

**Key notes:**
- Must add constructor accepting `AgentConfig`, `ManifestWriter`, `UploadIntentDispatcher`, `IClock`, `ILogger<StartupRecoveryService>`
- Update `AgentHostBuilder` to inject these parameters when registering `StartupRecoveryService`
- `DuckDbStorageReader.OpenAsync` signature: check if it takes a file path or directory; pass `orphan.EventsDbPath`
- The `intervals/` directory creation when absent (SC2) must use `Directory.CreateDirectory`
- The `CaptureGap` for a recovered orphan spans `StartUtc = WallclockTime.FromDateTimeOffset(orphan.Timestamp.ToDateTimeOffset())` to `EndUtc = WallclockTime.FromDateTimeOffset(orphan.Timestamp.ToDateTimeOffset() + _config.IntervalDuration)`

**New test file:**

`tests/Tracer.Tests.Unit/Agent/StartupRecoveryTests.cs` — all 6 test methods from SC8:
1. `StartupRecovery_NoIntervalsDirectory_CreatesDirectoryAndReturns`
2. `StartupRecovery_NoOrphans_LogsAndReturns`
3. `StartupRecovery_OneOrphan_WritesManifestAndSentinel`
4. `StartupRecovery_OneOrphan_ManifestHasRecoveryReason`
5. `StartupRecovery_MultipleOrphans_AllFinalized`
6. `StartupRecovery_CorruptEventsDb_CountsAsZeroAndContinues`

For tests 3-6, create real interval directories with actual DuckDB files (use `DuckDbStorageWriter.CreateAsync` then dispose). For test 6, simply delete or corrupt the events.duckdb file after creation.

---

### TRC-P2-007 — Upload & Retention

**Reference:** `docs/TASK-DETAIL.md#trc-p2-007` for exact success conditions (SC1-SC8), `docs/tracer_phase2_design.md §6.3 and §6.6`.

**Files to fully implement (replacing stubs):**

`src/Tracer.Agent/Storage/RetentionManager.cs`

```csharp
public sealed class RetentionManager
{
    // Keep last N intervals (lexicographic = chronological order)
    // Do NOT delete intervals without _ready
    // Do NOT delete the currently open interval
    // Also enforce DiskWatermarkPercent:
    //   if available free space < DiskWatermarkPercent% of total,
    //   evict oldest ready intervals until free or only 1 remains
    
    public async Task ApplyAsync(CancellationToken ct)
    {
        var intervalsRoot = Path.Combine(_config.DataRoot, "intervals");
        if (!Directory.Exists(intervalsRoot)) return;
        
        // Collect all ready interval folders in ascending order
        // Keep last N; delete the rest
        // Also check disk watermark
    }
}
```

**Key notes:**
- `RetentionManager` needs a way to know the currently-open interval; inject `IntervalRotator` OR pass the open interval timestamp as a parameter to `ApplyAsync`. The cleaner approach: inject `IntervalRotator` as a dependency and check `_rotator.CurrentDirectory?.Timestamp`
- `DriveInfo` for disk watermark: `new DriveInfo(intervalsRoot).AvailableFreeSpace` and `TotalSize`
- At least 1 interval is always preserved regardless of disk pressure (per SC4)

**New test files:**

`tests/Tracer.Tests.Unit/Agent/RetentionManagerTests.cs` — 3 test methods from SC6:
1. `RetentionManager_KeepLast3_WithFiveIntervals_DeletesOldestTwo`
2. `RetentionManager_OrphanNotDeleted`
3. `RetentionManager_NothingToEvict_NoException`

`tests/Tracer.Tests.Unit/Agent/UploadIntentDispatcherTests.cs` — 2 test methods from SC7:
1. `UploadIntentDispatcher_Dispatch_CallsUploadServiceOnce`
2. `UploadIntentDispatcher_Dispatch_IncludesAllIntervalFiles`

For these tests, create real interval directories with events.duckdb, slow_state.duckdb, manifest.json, and _ready file. The `UploadIntentDispatcher` tests need a `FakeUploadService` that captures the `UploadRequest`.

---

### TRC-P2-008 — Mock Transport & Upload

**Reference:** `docs/TASK-DETAIL.md#trc-p2-008` for exact success conditions (SC1-SC8), `docs/tracer_phase2_design.md §8`.

**Files to update:**

`src/Tracer.Adapters.Mock/Transport/InProcessChannelTransport.cs` — Complete the drop-tracking:
- When `BoundedChannelFullMode.DropOldest` drops a record, `_totalDropped` must be incremented. **However**, `System.Threading.Channels` does not provide a callback when items are dropped. The fix: track `TotalDropped` as `TotalReceived - channel.Reader.Count - TotalConsumed` (requires a separate consumed counter), OR use a custom `IChannelSink` approach. **Simplest approach**: override `WriteAsync` to check if the channel is at capacity before writing: if `_channel.Reader.Count >= _capacity`, increment `_totalDropped` and allow the channel's own `DropOldest` to proceed naturally. This is a slight approximation but matches the observable behavior.

`src/Tracer.Adapters.Mock/Upload/LocalFileSystemUploadService.cs` — Replace stub with ZIP implementation:

```csharp
public async Task<UploadIntentId> RequestUploadAsync(UploadRequest request, CancellationToken ct)
{
    var id = new UploadIntentId(Guid.NewGuid().ToString("N"));
    _statuses[id] = UploadStatus.InProgress;
    
    var targetDir = Path.Combine(_fakeNasRoot, request.NodeId.Value);
    Directory.CreateDirectory(targetDir);
    var targetZipPath = Path.Combine(targetDir, $"{request.Interval.Value}.zip");
    
    try
    {
        await Task.Run(() =>
        {
            if (File.Exists(targetZipPath)) File.Delete(targetZipPath);
            
            using var zipFs = File.Create(targetZipPath);
            using var archive = new ZipArchive(zipFs, ZipArchiveMode.Create);
            
            foreach (var file in request.Files)
            {
                if (!File.Exists(file.Path)) continue;
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
    }
    catch (Exception ex)
    {
        _statuses[id] = UploadStatus.Failed;
        _logger.LogError(ex, "Mock upload failed");
    }
    
    return id;
}
```

Need `using System.IO.Compression;` — add to `Directory.Packages.props` if `System.IO.Compression` is not already available (it's part of the BCL in .NET 8, no extra package needed).

**New test files:**

`tests/Tracer.Tests.Unit/Mock/InProcessChannelTransportTests.cs` — 3 test methods from SC6:
1. `InProcessChannelTransport_CapacityOne_SecondWriteDropsOldest`
2. `InProcessChannelTransport_Complete_ReadAsyncCompletes`
3. `InProcessChannelTransport_GetHealth_ReflectsDrops`

`tests/Tracer.Tests.Unit/Mock/LocalFileSystemUploadServiceTests.cs` — 4 test methods from SC7:
1. `LocalFileSystemUploadService_Upload_CreatesZipAtExpectedPath`
2. `LocalFileSystemUploadService_Upload_ZipContainsAllFiles`
3. `LocalFileSystemUploadService_Upload_Idempotent`
4. `LocalFileSystemUploadService_GetStatus_UnknownId_ReturnsUnknown`

For upload tests, create dummy files to pass as `FileToUpload` entries; verify the resulting ZIP with `ZipFile.OpenRead`.

---

### TRC-P2-009 — FakeNode

**Reference:** `docs/TASK-DETAIL.md#trc-p2-009` for exact success conditions (SC1-SC8), `docs/tracer_phase2_design.md §9`.

**Project to create: `src/Tracer.FakeNode/Tracer.FakeNode.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <AssemblyName>tracer-fakenode</AssemblyName>
    <RootNamespace>Tracer.FakeNode</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Tracer.Core\Tracer.Core.csproj" />
    <ProjectReference Include="..\Tracer.Agent\Tracer.Agent.csproj" />
    <ProjectReference Include="..\Tracer.Adapters.Mock\Tracer.Adapters.Mock.csproj" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Hosting" />
    <PackageReference Include="Microsoft.Extensions.Hosting.WindowsServices" />
    <PackageReference Include="Serilog" />
    <PackageReference Include="Serilog.Extensions.Hosting" />
    <PackageReference Include="Serilog.Sinks.Console" />
    <PackageReference Include="Serilog.Sinks.File" />
    <PackageReference Include="Serilog.Formatting.Compact" />
  </ItemGroup>
</Project>
```

Add to `Tracer.sln`.

**Files to create:**

`src/Tracer.FakeNode/Configuration/FakeNodeConfig.cs`
```csharp
namespace Tracer.FakeNode.Configuration;

public sealed record FakeNodeConfig
{
    public required string ScenarioName { get; init; }
    public required ScenarioConfig ScenarioConfig { get; init; }
    public required AgentConfig AgentConfig { get; init; }
}
```

`src/Tracer.FakeNode/Configuration/FakeNodeConfigLoader.cs`
```csharp
// Reads --config <path> from args; absolute path required (relative throws ArgumentException)
// Reads JSON under "FakeNode" key using System.Text.Json
// Returns FakeNodeConfig
```

`src/Tracer.FakeNode/FakeNodeOrchestrator.cs` — `BackgroundService` per §9.3:
- `ExecuteAsync` iterates `MockDataSource.ReadAsync(stoppingToken)` and calls `_transport.WriteAsync` for each record
- When the sequence completes (scenario done), calls `_transport.Complete()`
- `OperationCanceledException` from stoppingToken is caught without re-throw

`src/Tracer.FakeNode/Program.cs` — per §9.2:
- Writes `LOG_FILE=<path>` to stdout as FIRST output
- Registers full agent services using same pattern as `AgentHostBuilder`
- Registers `FakeNodeOrchestrator` as a second `IHostedService`
- Returns 0 on clean exit, 1 on exception

**Key notes:**
- SC5: `dotnet build Tracer.FakeNode --configuration Release` must pass zero warnings
- SC6 (acceptance smoke test): There is NO automated test for this — it's manual verification. However, the FakeNode integration test in BATCH-05 covers this.
- The `FakeNodeConfig` uses `ScenarioConfig` from `Tracer.Adapters.Mock.Scenarios`; add the using
- `Program.cs` does NOT call `AgentHostBuilder.Build` — it builds its own host with `FakeNodeOrchestrator` added

**Add TracerVersion helper if missing:**
- If `TracerVersion.Current` is referenced in agent code, ensure it's defined somewhere (e.g., `src/Tracer.Agent/TracerVersion.cs` returning `"2.0.0-dev"`)

---

## Cross-Cutting Requirements

1. **All 97 existing tests must still pass.** Do not break anything.
2. **Zero warnings, zero errors** on `dotnet build Tracer.sln --configuration Release`.
3. **`Tracer.FakeNode.csproj` must be added to `Tracer.sln`.**
4. **No new external packages** beyond what's already in `Directory.Packages.props`. `System.IO.Compression` is BCL — no package reference needed.
5. **CA1062 null guards** on all new public methods with reference parameters.

---

## Corrective Tasks (BATCH-03 P2 Debt)

Fix the following items from `DEBT-TRACKER.md` (DT-006, DT-007, DT-008):

**DT-006**: Add to `tests/Tracer.Tests.Unit/Agent/IntervalSchedulerTests.cs`:
```csharp
[Fact]
public void IntervalScheduler_LessThanOneMinute_Throws()
{
    var config = new AgentConfig { ..., IntervalDuration = TimeSpan.FromSeconds(30) };
    var act = () => new IntervalScheduler(clock, config);
    act.Should().Throw<ArgumentOutOfRangeException>();
}

[Fact]
public void IntervalScheduler_TimeUntilNextBoundary_DecreasesAsClockAdvances()
{
    // Create a MutableFakeClock or use two separate clock instances
    // First measurement: clock at 14:30; second: clock at 14:45
    // Second TimeUntilNextBoundary should be smaller than first
}
```

**DT-007**: Add to `tests/Tracer.Tests.Unit/Agent/IntervalRotatorTests.cs`:
```csharp
[Fact]
public async Task IntervalRotator_NotifyCaptureGap_AccumulatesInManifest()
{
    var rotator = BuildRotator();
    await rotator.OpenCurrentAsync(CancellationToken.None);
    
    rotator.NotifyCaptureGap(new CaptureGap
    {
        StartUtc = WallclockTime.Zero,
        EndUtc = WallclockTime.Zero,
        Reason = CaptureGapReason.TransportDisconnected,
        DroppedRecordCount = 5,
    });
    
    await rotator.RotateAsync(ManifestFinalizationReason.GracefulShutdown, CancellationToken.None);
    
    var manifestPath = Directory.GetFiles(_tempDir, "manifest.json", SearchOption.AllDirectories)
        .Should().ContainSingle().Which;
    var manifest = await ManifestWriter.ReadAsync(manifestPath, CancellationToken.None);
    manifest!.CaptureGaps.Should().ContainSingle()
        .Which.Reason.Should().Be(CaptureGapReason.TransportDisconnected);
}
```

**DT-008**: Add to `tests/Tracer.Tests.Unit/Agent/RecordRouterTests.cs`:
```csharp
[Fact]
public async Task RecordRouter_AfterWrite_NotifiesIntervalContext()
{
    var fakeContext = new FakeIntervalContext();
    var fakeWriter = new FakeWriter();
    var router = new RecordRouter(fakeContext, ...); // adapt to ctor
    var ev = MakeEvent();
    
    await router.RouteAsync(ev, fakeWriter, CancellationToken.None);
    
    fakeContext.NotifiedRecords.Should().ContainSingle().Which.Should().BeSameAs(ev);
}
```

Mark DT-006, DT-007, DT-008 as ✅ Resolved in DEBT-TRACKER.md after fixing.

---

## Developer Report Requirements

Report must include:
- Status table for all 4 tasks
- Test counts: unit tests passing and integration tests (still 10)
- Developer Insights Q1–Q5 (all 5 questions answered)
- Suggested commit message

Write report to: `d:\Work\Tracer\.dev\tracer\reports\BATCH-04-REPORT.md`

---

## Definition of Done

- `dotnet build Tracer.sln --configuration Release` exits 0, zero warnings
- `dotnet test tests\Tracer.Tests.Unit --configuration Release` exits 0
- `dotnet test tests\Tracer.Tests.Integration --configuration Release` exits 0
- New test classes: `StartupRecoveryTests`, `RetentionManagerTests`, `UploadIntentDispatcherTests`, `InProcessChannelTransportTests`, `LocalFileSystemUploadServiceTests`
- DT-006, DT-007, DT-008 fixed
- `StartupRecoveryService` fully implemented (not a stub)
- `RetentionManager` fully implemented (not a stub)
- `LocalFileSystemUploadService` creates ZIP archives
- `Tracer.FakeNode` project builds and registers all services
