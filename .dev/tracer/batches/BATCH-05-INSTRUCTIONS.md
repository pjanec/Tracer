# BATCH-05 Instructions

## Goal

Complete Phase 2 by implementing TestHarness Phase 2 fixtures (TRC-P2-010), remaining agent unit tests (TRC-P2-011), and agent integration tests (TRC-P2-012).

## Tasks

### TRC-P2-010 — TestHarness Phase 2 Additions

**Files to create/modify:**

1. **`src/Tracer.TestHarness/Tracer.TestHarness.csproj`** — Add project references to `Tracer.Agent` and `Tracer.FakeNode`; add package reference to `Microsoft.Extensions.Hosting`.

2. **`src/Tracer.TestHarness/Agent/AgentFixtureOptions.cs`**
   ```csharp
   namespace Tracer.TestHarness;
   public sealed record AgentFixtureOptions
   {
       public bool UseSimulatedClock { get; init; } = false;
       public int TransportCapacity { get; init; } = 10_000;
       public int KeepLastNIntervals { get; init; } = 24;
   }
   ```

3. **`src/Tracer.TestHarness/Agent/TracerAgentFixture.cs`**
   - `sealed`, implements `IAsyncDisposable`
   - Constructor is private; use `CreateAsync(AgentFixtureOptions?, CancellationToken)` factory
   - Creates temp `DataRoot` + `LogsRoot` in `Path.GetTempPath()`
   - Builds `AgentConfig` from options
   - Uses `InProcessChannelTransport` as `IAgentTransport`
   - Uses `LocalFileSystemUploadService` as `ITelemetryUploadService` (staging dir inside DataRoot)
   - Builds `IHost` using `Host.CreateApplicationBuilder()` — registers all agent services manually (mirrors FakeNode Program.cs pattern)
   - When `UseSimulatedClock = true`, registers `SimulatedClock` as `IClock` instead of `SystemClock`; exposes `SimulatedClock? SimulatedClock`
   - Starts host via `_host.StartAsync(ct)`
   - `PushAsync(DiagnosticRecord, CancellationToken)` calls `_transport.WriteAsync`
   - `ForceRotationAsync(CancellationToken)` resolves `IntervalRotator` and calls `RotateAsync(ScheduledRotation, ct)` then reopens via `OpenCurrentAsync`
   - `AdvanceToNextBoundaryAsync(CancellationToken)` — only when `UseSimulatedClock = true`; advances `SimulatedClock` past next boundary and waits 500ms for rotation to trigger
   - Properties: `Transport` (`InProcessChannelTransport`), `UploadService` (`LocalFileSystemUploadService`), `DataRoot` (`string`), `SimulatedClock` (`SimulatedClock?`)
   - `StopAsync(CancellationToken)` calls `_transport.Complete()` then `_host.StopAsync(ct)`
   - `DisposeAsync`: calls `StopAsync`, then deletes temp dirs

4. **`src/Tracer.TestHarness/Agent/FakeNodeFixture.cs`**
   - `sealed`, implements `IAsyncDisposable`
   - `RunScenarioAsync(string scenarioName, ScenarioConfig config, AgentConfig agentConfig, CancellationToken ct)`:
     - Creates `FakeNodeConfig { ScenarioName, ScenarioConfig, AgentConfig }`
     - Builds `IHost` using same pattern as FakeNode `Program.cs` but without the `LOG_FILE` print and without `FakeNodeConfigLoader` (takes config directly)
     - Starts host and awaits until `FakeNodeOrchestrator` completes (the orchestrator calls `transport.Complete()` when scenario finishes)
     - Waits for host to stop
   - `IReadOnlyList<IntervalManifest> Manifests` — parses all `manifest.json` files under `agentConfig.DataRoot/intervals/`
   - `IReadOnlyList<string> IntervalZipPaths` — finds all `.zip` files under `agentConfig.UploadService.LocalFileSystemRoot`
   - `DisposeAsync` — deletes `agentConfig.DataRoot` and `agentConfig.UploadService.LocalFileSystemRoot`

5. **`src/Tracer.TestHarness/ClockControl/TestableIntervalScheduler.cs`**
   - Not strictly needed for TRC-P2-012 tests; skip and add to DT tracker

### TRC-P2-011 — Agent Unit Tests (remaining)

**Files to modify:**

1. **`tests/Tracer.Tests.Unit/Agent/IntervalSchedulerTests.cs`** — Add `IntervalDuration_24Hours_DoesNotThrow`.

2. **`tests/Tracer.Tests.Unit/Agent/IntervalRotatorTests.cs`** — Add:
   - `IntervalRotator_RotateAsync_DispatchesUpload` — mock upload service captures call count; rotate; assert CapturingUploadService.Requests.Count == 1
   - `IntervalRotator_DisposeAsync_TriggersGracefulShutdownRotation` — open interval, dispose rotator; manifest exists with `FinalizationReason == GracefulShutdown`

3. **`tests/Tracer.Tests.Unit/Agent/ManifestWriterTests.cs`** — Add:
   - `ManifestWriter_WallclockTimes_SerializeAsIso8601` — write manifest with non-zero `FinalizedAt`; read raw JSON; assert JSON contains ISO 8601 string for `finalized_at`
   - `ManifestWriter_EmptyGapsAndMarkers_SerializesEmptyArrays` — write manifest with empty collections; raw JSON contains `[]` for both

### TRC-P2-012 — Agent Integration Tests

**Files to add to `tests/Tracer.Tests.Integration/`:**

1. Update `Tracer.Tests.Integration.csproj` to add `Tracer.TestHarness` reference (check if already present).

2. **`tests/Tracer.Tests.Integration/AgentIntervalLifecycleTests.cs`**:
   - `ThreeIntervals_ThreeReadyDirectories` — push 10 events, ForceRotation × 3; count ready dirs == 3
   - `RecordCounts_MatchPushed` — push 200 events, rotate; read DuckDB; CountEventsAsync == 200
   - `UploadServiceReceivesEachInterval` — 3 rotations; upload service has 3 requests
   - `NoDataLoss_HealthyConditions` — push 500 events; rotate once; DuckDB has 500 events

3. **`tests/Tracer.Tests.Integration/AgentRecoveryTests.cs`**:
   - `OrphanedInterval_FinalizedOnRestart` — create orphan dir manually, start fixture; orphan becomes ready
   - `RecoveredManifest_HasCrashReason` — read recovered manifest; FinalizationReason == RecoveryAfterCrash
   - `AfterRecovery_NewIntervalAcceptsRecords` — after recovery, push 50 events + rotate; EventCount == 50

4. **`tests/Tracer.Tests.Integration/FakeNodeEndToEndTests.cs`**:
   - `CalmScenario_ProducesIntervals` — `FakeNodeFixture` with 15-min intervals; manifests.Count >= 1; no crash reason
   - `AllIntervalsUploaded` — zip count == ready dir count
   - `GracefulShutdown_LastInterval_HasGracefulReason` — last manifest has GracefulShutdown

## Success Criteria

- 0 errors, 0 warnings
- All existing 119 tests continue to pass
- At least 20 new tests added
- BATCH-05 report written and reviewed
