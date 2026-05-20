# BATCH-03 Instructions

**Batch:** BATCH-03  
**Phase:** Phase 2 — TracerAgent, Interval Rotation, Fast State, FakeNode (Part 1 of 3)  
**Tasks:** TRC-P2-001, TRC-P2-002, TRC-P2-003, TRC-P2-004, TRC-P2-005  
**Previous review:** `.dev/tracer/reviews/BATCH-02-REVIEW.md`  
**Design reference:** `docs/tracer_phase2_design.md` (§2–§6)  
**Task detail reference:** `docs/TASK-DETAIL.md` (TRC-P2-001 through TRC-P2-005)

---

## Context

Phase 1 is complete (commit `5792f9e`). The solution has:
- `Tracer.Core` — pure domain types + interfaces
- `Tracer.Storage.DuckDB` — DuckDB write + read path
- `Tracer.Adapters.Mock` — deterministic scenario engine
- `Tracer.TestHarness` — integration test scaffolding
- 47 unit tests + 10 integration tests, all passing

This batch adds the foundational layer for Phase 2: new core abstractions, Parquet fast-state storage, and the TracerAgent process with its ingestion pipeline and interval rotation lifecycle.

---

## NuGet Packages to Add

Before implementing, add these to `Directory.Packages.props`:

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

---

## Project Files to Create

### `src/Tracer.Agent/Tracer.Agent.csproj`

New executable project:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <AssemblyName>tracer-agent</AssemblyName>
    <RootNamespace>Tracer.Agent</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Tracer.Core\Tracer.Core.csproj" />
    <ProjectReference Include="..\Tracer.Storage.DuckDB\Tracer.Storage.DuckDB.csproj" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Hosting" />
    <PackageReference Include="Microsoft.Extensions.Hosting.WindowsServices" />
    <PackageReference Include="Microsoft.Extensions.Configuration" />
    <PackageReference Include="Microsoft.Extensions.Configuration.Json" />
    <PackageReference Include="Microsoft.Extensions.Options" />
    <PackageReference Include="Microsoft.Extensions.Options.DataAnnotations" />
    <PackageReference Include="Serilog" />
    <PackageReference Include="Serilog.Extensions.Hosting" />
    <PackageReference Include="Serilog.Sinks.Console" />
    <PackageReference Include="Serilog.Sinks.File" />
    <PackageReference Include="Serilog.Formatting.Compact" />
  </ItemGroup>
</Project>
```

Add to `Tracer.sln`.

---

## Tasks

### TRC-P2-001 — New Core Abstractions in `Tracer.Core`

**Reference:** `docs/TASK-DETAIL.md#trc-p2-001` for exact success conditions (SC1-SC9), `docs/tracer_phase2_design.md §3` for full type definitions.

**Files to create in `src/Tracer.Core/`:**

- `Abstractions/IAgentTransport.cs` — `IAgentTransport` interface + `TransportHealth` sealed record (§3.1)
- `Abstractions/ITelemetryUploadService.cs` — `ITelemetryUploadService` interface + `UploadRequest`, `FileToUpload`, `UploadIntentId`, `UploadStatus` (§3.2)
- `Domain/IntervalTimestamp.cs` — `readonly record struct`, format `YYYYMMDDTHHMMSSZ` (§3.3)
- `Domain/CaptureGap.cs` — `sealed record` + `CaptureGapReason` enum (§3.3)
- `Domain/IntervalManifest.cs` — `sealed record` + `ManifestFinalizationReason`, `SessionMarker`, `SessionMarkerType` (§3.3)

**Files to update:**

- `src/Tracer.Core/Abstractions/IDiagnosticStorageWriter.cs` — add `Task AppendFastStateAsync(StateSampleRecord record, CancellationToken ct);` as fifth method
- `src/Tracer.Storage.DuckDB/DuckDbStorageWriter.cs` — add `AppendFastStateAsync` implementation that throws `NotSupportedException("AppendFastStateAsync not yet implemented; use DuckDbStorageWriter from Phase 2")`

**Key implementation notes:**

- `IntervalTimestamp` must have a `TryParse(string, out IntervalTimestamp)` static method in addition to the constructor
- `IntervalTimestamp.FromUtc(DateTimeOffset)` throws `ArgumentException` when `Offset != TimeSpan.Zero`
- Format is exactly 16 chars: `yyyyMMddTHHmmssZ` (capital T, capital Z, always UTC)
- `WallclockTime.Zero` must exist (or `default` must work) for `TransportHealth.LastReceivedAt`; add `public static readonly WallclockTime Zero = new(0);` if missing
- Use `System.Globalization.CultureInfo.InvariantCulture` and `DateTimeStyles.AssumeUniversal` in TryParse

