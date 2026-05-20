# BATCH-04 Dev Lead Review

**Batch:** BATCH-04  
**Date:** 2026-05-20  
**Reviewer:** Dev Lead  
**Status:** ✅ APPROVED

---

## Summary

All 7 task items completed: TRC-P2-006 through TRC-P2-009 and DT-006/007/008. Solution builds clean (0 errors, 0 warnings). 119 tests pass (109 unit, 10 integration), up from 87+10. FakeNode project compiles and is registered in solution.

---

## Task-by-Task Assessment

### TRC-P2-006 StartupRecoveryService ✅

Implementation matches spec exactly:
- Orphan detection via missing `_ready` sentinel
- DuckDB event count with graceful fallback (corrupt DB → count=0, loop continues)
- `RecoveryAfterCrash` manifest + `UnrecoveredCrashGap` CaptureGap
- Dispatches upload per orphan
- 6 tests cover: no directory, no orphans, single orphan manifest+sentinel, manifest reason, multiple orphans, corrupt DB

### TRC-P2-007 RetentionManager ✅

Clean redesign: `IntervalRotator` dependency removed; open interval timestamp passed explicitly. This is strictly better than the original design — `IntervalRotator` is sealed/non-virtual, so mocking it was impossible. The `AgentHostedService` call site passes `_rotator.CurrentDirectory?.Timestamp` correctly. 3 tests cover: keep-N eviction, orphan protection, nothing-to-evict no-throw.

### TRC-P2-008 LocalFileSystemUploadService + Transport ✅

ZIP archive creation is correct: parquet files get `NoCompression`, others `Optimal`, fast_state files get `"fast_state/"` prefix. Idempotent (delete-then-recreate). Drop tracking counter in `InProcessChannelTransport` was already correct as noted. 7 tests (4 upload + 3 transport).

### TRC-P2-009 FakeNode ✅

Complete project: `FakeNodeConfigLoader` validates absolute path, reads JSON under `"FakeNode"` key. `FakeNodeOrchestrator` drives `MockDataSource → InProcessChannelTransport`. `Program.cs` writes `LOG_FILE=` as first stdout line, registers all agent services explicitly. Added to `Tracer.sln`.

### DT-006/007/008 ✅

All three debt items resolved with correct test names. `IntervalScheduler_LessThanOneMinute_Throws` correctly expects `ArgumentOutOfRangeException` (matches constructor code). `IntervalRotator_NotifyCaptureGap_AccumulatesInManifest` verifies CaptureGaps round-trip through ManifestWriter. `RecordRouter_AfterWrite_NotifiesIntervalContext` is an explicit dedicated test.

---

## Issues Found

### P3 (Minor / Low Risk)

**P3-1: StartupRecovery slow_state DB count always 0**

In `TryFinalizeAsync`, the code opens `slow_state.duckdb` but does nothing with the reader beyond verifying it opens (`_ = reader`). `SlowStateCount` stays 0 in recovery manifests even when the file has data. Acceptable for crash recovery (the value is informational only), but should be logged or explicitly documented.

→ Add to DEBT-TRACKER as DT-009 (Low priority).

**P3-2: FakeNode `LoggingPaths.GetCurrentLogFilePath` not null-guarded**

If `config.AgentConfig.LogsRoot` is empty (e.g., bad JSON), `LoggingPaths` may throw before the host is built. The outer `catch (Exception ex)` in `Main` catches this, but the error message will be cryptic.

→ Acceptable. Document as config validation responsibility of `FakeNodeConfigLoader` in future.

---

## Debt Tracker Updates

| ID | Action |
|----|--------|
| DT-006 | ✅ Close |
| DT-007 | ✅ Close |
| DT-008 | ✅ Close |
| DT-009 | Open — `StartupRecovery` slow_state count always 0 (P3) |

---

## Commit Approval

Commit message from report is approved as-is.
