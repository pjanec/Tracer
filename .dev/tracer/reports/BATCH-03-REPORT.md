# BATCH-03 Report

**Batch:** BATCH-03  
**Developer:** GitHub Copilot  
**Date:** 2025-07-16  
**Status:** Complete

---

## 📊 Task Completion

| Task ID | Status | Notes |
|---------|--------|-------|
| TRC-P2-001 | ✅ Complete | All Core abstractions added: `IAgentTransport`, `ITelemetryUploadService`, `UploadRequest`, `FileToUpload`, `UploadIntentId`, `UploadStatus`, `IntervalTimestamp`, `CaptureGap`, `CaptureGapReason`, `IntervalManifest`, `ManifestFinalizationReason`, `SessionMarker`. `IDiagnosticStorageWriter` extended with `AppendFastStateAsync`. |
| TRC-P2-002 | ✅ Complete | `FastStateParquetWriter`, `NullFastStateWriter`, `ColumnExtractor`, `ParquetSchemas`, `WellKnownTopicSchemas` added. `DuckDbStorageWriter.CreateAsync` signature updated. Tests: 5 unit tests in `FastStateParquetWriterTests`. |
| TRC-P2-003 | ✅ Complete | `Tracer.Agent` project created (OutputType=Exe). `AgentConfig`, `ConfigValidation`, `TransportFactory`, `UploadServiceFactory`, `AgentHostBuilder`, `Program.cs`. `InProcessChannelTransport` and `LocalFileSystemUploadService` in `Tracer.Adapters.Mock`. Tests: 6 tests in `AgentConfigTests`. |
| TRC-P2-004 | ✅ Complete | `BackpressureMonitor`, `BackpressureLevel`, `DropPolicy`, `IIntervalContext`, `RecordRouter`, `IngestionPipeline`. Tests: 5 tests in `DropPolicyTests`, 3 tests in `RecordRouterTests`. |
| TRC-P2-005 | ✅ Complete | `IntervalScheduler`, `IntervalRotator`, `IntervalDirectory`, `ManifestWriter`, `UploadIntentDispatcher`, `AgentHostedService`, `StartupRecoveryService`, `RetentionManager`. Tests: 5 tests in `IntervalSchedulerTests`, 3 tests in `ManifestWriterTests`, 7 tests in `IntervalRotatorTests`. |

---

## 🧪 Testing Results

**Unit Tests Passed:** 87 / 87  
**Integration Tests Passed:** 10 / 10  
**Total:** 97 / 97

Previous batch had 68 tests (58 unit + 10 integration). This batch added 29 new unit tests.

**Key Test Scenarios Verified:**
- [x] `ConfigValidation` rejects missing NodeId, relative paths, too-short intervals, non-divisible intervals
- [x] `InProcessChannelTransport` write/read round-trip (BoundedChannel, Complete semantics)
- [x] `DropPolicy` correctly gates fast/slow/event records at each `BackpressureLevel`
- [x] `RecordRouter` dispatches to correct storage method and notifies `IIntervalContext`
- [x] `IntervalScheduler` correctly floors current time to interval boundary (UTC-safe)
- [x] `ManifestWriter` JSON serialization round-trip and `IntervalTimestamp` as bare string
- [x] `IntervalRotator` directory creation, double-open guard, per-record count tracking, manifest+sentinel writing, graceful-shutdown no-reopen

---

## 📝 Developer Insights

**Q1: What issues did you encounter during implementation? How did you resolve them?**

1. **`IIntervalContext` accessibility**: Initially declared `internal`, causing CS0051 (parameter type less accessible than the public constructors of `RecordRouter` and `IngestionPipeline`). Resolved by making it `public`; the interface is narrow and intentional as a public contract between the agent's subsystems.

