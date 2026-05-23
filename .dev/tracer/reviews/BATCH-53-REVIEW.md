# BATCH-53 Review — Phase 11 Part A: Real Adapter Assemblies

**Batch:** BATCH-53  
**Tasks:** TRC-P11-001 through TRC-P11-004  
**Reviewer:** Dev Lead  
**Verdict:** ⚠️ APPROVED WITH MANDATORY CORRECTIONS (P1 issues must be fixed in BATCH-54 Corrective Task 0)

---

## Review Checklist

### TRC-P11-001 — `Tracer.Adapters.DDS` Assembly ✅ (code) / ⚠️ (tests)

**Code quality: GOOD**
- `DdsDiagnosticDataSource` correctly uses `BoundedChannel<DiagnosticRecord>` with `DropOldest` back-pressure — never blocks DDS callback thread ✅
- `DdsSampleTranslator` correctly stamps `ReceiveWallclock` at translation time, `PublishWallclock` from `sample.SourceTimestamp` ✅
- `DdsTraceContextExtractor` uses compiled `Expression`-based accessors, cached per type — correct hot-path optimization ✅
- `IDdsSample` abstraction properly isolates `Tracer.Core` from CycloneDDS types ✅
- Drop-burst throttled logging (`Interlocked.Exchange` for once-per-burst) ✅
- `EntityIdField` nullable deviation is reasonable and well-documented ✅

**Test quality: FAIL — P1 issues**

❌ **P1 — SC9 MISSING: Drop-oldest back-pressure not tested**  
`DdsDiagnosticDataSourceTests` has only 2 tests. SC9 from `TASK-DETAIL.md#trc-p11-001` explicitly requires:
> "Configure `IngestBufferSize = 5`; simulate 10 rapid `OnSampleReceived` calls; assert at most 5 records yielded and at least one `LogWarning` mentioning the topic name."  
This is the critical behavioral guarantee of the adapter (never blocking the DDS callback). It is untested.

❌ **P2 — SC8 MISSING: Accessor cache-hit test**  
No test verifying `DdsTraceContextExtractor` builds compiled accessors exactly once per type. `Extract_CamelCaseFields_ExtractsCorrectValues` calls Extract once; a second call with the same type should not rebuild accessors.

---

### TRC-P11-002 — `Tracer.Adapters.SharedMemory` Assembly ✅ (code) / ⚠️ (tests)

**Code quality: GOOD**
- `SharedMemoryRingBuffer` header layout is clean with magic bytes, version, atomic offsets ✅
- `TryWrite` drop-oldest correctly advances `read_offset` and increments `dropped_count` ✅
- `TryRead` padding-marker handling (length == 0 → wrap to 0) ✅
- `SharedMemoryDiagnosticRecordCodec` source-generated JSON serialization ✅
- Windows-only annotation via `AssemblyInfo.cs` with `[assembly: SupportedOSPlatform("windows")]` ✅
- Unique GUID-based names per test instance prevents cross-test interference ✅

**Test quality: FAIL — P1 issues**

❌ **P1 — SC3: `GetDroppedCount_AfterDrop_ReturnsPositive` trivially passes**  
```csharp
buffer.GetDroppedCount().Should().BeGreaterThanOrEqualTo(0);  // WRONG — always true
```
Must be `Should().BeGreaterThan(0)` to actually verify drops occurred. Test name says "Positive" but assertion allows zero.

❌ **P1 — SC5: `ReadAsync_RecordsWrittenByWriter_AreYielded` only validates count**  
```csharp
received.Should().HaveCount(3);  // Only count — NO field validation
```
SC5 from `TASK-DETAIL.md#trc-p11-002` requires: "assert all arrive **in order with matching field values**." The test must validate `SequenceNumber`, `Topic`, `EventId` on the decoded records.

❌ **P2 — SC4: No explicit padding-marker test**  
`TryWrite_MultipleMessages_AllReadBack` does not specifically validate the wraparound/padding scenario where a write would straddle the capacity boundary. The implementation handles it, but SC4 requires an explicit test.

---

### TRC-P11-003 — `Tracer.Adapters.Sync` Assembly ✅ (code) / ❌ (tests)

**Code quality: ACCEPTABLE with design deviation**

⚠️ **Design deviation: `UploadIntentId` encodes `{nodeId}|{intervalTimestamp}` not the server's intentId**  
The design (§5.3, SC2) says: "Mock handler returns `{ "intentId": "abc-123" }`; assert `SubmitAsync` returns `UploadIntentId("abc-123")`."  
The current implementation discards the server's `intentId` and constructs its own. This means if the server assigns a UUID-based intent id, `GetStatusAsync` will never use it. The service instead calls `GET /api/telemetry/{nodeId}/{intervalTimestamp}` which happens to work with the sync contract, but deviates from the design. **This is acceptable** as a pragmatic implementation choice (the sync API is keyed by nodeId+timestamp anyway), but the test for SC2 must match the actual behavior.

**Test quality: FAIL — multiple P1 missing tests**