**New test file:**

`tests/Tracer.Tests.Unit/Core/IntervalTimestampTests.cs` — all 6 test methods from SC8 plus `CaptureGap_CanBeConstructedWithAllReasons`.

---

### TRC-P2-002 — Fast-State Parquet Writers

**Reference:** `docs/TASK-DETAIL.md#trc-p2-002` for exact success conditions (SC1-SC10), `docs/tracer_phase2_design.md §4` for architecture.

**Files to create in `src/Tracer.Storage.DuckDB/Parquet/`:**

- `ParquetTopicSchema.cs` — `ParquetTopicSchema` sealed record, `ParquetColumn` sealed record, `ParquetType` enum
- `ParquetSchemas.cs` — static `ParquetSchemaBuilder.BuildSchema(ParquetTopicSchema)` that builds a `Parquet.Net` `Schema` object
- `ColumnExtractor.cs` — `ColumnExtractor.ExtractRow(StateSampleRecord, ParquetTopicSchema)` using `System.Text.Json.JsonDocument` for JSON-path extraction
- `FastStateParquetWriter.cs` — factory + append + flush + dispose (see §4.3)
- `WellKnownTopicSchemas.cs` — static class with `Transforms` property and `ToDictionary()` helper
- `NullFastStateWriter.cs` — singleton that silently drops all samples

**Add `Parquet.Net` to `Tracer.Storage.DuckDB.csproj`:**
```xml
<PackageReference Include="Parquet.Net" />
```

**Update `DuckDbStorageWriter`:**
- Change constructor signature to accept `IReadOnlyDictionary<string, ParquetTopicSchema> fastStateSchemas` and a directory path (or derive `fast_state/` from the existing interval path)
- Implement `AppendFastStateAsync` using `GetOrCreateFastStateWriterAsync` per §4.4
- `DisposeAsync` must await all `FastStateParquetWriter` instances

**Parquet.Net API notes (version 4.x):**
- `ParquetSchema` constructed from `DataField[]` (e.g., `new DataField<float>("pos_x")`)
- `ParquetWriter.CreateAsync(ParquetSchema, Stream)` returns `ParquetWriter`
- Create row group with `writer.CreateRowGroup()` → `ParquetRowGroupWriter`
- Write column: `rowGroupWriter.WriteColumnAsync(new DataColumn(field, valuesArray))`
- `ParquetWriter` implements `IDisposable`; dispose writes the footer
- For `TimestampNs` columns use `DataField<DateTimeOffset>` — Parquet.Net 4.x represents timestamps as `DateTimeOffset`
- Standard columns order: `publish_wallclock`, `receive_wallclock`, `publisher_node`, `instance_key`, `sequence_number`, then schema columns

**JSON path extraction:**
- Simple JSON paths like `$.position.x` — split on `.`, skip `$`, navigate properties
- Missing paths → zero value for numeric, `null` for nullable, empty string for string
- Do NOT use any external JSON path library; implement inline with `System.Text.Json`

**New test file:**

`tests/Tracer.Tests.Unit/Storage/FastStateParquetWriterTests.cs` — all 5 test methods from SC9.

---

### TRC-P2-003 — Agent Configuration & DI

**Reference:** `docs/TASK-DETAIL.md#trc-p2-003` for exact success conditions (SC1-SC9), `docs/tracer_phase2_design.md §5` and §8.

**Files to create in `src/Tracer.Agent/`:**

