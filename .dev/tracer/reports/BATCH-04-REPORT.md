# BATCH-04 Developer Report

## Task Status

| Task | Status | Notes |
|------|--------|-------|
| TRC-P2-006 StartupRecoveryService | ✅ Complete | Full implementation; 6 unit tests added |
| TRC-P2-007 RetentionManager | ✅ Complete | Rewritten: `IntervalRotator` dependency removed; `ApplyAsync(IntervalTimestamp?, CancellationToken)` signature; 3 unit tests added |
| TRC-P2-008 LocalFileSystemUploadService + transport drops | ✅ Complete | ZIP archives via `System.IO.Compression`; drop tracking was already correct; 4 upload tests + 3 transport tests added |
| TRC-P2-009 Tracer.FakeNode | ✅ Complete | New exe project: `FakeNodeConfig`, `FakeNodeConfigLoader`, `FakeNodeOrchestrator`, `Program.cs`; added to `Tracer.sln` |
| DT-006 IntervalScheduler missing tests | ✅ Complete | Added `LessThanOneMinute_Throws` + `TimeUntilNextBoundary_DecreasesAsClockAdvances` |
| DT-007 NotifyCaptureGap test | ✅ Complete | Added `IntervalRotator_NotifyCaptureGap_AccumulatesInManifest` |
| DT-008 RecordRouter NotifyIntervalContext test | ✅ Complete | Added `RecordRouter_AfterWrite_NotifiesIntervalContext` |

## Test Counts

| Suite | Passing | Failing |
|-------|---------|---------|
| Tracer.Tests.Unit | 109 | 0 |
| Tracer.Tests.Integration | 10 | 0 |
| **Total** | **119** | **0** |

## New Test Files

- `tests/Tracer.Tests.Unit/Agent/StartupRecoveryTests.cs` — 6 tests
- `tests/Tracer.Tests.Unit/Agent/RetentionManagerTests.cs` — 3 tests
- `tests/Tracer.Tests.Unit/Agent/UploadIntentDispatcherTests.cs` — 2 tests
- `tests/Tracer.Tests.Unit/Mock/InProcessChannelTransportTests.cs` — 3 tests
- `tests/Tracer.Tests.Unit/Mock/LocalFileSystemUploadServiceTests.cs` — 4 tests

Additions to existing test files:
- `IntervalSchedulerTests.cs` — +2 tests (DT-006)
- `IntervalRotatorTests.cs` — +1 test (DT-007)
- `RecordRouterTests.cs` — +1 test (DT-008)

## Design Decisions

**Q1: RetentionManager decoupled from IntervalRotator.**
The original implementation injected `IntervalRotator` to check `CurrentDirectory`, but `IntervalRotator` is sealed with non-virtual properties — untestable without real instances. Decoupled by moving the open-interval check to the call site: `ApplyAsync(IntervalTimestamp? openIntervalTimestamp, CancellationToken)`. `AgentHostedService` passes `_rotator.CurrentDirectory?.Timestamp`. Cleaner DI, fully testable.

**Q2: StartupRecovery uses DuckDB reader for event count with graceful fallback.**
Corrupt/missing DuckDB files are caught per-orphan; the loop continues and the orphan is still finalized with `EventCount = 0`. This matches expected behaviour in crash scenarios where the DB may be half-written.

**Q3: FakeNode Program.cs mirrors AgentHostBuilder but stays explicit.**
Rather than calling `AgentHostBuilder.Configure()`, `Program.cs` explicitly registers all services so the fake node's overrides (transport, upload service) aren't silently overwritten. Any change to AgentHostBuilder will not silently affect FakeNode.

**Q4: LocalFileSystemUploadService re-uploads are idempotent.**
If the zip already exists, it is deleted and re-created. This avoids partial-write corruption and makes the test `Upload_Idempotent` deterministic.

**Q5: InProcessChannelTransport drop-tracking is a best-effort count.**
The `Reader.Count >= _capacity` check before `TryWrite` has a race window under concurrent writers: the channel's `DropOldest` policy ensures exactly one drop per overflow, but our counter may over-count in heavy concurrency. Acceptable for dev/test telemetry; documented in code.

## Debt Items Resolved This Batch

| ID | Resolution |
|----|-----------|
| DT-006 | Added both missing `IntervalSchedulerTests` methods |
| DT-007 | Added `NotifyCaptureGap_AccumulatesInManifest` |
| DT-008 | Added `RecordRouter_AfterWrite_NotifiesIntervalContext` |

## Remaining Open Debt (not addressed this batch)

| ID | Description |
|----|-------------|
| DT-001 | LIMIT/OFFSET inline integers |
| DT-002 | SQL injection test wrong field |
| DT-004 | DeterminismTests missing fields |
| DT-005 | Different-seeds weak assertion |

## Suggested Commit Message

```
feat(phase2-p2): startup recovery, retention, ZIP upload, FakeNode (BATCH-04)

TRC-P2-006: StartupRecoveryService — scans orphans, reads DuckDB event counts,
            writes RecoveryAfterCrash manifest + _ready sentinel + dispatches upload
TRC-P2-007: RetentionManager — KeepLastN + disk watermark; decoupled from
            IntervalRotator via ApplyAsync(IntervalTimestamp?, CT) signature
TRC-P2-008: LocalFileSystemUploadService — ZIP archives via System.IO.Compression
            (parquet=NoCompression, others=Optimal); drop tracking verified correct
TRC-P2-009: Tracer.FakeNode project (tracer-fakenode.exe):
            FakeNodeConfig, FakeNodeConfigLoader, FakeNodeOrchestrator, Program.cs
DT-006: IntervalScheduler_LessThanOneMinute_Throws,
        IntervalScheduler_TimeUntilNextBoundary_DecreasesAsClockAdvances
DT-007: IntervalRotator_NotifyCaptureGap_AccumulatesInManifest
DT-008: RecordRouter_AfterWrite_NotifiesIntervalContext

109 unit + 10 integration tests pass; 0 errors, 0 warnings
```
