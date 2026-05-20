# BATCH-22 Review — TRC-P5-001 LiveMultiIntervalReader & IntervalSetTracker

**Status:** ✅ APPROVED (with dev-lead corrections)

---

## Summary

BATCH-22 delivers TRC-P5-001's core production code in full. The `IntervalSetTracker`, `LiveMultiIntervalReader`, query service migration (CTE approach), `RetentionManager` pre-deletion callback, and all DI wiring are solid. The sub-agent reported 5 missing test files in the batch report's Outstanding Issues; those files were created directly by the dev-lead review step, resolving all outstanding issues. A concurrent-race bug in `AcquireAsync` was also discovered and fixed during review.

Tests grew: Unit 261 → 274; Integration 68 → 72.

---

## Test Quality Assessment

### New Unit Tests — `IntervalSetTrackerTests.cs` (7 tests) ✅

**`InitializeAsync_NoCompletedIntervals_SnapshotContainsOnlyActive`** ✅
- Minimal file setup, no spurious intervals on disk
- Asserts count=1 and active non-null — precise

**`InitializeAsync_FiveCompleted_CapThree_SnapshotContainsThreeNewestPlusActive`** ✅
- Creates 5 real `_ready` sentinel directories
- Verifies exact set membership (newest 3 kept, oldest 2 discarded) — correct for descending sort + Skip

**`OnIntervalRotatedAsync_PreviousActiveBecomesCompleted`** ✅
- Captures original active timestamp before rotation
- Forces real `IntervalRotator.RotateAsync` to produce a new active
- Asserts both that old is now Completed and new Active exists — complete assertion

**`OnIntervalEvictedAsync_RemovesEvictedIntervalFromSnapshot`** ✅
- Starts with 1 completed interval, evicts it, verifies empty Completed set

**`SetChanged_FiredAfterInitialize`** ✅ / **`SetChanged_FiredAfterRotation`** ✅
- Event-counting pattern; clean

**`SetChanged_NotFiredIfEvictionTargetNotInSet`** ✅
- Evicts a directory that was never tracked; asserts 0 firings — critical edge case, well-covered

### New Unit Tests — `LiveMultiIntervalReaderTests.cs` (5 tests) ✅

**`PoolSize_AfterInitialize_AllConnectionsAreAvailable`** ✅
- Acquires all 4 slots without blocking — proves correct pool size
- Uses generous CTS timeout (5s) — does not mask real hangs as false passes

**`AcquireAsync_EmptySnapshot_ConnectionSqlIsEmptySentinel`** ✅
- Verifies `SELECT NULL WHERE FALSE` for no-interval case — important safety net

**`SetChanged_TriggersPoolRebuild_NewConnectionsReflectNewSnapshot`** ✅
- Creates a real DuckDB file with `events` table
- Fires SetChanged with a completed-interval snapshot
- Asserts new SQL ≠ empty sentinel — verifies rebuild propagated

**`StaleConnection_ReturnedAfterRebuild_IsDiscarded`** ✅
- Acquire → rebuild → return stale conn → assert pool still has fresh connections
- Directly tests the `ReferenceEquals(conn.IssuingSnapshot, _currentSnapshot)` logic

**`ConcurrentAcquireAndRebuild_DoesNotDeadlock`** ✅
- Pool size 2, 4 acquires, 2 rebuilds, 30s CTS — proportional, won't false-pass
- Acquires a final connection after all tasks complete — proves pool remains usable

### New Integration Tests — `LiveMultiIntervalQueryTests.cs` (3 tests) ✅

**`QuerySpansThreeIntervals_AllSessionsReturned`** ✅
- Uses unique GUIDs per interval to eliminate cross-interval contamination
- Pushes 1 session_start per interval, rotates between each
- Asserts all 3 session IDs appear in `/api/sessions` — precise

**`AfterRotation_NewIntervalEventsIncluded`** ✅
- Before and after sessions both visible after single rotation — regression test for query continuity

**`AfterEviction_EvictedIntervalEventsExcluded`** ✅
- Captures `interval1Dir` before rotation
- Calls `tracker.OnIntervalEvictedAsync` directly — no reliance on retention timer
- Checks pre-eviction (both present) AND post-eviction (evicted absent, kept present)
- 100ms delay after eviction for pool rebuild — sufficient without being excessive

### New Integration Test — `RetentionCoordinationTests.cs` (1 test) ✅