- `Program.cs` — entrypoint per §5.2 (LOG_FILE first stdout line, exit codes)
- `AgentHostBuilder.cs` — per §5.3 (Build method, config resolution, DI wiring)
- `Configuration/AgentConfig.cs` — per §5.4 (all properties, defaults, nested configs)
- `Configuration/AgentConfigLoader.cs` — static helper (optional; may be inlined in AgentHostBuilder)
- `Configuration/ConfigValidation.cs` — static `Validate(AgentConfig)` per SC2
- `Lifecycle/AgentHostedService.cs` — `BackgroundService` per §6.6 (STUB: just calls recovery, opens interval, starts ingestion loop; full implementation in TRC-P2-005)
- `Lifecycle/IntervalScheduler.cs` — STUB (full implementation in TRC-P2-005)
- `Lifecycle/IntervalRotator.cs` — STUB (full implementation in TRC-P2-005)
- `Lifecycle/StartupRecoveryService.cs` — STUB (full implementation in TRC-P2-006)
- `Ingestion/IngestionPipeline.cs` — STUB (full implementation in TRC-P2-004)
- `Ingestion/BackpressureMonitor.cs` — STUB (full implementation in TRC-P2-004)
- `Ingestion/DropPolicy.cs` — STUB (full implementation in TRC-P2-004)
- `Storage/IntervalDirectory.cs` — full implementation per §6.2
- `Storage/ManifestWriter.cs` — STUB (`WriteAsync` writes JSON to file; full in TRC-P2-005)
- `Storage/RetentionManager.cs` — STUB (full implementation in TRC-P2-007)
- `Upload/UploadIntentDispatcher.cs` — STUB (full implementation in TRC-P2-007)
- `Diagnostics/AgentStateReporter.cs` — minimal (reports open interval info for tests)

**Files to create in `src/Tracer.Adapters.Mock/`:**

- `Transport/InProcessChannelTransport.cs` — per §8.1 (full implementation; see also TRC-P2-008 for completion)
- `Upload/LocalFileSystemUploadService.cs` — per §8.2 (initial version; ZIP archive behavior added in TRC-P2-008)

**STUB strategy:**
- Stubs must compile and satisfy DI resolution (SC3 of TRC-P2-003)
- Each stub should implement `CreateAsync` or constructor as needed; methods may throw `NotImplementedException` or return default for now
- `IntervalRotator` stub: `CurrentWriter` returns null; `OpenCurrentAsync`/`RotateAsync`/`DisposeAsync` are no-ops
- `StartupRecoveryService` stub: `RecoverAsync` returns `Task.CompletedTask`
- `IngestionPipeline` stub: `RunAsync` awaits `ct.WhenCanceled()` (i.e., loops until cancelled)
- `RetentionManager` stub: `ApplyAsync` returns `Task.CompletedTask`
- `UploadIntentDispatcher` stub: `DispatchAsync` returns `Task.CompletedTask`

**DI wiring in AgentHostBuilder:**
- All stubs must be registered so `Build()` can resolve them (SC3)
- `InProcessChannelTransport` registered as both `IAgentTransport` and itself
- `LocalFileSystemUploadService` registered as `ITelemetryUploadService`
- Fast state schemas: `WellKnownTopicSchemas.ToDictionary()` as `IReadOnlyDictionary<string, ParquetTopicSchema>`

**Important:**
- `AgentHostBuilder.Build(string[])` must NOT throw when the config file is valid (SC3 test calls `Build` + resolves services)
- For tests, a helper `AgentHostBuilder.BuildForTest(AgentConfig)` or overload that accepts a pre-built config and uses `AddInMemoryCollection` is useful

**New test files:**

`tests/Tracer.Tests.Unit/Agent/AgentConfigTests.cs` — all 6 test methods from SC8.

---

### TRC-P2-004 — Ingestion Pipeline

**Reference:** `docs/TASK-DETAIL.md#trc-p2-004` for exact success conditions (SC1-SC9), `docs/tracer_phase2_design.md §6.4` and `§6.5`.

**Files to fully implement (replacing stubs from TRC-P2-003):**

- `src/Tracer.Agent/Ingestion/BackpressureMonitor.cs` — per §6.5 (reads `IAgentTransport.GetHealth().PendingCount`, compares to thresholds)
- `src/Tracer.Agent/Ingestion/DropPolicy.cs` — per §6.5 (ShouldDrop logic, out CaptureGapReason)
- `src/Tracer.Agent/Ingestion/RecordRouter.cs` — new file dispatching to writer methods + calling `IntervalRotator.NotifyRecordWritten`
- `src/Tracer.Agent/Ingestion/IngestionPipeline.cs` — per §6.4 (read from transport, check backpressure, drop or route, catch exceptions as gaps)

**Key implementation notes:**

- `BackpressureLevel` enum: `Healthy`, `FastStateAtRisk`, `SlowStateAtRisk`, `EventsAtRisk`, `Saturated` — define in `Tracer.Agent.Ingestion` namespace
- Threshold comparisons use `>=` (at-or-above, not strictly above)
- `DropPolicy` receives the `BackpressureLevel` from the monitor (not the count); it's a pure function
- `RecordRouter.RouteAsync(DiagnosticRecord record, IDiagnosticStorageWriter writer, CancellationToken ct)` — uses pattern matching on record type and `StateSampleRate`
- `IngestionPipeline.RunAsync` must catch any exception from the router (per-record) and convert it to a capture gap via `_rotator.NotifyCaptureGap`; `OperationCanceledException` from the token is NOT caught and re-thrown
- When `_rotator.CurrentWriter is null`, drop with `CaptureGapReason.TransportDisconnected`

