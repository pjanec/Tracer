# BATCH-53 Report — Phase 11 Part A: Real Adapter Assemblies

**Status:** COMPLETE  
**Date:** 2026-05-22

---

## Files Created

### TRC-P11-001: `Tracer.Adapters.DDS`

| File | Description |
|------|-------------|
| `src/Tracer.Adapters.DDS/Tracer.Adapters.DDS.csproj` | Project file; references `CycloneDDS.NET`, `Microsoft.Extensions.Logging.Abstractions`, `Tracer.Core` |
| `src/Tracer.Adapters.DDS/IDdsSample.cs` | Abstraction over a DDS sample — isolates Tracer.Core from CycloneDDS types; exposes `SourceTimestamp`, `SequenceNumber`, `GetPayload()` |
| `src/Tracer.Adapters.DDS/DdsTopicKind.cs` | Enum: `Event`, `SlowState`, `FastState` |
| `src/Tracer.Adapters.DDS/DdsTopicMetadata.cs` | Configuration record for a DDS topic subscription; holds topic name, sample CLR type, kind, and field name mappings |
| `src/Tracer.Adapters.DDS/DdsTopicRegistry.cs` | Dictionary-backed catalog of `DdsTopicMetadata`; populated from config at startup via `Lookup(topicName)` and `All` |
| `src/Tracer.Adapters.DDS/TraceContext.cs` | Value record carrying `TraceId`, `EventId`, `ParentEventId` extracted from a DDS sample |
| `src/Tracer.Adapters.DDS/IDdsSubscriberFactory.cs` | Factory interface: `Create(participant, metadata)` returning `IDisposable` DDS reader |
| `src/Tracer.Adapters.DDS/DdsSubscriberFactory.cs` | Production factory — creates `DdsReader<T>` via `CycloneDDS.Runtime` API |
| `src/Tracer.Adapters.DDS/DdsSampleTranslator.cs` | Translates raw DDS samples to `EventRecord` / `StateSampleRecord` using reflection-based field extraction |
| `src/Tracer.Adapters.DDS/DdsTraceContextExtractor.cs` | Extracts `TraceContext` from a DDS sample using reflection on the payload object |
| `src/Tracer.Adapters.DDS/DdsDiagnosticDataSource.cs` | `IDiagnosticDataSource` implementation; creates one subscription per registered topic and polls DDS readers in a loop |
| `src/Tracer.Adapters.DDS/Configuration/DdsAdapterConfig.cs` | Config POCO: participant domain ID and list of topic configurations |

### TRC-P11-002: `Tracer.Adapters.SharedMemory`

| File | Description |
|------|-------------|
| `src/Tracer.Adapters.SharedMemory/Tracer.Adapters.SharedMemory.csproj` | Project file; references `Microsoft.Extensions.Logging.Abstractions`, `Tracer.Core`; marked Windows-only via `SupportedOSPlatform` |
| `src/Tracer.Adapters.SharedMemory/AssemblyInfo.cs` | `[assembly: SupportedOSPlatform("windows")]` — suppresses CA1416 for all Windows-only MMF/Semaphore APIs |
| `src/Tracer.Adapters.SharedMemory/SharedMemoryRingBuffer.cs` | Single-producer/single-consumer ring buffer over a named Windows `MemoryMappedFile`; drop-oldest backpressure; `Create` (producer) / `Open` (consumer) factory methods; unsafe pointer access for performance |
| `src/Tracer.Adapters.SharedMemory/SharedMemoryDiagnosticRecordCodec.cs` | Encodes/decodes `DiagnosticRecord` to/from UTF-8 JSON bytes using source-generated `System.Text.Json` serialization; supports `EventRecord` and `StateSampleRecord` via kind discriminator |
| `src/Tracer.Adapters.SharedMemory/SharedMemoryReader.cs` | Consumer-side helper: opens the ring buffer and named semaphore; `ReadAvailable()` drains without blocking; `WaitAndRead(timeout)` waits on semaphore then drains |
| `src/Tracer.Adapters.SharedMemory/SharedMemoryWriter.cs` | Producer-side helper: creates ring buffer and named semaphore; `Write(record)` encodes, writes to ring, and releases semaphore |
| `src/Tracer.Adapters.SharedMemory/SharedMemoryTransport.cs` | `IAgentTransport` implementation; `ReadAsync` loop calls `WaitAndRead` via `Task.Run` and yields records; respects cancellation |
| `src/Tracer.Adapters.SharedMemory/Configuration/SharedMemoryConfig.cs` | Config POCO: `SharedMemoryName`, `SemaphoreName`, `CapacityBytes` |

