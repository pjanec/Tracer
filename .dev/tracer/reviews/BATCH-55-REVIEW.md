# BATCH-55 Review

**Date:** 2026-05-23  
**Reviewer:** Dev Lead  
**Batch:** BATCH-55  
**Status: APPROVED**

---

## Verdict

**APPROVED.** All five P1 items from BATCH-54 are correctly implemented and verified. TRC-P11-007 hardening is implemented with solid tests. Build passes cleanly, targeted tests all pass, full suite shows zero failures before the pre-existing testhost crash.

---

## Build Verification

**Result: PASSED**

```
dotnet build tests\Tracer.Tests.Unit -c Release
Build succeeded. 0 Warning(s) 0 Error(s)
```

---

## Corrective Task 0 — All Five P1 Items Verified

### P1-A: DDS Overflow Detection — DONE ✅

`DdsDiagnosticDataSource.cs` now has:
- `private long _droppedCount;` field
- `public long GetDroppedCount() => Interlocked.Read(ref _droppedCount);` method
- Pre-check `if (reader.Count >= capacity)` before `TryWrite` — correctly avoids the dead-code path that the `DropOldest` channel mode created
- Warning log: `"DDS ingest channel full (capacity={Capacity}), dropping oldest record for topic {Topic}"`

`DdsDiagnosticDataSourceTests.cs` now has:
- `CapturingLogger<T>` helper
- `Build(...)` accepts `ingestBufferSize` and `logger` parameters
- `ReadAsync_OverfilledChannel_DropsRecordsAndLogsWarning` verifies: `received.Count <= bufferSize`, `GetDroppedCount() > 0`, warning log contains "channel full"

**Quality assessment: Excellent.** The test actually exercises the drop path and verifies both the counter and the log output.

### P1-B: Trivial Assertion Fixed — DONE ✅

`SharedMemoryRingBufferTests.cs:109` now reads `BeGreaterThan(0)`. Confirmed.

### P1-C: Field-Level Transport Assertions — DONE ✅

`SharedMemoryTransportTests.cs` `ReadAsync_RecordsWrittenByWriter_AreYielded` now asserts:
- `received[0].SequenceNumber == 1UL`, `[1]` == 2, `[2]` == 3
- All records have `Topic == "topic.event"` and `PublisherNode == "pub"`

**Quality assessment: Good.** The round-trip through encode/decode is now verified at field level.

### P1-D: Four Missing Sync Upload Tests — DONE ✅

All four tests present and correctly implemented:
- `RequestUploadAsync_SendsCorrectBodyToSyncMaster` (SC1) — captures raw HTTP body and asserts it contains nodeId and intervalTimestamp
- `WaitForCompletionAsync_PollingUntilComplete_CallsGetStatusMultipleTimes` (SC3) — feeds InProgress×2 then Completed; asserts final status is Complete; asserts ≥3 GET requests made
- `WaitForCompletionAsync_CancelledDuringPoll_ThrowsOperationCanceledException` (SC5) — feeds infinite InProgress; cancels after 50ms; asserts `OperationCanceledException`
- `RequestUploadAsync_Returns503Twice_Then201_RetriesAndSucceeds` (SC6) — uses `retryAttempts: 3`; enqueues two 503s then 200; asserts 3 HTTP calls total

**Note on SC3:** The developer correctly adapted the assertion from the spec's `r.RequestUri!.PathAndQuery.Contains("status")` to `r.Method == HttpMethod.Get` because the actual API path doesn't contain "status". This is the correct adaptation — the GET verb correctly distinguishes status polls from the initial POST.

**`FakeHttpMessageHandler`** was properly extended with a `Queue<Func<HttpRequestMessage, HttpResponseMessage>>` factory queue and `CapturedRequests` list. The `EnqueueFactory` method allows request-body capture.

**Quality assessment: Excellent.** All four scenarios are correctly tested with real behavior assertions.

### P1-F: AgentHostBuilder Wired — DONE ✅

`AgentHostBuilder.cs` line 34: `builder.Services.AddTracerAdapters(builder.Configuration)` replaces the three hardcoded adapter singletons. `using Tracer.AdapterSelection` added. Old unused `using` directives removed.

---

## TRC-P11-007 Hardening — All Tasks Verified

### Task 7.1: TransportMonitor — DONE ✅

`TransportMonitor.cs` correctly:
- Polls `_transport.GetHealth().TotalDropped` every 5s (configurable)
- Logs `LogWarning` with `NewDrops` and `TotalDropped` when count increases
- Resets `_lastDroppedCount` after logging
- Catches `OperationCanceledException` to break cleanly
- Catches all other exceptions (swallows with `LogError`) so the monitor never crashes the host

3 tests (all pass): warns on increase, silent when stable, does not throw on `GetHealth()` exception.

**Quality assessment: Excellent.** The "does not throw" test is an important robustness property.

### Task 7.2: NAS Reader Circuit Breaker — DONE ✅

`NasStorageReader.ExecuteFileOp<T>` correctly:
- Checks circuit breaker open state (with reset after `CircuitBreakerResetIntervalSeconds`)
- Retries up to `RetryOnTransientError` times on `IOException`
- Increments `_consecutiveFailures` after all retries exhausted
- Trips circuit breaker (throws `CircuitBreakerOpenException`) when threshold reached
- Per-instance state (not static): `_consecutiveFailures`, `_circuitOpenedAt`, `_circuitLock`