2. **`IntervalScheduler` UTC offset bug**: `now.Date` on a `DateTimeOffset` returns a `DateTime` with `DateTimeKind.Unspecified`. When implicitly cast back to `DateTimeOffset` this picked up the machine's local timezone (+2h in the CI environment), causing `IntervalTimestamp.FromUtc` to throw and scheduler tests to fail. Fixed by using `new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero)` and `now.UtcDateTime.TimeOfDay`.

3. **`Serilog.ReadFrom.Configuration`**: Referenced in `AgentHostBuilder` but `Serilog.Settings.Configuration` package was not in `Directory.Packages.props`. Removed the call; configuration is fully driven by `AgentConfig` properties.

4. **CA1062 null-validation warnings-as-errors**: Several public methods lacked null guards. Added `ArgumentNullException.ThrowIfNull()` to all externally-visible parameter sites.

5. **`InProcessChannelTransport` IDisposable vs IAsyncDisposable**: Only implements `IAsyncDisposable`, so test code needed `await using` not `using`.

6. **`ManifestWriter` JSON whitespace**: `WriteIndented=true` inserts a space after the colon; the initial test assertion used `:"`  (no space). Fixed assertion to use a regex.

**Q2: Did you spot any weak points in the existing codebase? What would you improve?**

- `WellKnownTopicSchemas.ToDictionary()` is invoked on every interval open (`IntervalRotator.OpenInternalAsync`). For a long-running process this is negligible, but the schema map could be cached as a static field.
- `RetentionManager.ApplyAsync` is a stub that does nothing. Until implemented, old intervals will accumulate indefinitely on disk.
- `StartupRecoveryService.RecoverAsync` is also a stub — crash-gap detection is not yet active.

**Q3: What design decisions did you make beyond the instructions? How did you handle them?**

- `IIntervalContext` was promoted from `internal` to `public` (described in Q1). An alternative would have been to make `RecordRouter` and `IngestionPipeline` also `internal`, but that would prevent clean DI registration in `AgentHostBuilder` without an explicit factory.
- `IntervalRotator` uses a single `SemaphoreSlim(1,1)` for both `OpenCurrentAsync` and `RotateAsync`, ensuring mutual exclusion. An alternative (reader-writer lock) was considered but deemed over-engineered for a single-rotation-at-a-time lifecycle.

**Q4: What edge cases did you discover that weren't mentioned in the spec?**

- `IntervalTimestamp.FromUtc` validates that the offset is exactly `TimeSpan.Zero`. This means the scheduler must always produce UTC `DateTimeOffset` values — a subtle invariant that broke when running on non-UTC machines.
- `ManifestWriter.ReadAsync` returns `null` if the file does not exist rather than throwing, which matches the opt-in recovery pattern in `StartupRecoveryService`.

**Q5: Are there any performance concerns or optimization opportunities you noticed?**

- `InProcessChannelTransport` uses `BoundedChannelFullMode.DropOldest` with a capacity of 100,000 records. For high-frequency topics this means recent records survive while older ones are silently lost, which matches the design intent (fast-state is periodic and can tolerate skipped samples). However, dropping is counted per-check on `ReadAsync` and may slightly overcount if the channel drains between check and read.
- `IntervalRotator.RotateAsync` takes the semaphore and does file I/O (DuckDB flush, Parquet close, JSON write, file copy for upload) inside the lock. For large intervals this could delay ingestion briefly; a future improvement would be to snapshot counts and release the lock before I/O.

---

## ⚠️ Outstanding Issues / Next Steps

- `RetentionManager` is a stub — TRC-P2-006 or later batch should implement disk-quota enforcement.
- `StartupRecoveryService` is a stub — crash-gap recovery is not yet active.
- `AgentHostedService.ExecuteAsync` runs ingestion, retention, and rotation as concurrent tasks but does not handle partial failures (e.g., if retention crashes, the other loops continue). A supervision strategy may be needed.
- `Serilog.Settings.Configuration` was not added; log level is currently hard-coded. A future batch should add this package and allow `appsettings.json` overrides.
