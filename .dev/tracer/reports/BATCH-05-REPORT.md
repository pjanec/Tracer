# BATCH-05 Report

## Status: COMPLETED

## Tasks Implemented

### TRC-P2-011 — Missing Agent Unit Tests
**Files modified:**
- `tests/Tracer.Tests.Unit/Agent/IntervalRotatorTests.cs` — Added 2 tests:
  - `IntervalRotator_RotateAsync_DispatchesUpload` — verifies `_ready` file exists after rotation
  - `IntervalRotator_DisposeAsync_TriggersGracefulShutdownRotation` — verifies GracefulShutdown manifest written on disposal
- `tests/Tracer.Tests.Unit/Agent/ManifestWriterTests.cs` — Added 2 tests:
  - `ManifestWriter_WallclockTimes_SerializeAsIso8601` — verifies `finalized_at` is ISO 8601 string
  - `ManifestWriter_EmptyGapsAndMarkers_SerializesEmptyArrays` — verifies empty arrays serialize as `[]`
- `tests/Tracer.Tests.Unit/Agent/IntervalSchedulerTests.cs` — Added 1 test:
  - `IntervalScheduler_24HourDuration_DoesNotThrow` — verifies 24-hour intervals work

**Unit test count: 109 → 114**

---

### TRC-P2-010 — TestHarness Phase 2 Additions
**Files created/modified:**
- `src/Tracer.TestHarness/Tracer.TestHarness.csproj` — Added `Tracer.Agent`, `Tracer.FakeNode` project refs; `Microsoft.Extensions.Hosting` package
- `src/Tracer.TestHarness/Agent/AgentFixtureOptions.cs` — New; `record` with `UseSimulatedClock`, `TransportCapacity`, `KeepLastNIntervals`
- `src/Tracer.TestHarness/Agent/TracerAgentFixture.cs` — New; full agent-in-process fixture with `IAsyncDisposable`, `CreateAsync`, `PushAsync`, `ForceRotationAsync`, `StopAsync`; supports optional `SimulatedClock`
- `src/Tracer.TestHarness/Agent/FakeNodeFixture.cs` — New; runs a FakeNode scenario in-process, waits for completion, exposes `Manifests` and `IntervalZipPaths`

---

### TRC-P2-012 — Agent Integration Tests
**Files created:**
- `tests/Tracer.Tests.Integration/AgentIntervalLifecycleTests.cs` — 4 tests; verifies rotation, record counts, upload, and no data-loss under healthy conditions
- `tests/Tracer.Tests.Integration/AgentRecoveryTests.cs` — 3 tests; verifies orphan recovery on restart, crash reason in manifest, and post-recovery record acceptance
- `tests/Tracer.Tests.Integration/FakeNodeEndToEndTests.cs` — 3 tests; full end-to-end via `FakeNodeFixture`; verifies intervals produced, all uploaded, last interval has GracefulShutdown

**Integration test count: 10 → 20**

---

## Bug Fixes

### BUG-01: `IntervalRotator.DisposeAsync` double-disposal crash (`ObjectDisposedException`)
- **Root cause**: `IntervalRotator` registered under two DI service keys (`AddSingleton<IntervalRotator>()` + `AddSingleton<IIntervalContext>(sp => sp.GetRequiredService<IntervalRotator>())`). DI container called `DisposeAsync` twice on the same instance. The second call hit `_lock.WaitAsync()` on an already-disposed `SemaphoreSlim`.
- **Fix**: Made `DisposeAsync` idempotent using `private int _disposed` + `Interlocked.Exchange(ref _disposed, 1)` guard.
- **File**: `src/Tracer.Agent/Lifecycle/IntervalRotator.cs`

### BUG-02: `IntervalRotator.RotateAsync` re-opening the same interval on forced rotation
- **Root cause**: `RotateAsync(ScheduledRotation)` used `_scheduler.CurrentIntervalStart()` to determine the next interval's start time. When `ForceRotationAsync` is called before the wall-clock boundary, `CurrentIntervalStart()` returns the same timestamp as the interval just closed — causing the next interval to collide with the just-closed one.
- **Fix**: Changed to use `prevDir.Timestamp.ToDateTimeOffset() + _config.IntervalDuration` — produces the correct "next" timestamp regardless of when rotation is called.
- **File**: `src/Tracer.Agent/Lifecycle/IntervalRotator.cs`
- **Note**: Semantically equivalent to the old code for normal (post-boundary) rotations; more correct for forced early rotations.

---

## Test Results

| Suite | Before | After |
|-------|--------|-------|
| Unit | 109 | 114 |
| Integration | 10 | 20 |
| **Total** | **119** | **134** |

All 134 tests pass (0 failures, 0 skipped).

Build: 0 errors, 0 warnings.