**New test files:**

`tests/Tracer.Tests.Unit/Agent/DropPolicyTests.cs` — all 5 test methods from SC7.  
`tests/Tracer.Tests.Unit/Agent/RecordRouterTests.cs` — all 4 test methods from SC8.

**Notes on mocking `IDiagnosticStorageWriter` for router tests:**
- Create a simple `FakeWriter` internal class in the test file that implements `IDiagnosticStorageWriter` and records which methods were called; do NOT add external mocking libraries

---

### TRC-P2-005 — Interval Rotation Lifecycle

**Reference:** `docs/TASK-DETAIL.md#trc-p2-005` for exact success conditions (SC1-SC12), `docs/tracer_phase2_design.md §6.1–§6.3` and `§6.6`.

**Files to fully implement (replacing stubs from TRC-P2-003):**

- `src/Tracer.Agent/Lifecycle/IntervalScheduler.cs` — per §6.1 (full implementation with clock-aligned boundaries)
- `src/Tracer.Agent/Lifecycle/IntervalRotator.cs` — per §6.3 (full implementation with lock, stats tracking, session marker extraction)
- `src/Tracer.Agent/Lifecycle/AgentHostedService.cs` — per §6.6 (full implementation: recovery → open → ingestion + retention + rotation loops → graceful shutdown)
- `src/Tracer.Agent/Storage/ManifestWriter.cs` — serialize `IntervalManifest` to indented UTF-8 JSON

**Important implementation details:**

**IntervalScheduler:**
- `AlignDown` uses integer division: `(utcNow.UtcTicks / durationTicks) * durationTicks`
- `NextIntervalBoundary()` must return a `WallclockTime`, not `DateTimeOffset`; use `WallclockTime.FromDateTimeOffset(nextDt)`
- The constructor `ArgumentOutOfRangeException` is for duration range, `ArgumentException` for divisibility
- Accept `IClock` via constructor injection

**IntervalRotator:**
- `_rotationLock = new SemaphoreSlim(1, 1)` 
- On `RotateAsync`: (1) flush+dispose current writer, (2) write manifest, (3) write `_ready`, (4) dispatch upload, (5) open next
- `OpenInternalAsync` creates a new `DuckDbStorageWriter` using `DuckDbStorageWriter.CreateAsync(directory.RootPath, _fastStateSchemas, _logger, ct)` — note: `DuckDbStorageWriter.CreateAsync` signature must now accept the directory path and schemas (updated from Phase 1 single-file path)
- Stats counters (`_eventCountInCurrent`, etc.) must be reset in `OpenInternalAsync`
- `NotifyRecordWritten` and `NotifyCaptureGap` are NOT thread-safe (only called from the single-threaded `IngestionPipeline`)
- `DisposeAsync` must call `RotateAsync(GracefulShutdown, CancellationToken.None)` if a writer is currently open

**DuckDbStorageWriter signature update:**
- Change `CreateAsync` from `(string dbPath, ...)` to `(string intervalDirectory, IReadOnlyDictionary<string, ParquetTopicSchema> fastStateSchemas, ...)`
- The DuckDB files live at `Path.Combine(intervalDirectory, "events.duckdb")` and `Path.Combine(intervalDirectory, "slow_state.duckdb")`
- `fast_state/` subdirectory is `Path.Combine(intervalDirectory, "fast_state")`
- This is a breaking change to `DuckDbStorageWriter.CreateAsync`; update call sites: `TracerStackFixture.CreateAsync` passes the parent directory, not a specific file path
- WARNING: Phase 1 integration tests construct the writer via `TracerStackFixture`; update `TracerStackFixture` to match the new signature (pass temp dir, not specific .duckdb path)

**ManifestWriter (System.Text.Json):**
- Use `JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }`
- `IntervalTimestamp` needs a custom `JsonConverter<IntervalTimestamp>` that reads/writes the `Value` string
- `WallclockTime` needs a `JsonConverter<WallclockTime>` that serializes as ISO 8601 with ns precision (use `NanosecondsSinceEpoch` as a long, or convert to `DateTimeOffset` and format)
- `AgentId`, `TraceId`, `EventId` need converters too if they appear in manifests
- Register converters on `JsonSerializerOptions` used by `ManifestWriter`