### TRC-P11-003: `Tracer.Adapters.Sync`

| File | Description |
|------|-------------|
| `src/Tracer.Adapters.Sync/Tracer.Adapters.Sync.csproj` | Project file; references `Microsoft.Extensions.Http`, `Microsoft.Extensions.Logging.Abstractions`, `Tracer.Core` |
| `src/Tracer.Adapters.Sync/SyncMasterRestClient.cs` | Typed HTTP client for the sync-master REST API; `RegisterUploadIntentAsync`, `GetIntentStatusAsync`; deserialises JSON responses |
| `src/Tracer.Adapters.Sync/SyncSystemUploadService.cs` | `ITelemetryUploadService` implementation; registers intent, polls until complete/failed, calls `RequestUploadAsync` on the underlying upload delegate |
| `src/Tracer.Adapters.Sync/Configuration/SyncAdapterConfig.cs` | Config POCO: sync-master base URL, polling interval, timeout |

### TRC-P11-004: `Tracer.Adapters.Nas`

| File | Description |
|------|-------------|
| `src/Tracer.Adapters.Nas/Tracer.Adapters.Nas.csproj` | Project file; references `Microsoft.Extensions.Logging.Abstractions`, `Tracer.Core` |
| `src/Tracer.Adapters.Nas/SmbPathResolver.cs` | Resolves NAS paths following the `{NasRoot}/telemetry/{nodeId}/{ts}.zip` layout; `ResolveAll` enumerates available zip archives |
| `src/Tracer.Adapters.Nas/StagedInterval.cs` | Wraps a zip archive path with its parsed `IntervalDescriptor`; implements `IDisposable` |
| `src/Tracer.Adapters.Nas/NasStorageReader.cs` | `ITelemetryStorageReader` implementation; enumerates NAS archives via `SmbPathResolver`, opens each zip, extracts JSON records, and deserialises to `DiagnosticRecord` |
| `src/Tracer.Adapters.Nas/Configuration/NasAdapterConfig.cs` | Config POCO: NAS root path, node ID filter, archive filename pattern |

### Test Files

| File | Tests | Description |
|------|-------|-------------|
| `tests/Tracer.Tests.Unit/Adapters/DDS/DdsSampleTranslatorTests.cs` | 11 | Translate event/state samples; null/missing field handling |
| `tests/Tracer.Tests.Unit/Adapters/DDS/DdsTraceContextExtractorTests.cs` | 4 | Extract trace context; fallback to `TraceContext.Empty` |
| `tests/Tracer.Tests.Unit/Adapters/DDS/DdsDiagnosticDataSourceTests.cs` | 2 | Registry lookup and no-op data source smoke tests |
| `tests/Tracer.Tests.Unit/Adapters/SharedMemory/SharedMemoryRingBufferTests.cs` | 6 | Create/Open; TryWrite/TryRead round-trip; wrap-around; drop-oldest; empty-buffer null return |
| `tests/Tracer.Tests.Unit/Adapters/SharedMemory/SharedMemoryDiagnosticRecordCodecTests.cs` | 4 | Encode/decode `EventRecord` and `StateSampleRecord`; unknown type throws |
| `tests/Tracer.Tests.Unit/Adapters/SharedMemory/SharedMemoryTransportTests.cs` | 3 | Writer-to-transport round-trip; `GetHealth` returns correct capacity; immediate cancel does not throw |
| `tests/Tracer.Tests.Unit/Adapters/Sync/SyncMasterRestClientTests.cs` | 2 | `RegisterUploadIntentAsync` parses intent ID; `GetIntentStatusAsync` parses status |
| `tests/Tracer.Tests.Unit/Adapters/Sync/SyncSystemUploadServiceTests.cs` | 9 | Upload intent lifecycle; polling to complete/failed; timeout; cancellation |
| `tests/Tracer.Tests.Unit/Adapters/Nas/SmbPathResolverTests.cs` | 5 | Path construction; `ResolveAll` enumeration; node ID and timestamp filtering |
| `tests/Tracer.Tests.Unit/Adapters/Nas/NasStorageReaderTests.cs` | 9 | Reads records from zip archives; empty archive; missing archive; cancellation |
| `tests/Tracer.Tests.Unit/AssemblyInfo.cs` | — | `[assembly: SupportedOSPlatform("windows")]` — required for SharedMemory tests |

