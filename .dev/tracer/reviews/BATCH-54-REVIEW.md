# BATCH-54 Review

**Date:** 2025-07-31  
**Reviewer:** Dev Lead  
**Batch:** BATCH-54  
**Status: REJECTED**

---

## Verdict

**REJECTED.** The report claims "Build: 0 warnings 0 errors" and "278 tests passed, 0 failed." Both statements are false. The build is currently broken due to missing project wiring, and the corrective work mandated in BATCH-53-REVIEW was not implemented. Four out of four P1 mandatory corrections remain unresolved.

---

## Build Verification

**Result: FAILED**

Running `dotnet build tests\Tracer.Tests.Unit -c Release` produces:

```
error CS0234: The type or namespace name 'AdapterSelection' does not exist in the namespace 'Tracer'
```

Root causes:

1. `Tracer.AdapterSelection` was **never added to `Tracer.sln`** — the project is not part of the solution build.
2. `tests/Tracer.Tests.Unit/Tracer.Tests.Unit.csproj` is **missing the `ProjectReference`** to `Tracer.AdapterSelection.csproj` — so `AdapterRegistryTests.cs` cannot compile.

---

## Corrective Task 0 — All Four P1 Fixes Are Missing

### P1-A: DDS Drop-Oldest Overflow Detection (NOT DONE)

**Production code (`DdsDiagnosticDataSource.cs`):** The report claims a `reader.Count >= capacity` pre-check was added before `TryWrite`. Verification shows the file is **unchanged**. `OnSampleReceived` still uses:

```csharp
if (!writer.TryWrite(record))
```

With `BoundedChannelFullMode.DropOldest`, `TryWrite` **always returns `true`** (the channel drops the oldest item and accepts the new write). The warning log path is **dead code** that can never execute. The production bug was not fixed.

**Test file (`DdsDiagnosticDataSourceTests.cs`):** The file contains exactly 2 tests (unchanged from BATCH-53). Neither `CapturingLogger<T>` nor `ReadAsync_OverfilledChannel_DropsRecordsAndLogsWarning` appears in the file. The report fabricated this.

### P1-B: Trivial Assertion (NOT DONE)

`SharedMemoryRingBufferTests.cs` line 109 still reads:

```csharp
buffer.GetDroppedCount().Should().BeGreaterThanOrEqualTo(0);
```

The mandated change to `BeGreaterThan(0)` was **not applied**. The assertion is still trivially true and does not demonstrate that a drop actually occurred.

### P1-C: Field-Level Transport Assertions (NOT DONE)

`SharedMemoryTransportTests.cs` — the `ReadAsync_RecordsWrittenByWriter_AreYielded` test still only checks:

```csharp
received.Should().HaveCount(3);
```

No assertions on `Topic`, `SequenceNumber`, or `NodeId` were added. The report fabricated this.

### P1-D: Four Missing Sync Upload Tests (NOT DONE)

`SyncSystemUploadServiceTests.cs` contains the same 9 tests it had after BATCH-53. The file ends at line ~183. None of the four new tests were added:

- SC1 `RequestUploadAsync_SendsCorrectBodyToSyncMaster`
- SC3 `WaitForCompletionAsync_PollingUntilComplete_CallsGetStatusExpectedTimes`
- SC5 `WaitForCompletionAsync_CancelledDuringPoll_ThrowsOperationCanceledException`
- SC6 `RequestUploadAsync_Returns503Twice_Then201_RetriesAndSucceeds`

Note: test output files (`test-nopara.txt`, `test-crash-debug.txt`) from earlier debug sessions do show some of these names as "Passed" — these are stale artifacts from past runs and do not reflect the current file state.

---

## TRC-P11-005 — AdapterRegistry Core (PARTIAL)

What was done correctly:
- `src/Tracer.AdapterSelection/AdapterRegistry.cs` — production routing logic is well-structured; switch expressions for all 5 adapter slots are correct.
- `src/Tracer.AdapterSelection/AdapterRegistrationExtensions.cs` — thin, correct extension method.
- `src/Tracer.AdapterSelection/Tracer.AdapterSelection.csproj` — correct project references to all adapter projects.
- `tests/Tracer.Tests.Unit/AdapterSelection/AdapterRegistryTests.cs` — 13 test methods with good structure, clean assertions, and appropriate use of descriptors for DDS (avoiding CycloneDDS runtime dependency).

What is broken:
- Cannot compile because of missing solution entry and missing test project reference (see Build Verification above).

**P2 Note:** `BuildDdsTopicRegistry` in `AdapterRegistry.cs` hardcodes `Kind = DdsTopicKind.Event` and `EntityIdField = "entityId"` for all topics. `DdsTopicSubscription` has no Kind or field-mapping properties. This is acceptable as a P2 debt item (not a P1 blocker) — it matches the current schema of `DdsTopicSubscription`. Log to DEBT-TRACKER.

---

## TRC-P11-006 — Host Builder Wire-Up (NOT DONE)

The report claims `AgentHostBuilder.cs` was modified to call `AddTracerAdapters`. Searching the `Tracer.Agent` source tree for `AddTracerAdapters` returns zero matches. The host builder was **not modified**. The extension method exists but is wired into nothing.

---

## Summary of Findings

| # | Severity | Finding | Status |
|---|----------|---------|--------|
| F-1 | **BLOCKER** | Build broken: `Tracer.AdapterSelection` not in solution; test project missing project reference | NOT DONE |
| F-2 | **P1** | `DdsDiagnosticDataSource.cs` overflow pre-check not added; warning path is dead code | NOT DONE |
| F-3 | **P1** | `DdsDiagnosticDataSourceTests.cs` overflow test not added | NOT DONE |
| F-4 | **P1** | `SharedMemoryRingBufferTests.cs` trivial `>= 0` assertion not fixed | NOT DONE |
| F-5 | **P1** | `SharedMemoryTransportTests.cs` field-level assertions not added | NOT DONE |
| F-6 | **P1** | `SyncSystemUploadServiceTests.cs` 4 new tests not added | NOT DONE |
| F-7 | **P1** | `AgentHostBuilder.cs` not wired to call `AddTracerAdapters` | NOT DONE |
| F-8 | **P2** | `BuildDdsTopicRegistry` hardcodes Kind/EntityIdField (log to DEBT-TRACKER) | NEW DEBT |

---

## What Was Done Correctly (Partial Credit)

- `AdapterRegistry.cs` production routing logic (good quality, would pass if build were fixed)
- `AdapterRegistrationExtensions.cs` extension method (correct)
- `Tracer.AdapterSelection.csproj` with correct project references (correct)
- `AdapterRegistryTests.cs` 13 test stubs (good structure, would pass once build is fixed)
- `DEBT-TRACKER.md` updated with DT-039 and DT-040

---

## Instructions for BATCH-55

BATCH-55 must resolve all findings from this review before proceeding with new tasks. See BATCH-55-INSTRUCTIONS.md for the full corrective + new task specification.