**AgentHostedService:**
- `ExecuteAsync` starts ingestion and retention as `Task.Run` sub-tasks
- The rotation loop (`RotationLoopAsync`) runs on the main task body
- On cancellation, all loops must drain within 5 seconds; the `Task.WhenAll` wait should use a timeout
- The final `RotateAsync(GracefulShutdown, CancellationToken.None)` is called AFTER the ingestion loop exits

**New test files:**

`tests/Tracer.Tests.Unit/Agent/IntervalSchedulerTests.cs` — all 6 test methods from SC10 (TRC-P2-005) and SC1 (TRC-P2-011).  
`tests/Tracer.Tests.Unit/Agent/ManifestWriterTests.cs` — all 3 test methods from SC11 (TRC-P2-005) and SC5 (TRC-P2-011).  
`tests/Tracer.Tests.Unit/Agent/IntervalRotatorTests.cs` — all 7 test methods from SC2 of TRC-P2-011.

**Notes on IntervalRotatorTests:**
- Use temp directories; the real `DuckDbStorageWriter` is acceptable but makes tests slow; alternatively use a `FakeStorageWriter` that records calls
- For `RotateAsync_DispatchesUpload`, inject a `FakeUploadIntentDispatcher` that records calls
- For `DisposeAsync_TriggersGracefulShutdownRotation`, check manifest `FinalizationReason`

---

## Additional Cross-Cutting Requirements

1. **`Tracer.Core.csproj` must still have zero third-party packages.** All new types (`IntervalTimestamp`, `CaptureGap`, etc.) in Core use only BCL types.

2. **All Phase 1 tests must still pass.** The `DuckDbStorageWriter.CreateAsync` signature change breaks `TracerStackFixture`. Update the fixture to pass `fixture._tempDir` (the directory) instead of `fixture.DbPath` (the file). Keep `DbPath` pointing to `events.duckdb` inside that directory.

3. **Build must pass with zero warnings.** Pay special attention to:
   - `[Required]` attribute on `AgentConfig` properties needs `System.ComponentModel.DataAnnotations`
   - `CA1062` (null-check public args) applies to new public methods
   - No new `#pragma warning disable` allowed

4. **`Tracer.Agent.csproj` must be added to `Tracer.sln`.**

5. **Batch sizing:** This batch (TRC-P2-001 through TRC-P2-005) is the largest in the project to date. Implement incrementally — get each task compiling and its tests passing before moving to the next. The tasks have strict dependencies:
   - TRC-P2-001 first (adds to Core; everything depends on it)
   - TRC-P2-002 next (updates DuckDbStorageWriter; must not break Phase 1 tests)
   - TRC-P2-003 next (creates Agent project with stubs; DI must resolve)
   - TRC-P2-004 next (replaces ingestion stubs; adds DropPolicy and RecordRouter tests)
   - TRC-P2-005 last (replaces rotation stubs; most complex; adds scheduler + rotator + manifest tests)

---

## Developer Report Requirements

The report must include:

- Status table with all 5 tasks
- Test counts: unit tests passing (was 47; will grow) and integration tests still passing (still 10)
- **Developer Insights Q1–Q5:**
  - Q1: Issues encountered and how resolved (compiler blockers, Parquet.Net API quirks, DuckDbStorageWriter signature change)
  - Q2: Weak points in the codebase you'd improve
  - Q3: Design decisions made beyond the instructions
  - Q4: Edge cases discovered not mentioned in the spec
  - Q5: Performance concerns or optimization opportunities
- Suggested commit message in standard format

---

## Definition of Done

- `dotnet build Tracer.sln --configuration Release` exits 0, zero warnings
- `dotnet test tests\Tracer.Tests.Unit --configuration Release` exits 0
- `dotnet test tests\Tracer.Tests.Integration --configuration Release` exits 0 (Phase 1 tests still passing)
- New test classes exist and pass: `IntervalTimestampTests`, `FastStateParquetWriterTests`, `AgentConfigTests`, `DropPolicyTests`, `RecordRouterTests`, `IntervalSchedulerTests`, `ManifestWriterTests`, `IntervalRotatorTests`
- `IntervalRotator`, `IntervalScheduler`, `IngestionPipeline`, `BackpressureMonitor`, `DropPolicy`, `ManifestWriter` fully implemented (not stubs)
- `AgentHostedService` fully implemented per §6.6
- Phase 2 integration tests are not yet required (those come in BATCH-05)