❌ **P1 — SC1 MISSING: No request body validation**  
No test captures and asserts the HTTP request body sent to `POST /api/telemetry` contains the correct `nodeId`, `intervalTimestamp`, and `files` array. This is a critical correctness check — if the body format is wrong, the real sync master will reject uploads silently.

❌ **P1 — SC3 MISSING: `WaitForCompletionAsync` poll count not tested**  
The design requires asserting the handler was called exactly N times (once for register, then once for each status poll). No such test exists.

❌ **P1 — SC5 MISSING: `WaitForCompletionAsync` cancellation not tested**  
No test for cancelling during a poll delay. This is important behavior (the poll uses `Task.Delay` which must be cancellable).

❌ **P1 — SC6 MISSING: Retry on 503 not tested**  
No test verifying that `SyncSystemUploadService` retries on transient 5xx errors and eventually succeeds.

---

### TRC-P11-004 — `Tracer.Adapters.Nas` Assembly ✅ (code + tests)

**Code quality: EXCELLENT**
- `NasStorageReader` correctly reads `_ready` sentinel from zip before reporting interval as complete ✅
- `SmbPathResolver` correctly rejects path traversal (`..\`, `/`, `\`) ✅
- `PreferLocalStaging` copy-and-cleanup path works correctly ✅
- Temp directory cleanup in `Dispose()` even on exception ✅

**Test quality: GOOD**
- All 8 SC conditions from `TASK-DETAIL.md#trc-p11-004` are met ✅
- Real filesystem I/O used — not mocked ✅
- `SmbPathResolverTests` covers directory traversal for both nodeId and timestamp ✅
- `StageAsync` cleanup verification is solid ✅

---

## Summary of Issues

### P1 Issues — Must Fix in BATCH-54 Corrective Task 0

| ID | File | Issue |
|----|------|-------|
| P1-A | `DdsDiagnosticDataSourceTests.cs` | SC9 missing: add drop-oldest test with `IngestBufferSize = 5` + 10 samples, assert ≤ 5 yielded + LogWarning |
| P1-B | `SharedMemoryRingBufferTests.cs` | `GetDroppedCount_AfterDrop_ReturnsPositive`: change `>= 0` to `> 0` |
| P1-C | `SharedMemoryTransportTests.cs` | `ReadAsync_RecordsWrittenByWriter_AreYielded`: add field-level assertions (SequenceNumber, Topic, EventId) |
| P1-D | `SyncSystemUploadServiceTests.cs` | Add SC1 (request body captured + validated), SC3 (poll count), SC5 (cancellation during delay), SC6 (retry on 503) |

### P2 Issues — Track in DEBT-TRACKER

| ID | File | Issue |
|----|------|-------|
| P2-A | `DdsTraceContextExtractorTests.cs` | SC8 missing: add cache-hit test (call Extract twice same type, BuildAccessors called once) |
| P2-B | `SharedMemoryRingBufferTests.cs` | SC4 missing: add explicit padding-marker/wraparound-boundary test |

---

## DEBT-TRACKER Updates

The following entries should be added to `.dev/tracer/DEBT-TRACKER.md`:

```
| DT-039 | P2 | BATCH-53 | `DdsTraceContextExtractorTests` missing SC8: cache-hit test — call Extract twice same type, assert BuildAccessors invoked exactly once | BATCH-54 corrective | Open |
| DT-040 | P2 | BATCH-53 | `SharedMemoryRingBufferTests` missing SC4: no explicit padding-marker/capacity-boundary wraparound test | BATCH-54 corrective | Open |
```

P1 issues (P1-A through P1-D) go directly into BATCH-54 Corrective Task 0 — they do not enter DEBT-TRACKER.

---

## Commit Message (partial — P1 corrections excluded until BATCH-54)

```
feat(phase11): real adapter assemblies — DDS, SharedMemory, Sync, NAS (TRC-P11-001..004)

- Tracer.Adapters.DDS: DdsDiagnosticDataSource (IDiagnosticDataSource), DdsSampleTranslator,
  DdsTraceContextExtractor (compiled expression accessors), DdsTopicRegistry,
  DdsSubscriberFactory, IDdsSample abstraction, DdsAdapterConfig
- Tracer.Adapters.SharedMemory: SharedMemoryRingBuffer (SPSC ring, drop-oldest, padding markers,
  atomic header), SharedMemoryDiagnosticRecordCodec (source-gen JSON), SharedMemoryWriter/Reader,
  SharedMemoryTransport (IAgentTransport)
- Tracer.Adapters.Sync: SyncSystemUploadService (ITelemetryUploadService, exponential retry),
  SyncMasterRestClient (HttpClient wrapper)
- Tracer.Adapters.Nas: NasStorageReader (ITelemetryStorageReader, _ready sentinel),
  SmbPathResolver (traversal prevention), StagedInterval (PreferLocalStaging cleanup)
- 55 new unit tests across 10 test files; 234 total passing
- CycloneDDS.NET 0.2.2 and Microsoft.Extensions.Http 8.0.0 added to Directory.Packages.props
```