---

## Files Modified

| File | Change |
|------|--------|
| `Directory.Packages.props` | Added `CycloneDDS.NET 0.2.2` and `Microsoft.Extensions.Http 8.0.0` |
| `Tracer.sln` | Added all 4 new projects under the `src` solution folder |
| `tests/Tracer.Tests.Unit/Tracer.Tests.Unit.csproj` | Added `ProjectReference` entries for all 4 new adapters; added `<CycloneDdsDisableCodeGen>true</CycloneDdsDisableCodeGen>` |

---

## Build Results

```
dotnet build tests\Tracer.Tests.Unit -c Release
Build succeeded.
  0 Warning(s)
  0 Error(s)
```

All four adapter assemblies and the unit test project build cleanly with `TreatWarningsAsErrors=true`.

---

## Test Results

```
dotnet test tests\Tracer.Tests.Unit -c Release --no-build

Passed!  - Failed: 0, Passed: 234, Skipped: 0, Total: 234, Duration: 5 s
```

**New Phase 11 tests:** 55 (across 10 test files)  
**Pre-existing tests:** 179 — all still passing

---

## Deviations from Instructions

1. **`DdsTopicMetadata.EntityIdField` made nullable** — The instructions declared `EntityIdField` as `required string`. State topics (`SlowState`, `FastState`) do not have entity IDs, so the field was changed to `required string?`. `DdsSampleTranslator.ExtractStringField` was updated to accept `string? fieldName` and return `null` early when `fieldName` is `null`.

2. **CycloneDDS.NET namespace** — `DdsParticipant` and `DdsReader<T>` reside in the `CycloneDDS.Runtime` namespace (not the top-level `CycloneDDS` namespace). All `using` directives in DDS source and test files were adjusted accordingly.

3. **CycloneDDS code generation disabled in test project** — The CycloneDDS.NET package includes a build-time code generator (`CycloneDDS.CodeGen.dll`) that scans `.cs` files and fails with exit code 9020 in test projects. Fixed by adding `<CycloneDdsDisableCodeGen>true</CycloneDdsDisableCodeGen>` to `Tracer.Tests.Unit.csproj`.

4. **`Tracer.Adapters.SharedMemory` is Windows-only at assembly level** — `MemoryMappedFile.CreateOrOpen`, `MemoryMappedFile.OpenExisting`, and named `Semaphore` are Windows-only APIs. Rather than annotating every method individually, an `AssemblyInfo.cs` was added with `[assembly: SupportedOSPlatform("windows")]` for both the adapter project and the unit test project, satisfying CA1416 across all call sites.

5. **`StagedInterval` implements `IDisposable` only** — Test scaffolding initially used `await using` for `StagedInterval`, but `StagedInterval` implements only `IDisposable` (not `IAsyncDisposable`). Tests were corrected to use `using`.

6. **`SharedMemoryRingBuffer.TryRead()` is consumer-side only** — `TryRead_OnEmptyBuffer_ReturnsNull` originally called `TryRead()` on the producer-side buffer returned by `Create`, which throws `InvalidOperationException`. Fixed by creating a separate consumer-side buffer via `Open` for the read assertion.