`CircuitBreakerOpenException.cs` created as a sealed exception type.
`NasAdapterConfig` extended with `RetryOnTransientError`, `RetryBaseDelaySeconds`, `CircuitBreakerThreshold`, `CircuitBreakerResetIntervalSeconds`.

3 tests (all pass): transient retry succeeds, circuit trips after threshold, circuit resets after interval.

**Quality assessment: Good.** Circuit breaker is per-instance and correctly tested.

**P2 Note:** `ExecuteFileOp` uses `Thread.Sleep` for retry delays rather than async `Task.Delay`. For a file I/O operation that may be called from async contexts, `Thread.Sleep` blocks the calling thread. Given the NAS reader is likely called from synchronous file enumeration contexts, this is acceptable for now. Log to DEBT-TRACKER.

### Task 7.3: Sync Upload Backlog Tracking — DONE ✅

`UploadIntentDispatcher` correctly:
- `_pendingCount` incremented with `Interlocked.Increment` on dispatch
- Logs `LogWarning` "Upload backlog exceeds threshold: PendingCount={PendingCount}, Threshold={Threshold}" when `_pendingCount > _backlogWarningThreshold`
- `WaitForPendingAsync(TimeSpan timeout)` polls `_pendingCount == 0` or deadline
- `AgentConfig` extended with `BacklogWarningThreshold` (default 3) and `ShutdownUploadFlushTimeoutSeconds` (default 60)

2 tests (all pass): backlog warning fires at threshold+1, graceful shutdown waits.

### Task 7.4: Health Endpoint Enrichment — DONE ✅

`HealthEndpoints.cs` now accepts `[FromServices] IAgentTransport? transport` (nullable optional) and returns `sharedMemoryDropped` and `ingestChannelDepth` from `transport.GetHealth()` (or 0 if transport is null).

**Good catch:** The developer correctly identified that `[FromServices]` attribute is **required** for Minimal API optional service parameters — ASP.NET Core would otherwise infer the parameter as a request body, causing a runtime error at route registration.

3 tests pass including `GetHealth_ResponseContainsTransportFields` which validates JSON field presence.

---

## Summary of Findings

| # | Severity | Finding | Status |
|---|----------|---------|--------|
| P2-A | P2 | `NasStorageReader.ExecuteFileOp` uses `Thread.Sleep` for retry delays; should be `Task.Delay` in async contexts | New debt — log to DEBT-TRACKER |
| P2-B | P2 | `UploadIntentDispatcher.WaitForPendingAsync` reads `_pendingCount` without `Volatile.Read` or `Interlocked.Read`; while safe in practice on .NET, a `Volatile.Read` would make the memory ordering intent explicit | New debt — log to DEBT-TRACKER |

No P1 blockers. Both items are P2 and can be addressed in a future maintenance batch.

---

## DEBT-TRACKER Updates

Add to DEBT-TRACKER.md:

| DT-042 | P2 | BATCH-55 | `NasStorageReader.ExecuteFileOp<T>` uses `Thread.Sleep` for retry delays. Replace with `Task.Delay` (making the method `async Task<T>`) to avoid blocking thread pool threads. | Future | Open |
| DT-043 | P2 | BATCH-55 | `UploadIntentDispatcher.WaitForPendingAsync` reads `_pendingCount` as a plain field read. Use `Volatile.Read(ref _pendingCount)` for explicit memory ordering. Low-risk on .NET due to x86/x64 memory model, but not portable across all architectures. | Future | Open |

---

## Git Commit Message

```
feat(phase11): batch-55 — P1 corrective fixes + TRC-P11-007 hardening (TRC-P11-007)

- Fix DDS overflow detection: pre-check reader.Count >= capacity before TryWrite
  (BoundedChannelFullMode.DropOldest made TryWrite always return true — dead code)
- Add DdsDiagnosticDataSource.GetDroppedCount(); add CapturingLogger + overflow test
- Fix SharedMemoryRingBuffer drop test: BeGreaterThan(0) not BeGreaterThanOrEqualTo(0)
- Add field-level round-trip assertions to SharedMemoryTransport test (SeqNo, Topic, Node)
- Add 4 missing SyncSystemUploadService tests (SC1 body, SC3 polling, SC5 cancel, SC6 retry)
- Wire AddTracerAdapters into AgentHostBuilder (replaces 3 hardcoded adapter singletons)
- Add TransportMonitor: periodic IAgentTransport.GetHealth() poll with drop warning
- Add NasStorageReader retry + circuit breaker (CircuitBreakerOpenException)
- Add UploadIntentDispatcher backlog tracking + WaitForPendingAsync shutdown flush
- Enrich /api/health with sharedMemoryDropped + ingestChannelDepth (fix [FromServices])
```

---

## Next Batch

BATCH-56 will cover:
- **TRC-P11-008**: Integration test infrastructure (`Tracer.Tests.Integration.Real`) with simulation harness fixture, skip attributes, and stub tests for all real-integration test categories
- **TRC-P11-009**: Soak test scaffold + handoff notes document

See BATCH-56-INSTRUCTIONS.md.