**`Retention_WaitsBeforeDeletion`** ✅
- Creates 2 completed intervals, configures `KeepLastNIntervals=1`
- Callback checks `Directory.Exists` at fire time — verifies "before delete" guarantee
- Captures `callbackDir.Timestamp` and asserts it's `ts1` (the older one) — precise eviction order
- `SetPreDeletionDelay(100ms)` makes the test fast without falsifying the delay behavior
- After `ApplyAsync`, asserts `dir1.RootPath` deleted and `dir2.RootPath` still exists — complete

### New Unit Test — `ObserverDiTests.cs` (1 test) ✅

**`QueryServices_UseLiveMultiIntervalReader_NotSinglePool`** ✅
- Resolves `LiveMultiIntervalReader` from actual DI container (via `ObserverFixture`)
- Resolves all 4 query services to check for missing dependency exceptions
- Asserts `ReadOnlyConnectionPool` service is null — verifies removal from DI

---

## Bug Fixed During Review

**Race condition in `AcquireAsync` (ChannelClosedException)** — The concurrent test exposed that
`AcquireAsync` could hold a stale channel reference when a concurrent `RebuildAsync` completed the
old pool's writer. Added a `while(true)` retry loop that catches `ChannelClosedException` and
re-reads `_connections`, matching the pattern used for other bounded-channel consumers. This is a
correctness fix (not just a test workaround) that prevents transient failures in production under
rapid rotations.

---

## Code Quality Notes

### Strengths
1. **Coordinator/worker DuckDB pattern** — elegant solution to shared-catalog ATTACH constraints. Only the coordinator owns ATTACHments; workers inherit for free.
2. **`WithEventsCte(sql)` helper** — replaces raw SQL string manipulation in 4 query services cleanly.
3. **`BuildEventsUnionSql()` returns `SELECT NULL WHERE FALSE`** for empty snapshot — safe for all callers.
4. **Pool rebuild drain-before-build** — correctly releases file locks before opening new connections.
5. **Stale connection detection via `ReferenceEquals(IssuingSnapshot, _currentSnapshot)`** — O(1), no GC pressure.
6. **`SetPreDeletionCallback` / `SetPreDeletionDelay`** — clean seam for testing without changing production defaults.
7. **`IntervalSetTracker` not sealed, methods `virtual`** — enables all test subclassing patterns.

### Minor Observations (non-blocking)
- `ControllableTracker` in `LiveMultiIntervalReaderTests` is a compact, well-designed stub. Its pattern could be extracted to TestHarness if needed by future tests.
- The coordinator's `RebuildAsync` does `oldPool.Reader.TryRead(out var stale)` after `TryComplete()`. If connections are all checked out, nothing is read here. That is fine — checked-out stale connections are handled in `ReturnAsync`.

---

## Success Conditions Verification

| # | Condition | Status |
|---|-----------|--------|
| 1 | `IntervalSetTrackerTests.cs` with 7 named tests | ✅ 7 tests, all pass |
| 2 | `LiveMultiIntervalReaderTests.cs` with 5 tests | ✅ 5 tests, all pass |
| 3 | `ObserverHostedService` calls `tracker.InitializeAsync` then `reader.InitializeAsync` | ✅ Verified in `ObserverHostedServiceTests.OnStart_TrackerInitializedAfterIntervalOpen` |
| 4 | `ReadOnlyConnectionPool` has zero DI registrations (`ObserverDiTests`) | ✅ `services.GetService<ReadOnlyConnectionPool>()` returns null |
| 5 | `LiveMultiIntervalQueryTests.cs` with 3 integration tests | ✅ 3 tests, all pass |
| 6 | `RetentionCoordinationTests.Retention_WaitsBeforeDeletion` | ✅ Passes |
| 7 | All Phase 1-4 tests still pass | ✅ 274/274 unit, 72/72 integration |

---

## Test Results

- **Unit tests:** 274 / 274 passed (↑ from 261)
- **Integration tests:** 72 / 72 passed (↑ from 68, excluding pre-existing file-lock smoke test)
- **1 known transient:** `DistributionSmokeTests.Publish_ProducesExpectedLayout` — PDB file-lock when run alongside agent-spawning tests; unrelated to BATCH-22; passes in isolation.

---

## Verdict

**APPROVED.** All 7 success conditions are met. Core production code (coordinator/worker pool, tracker, CTE migration, retention callback) is correct and robust. The 5 initially-missing test files were completed by dev-lead review. A race condition bug in `AcquireAsync` was discovered and fixed. TRC-P5-001 is complete.
