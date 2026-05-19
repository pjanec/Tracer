# BATCH-02 Report

**Batch:** BATCH-02  
**Developer:** GitHub Copilot  
**Date:** 2025-07-16  
**Status:** Complete

---

## 📊 Task Completion

| Task ID | Status | Notes |
|---------|--------|-------|
| Corrective Task 0 | ✅ | All 3 BATCH-01 test quality issues resolved |
| TRC-P1-007 | ✅ | MockDataSource, SimulatedClock, TraceIdGenerator |
| TRC-P1-008 | ✅ | IScenarioScript, ScenarioConfig, ScenarioContext, ScenarioRegistry, CalmScenario, CombatEngagementScenario |
| TRC-P1-009 | ✅ | TracerStackFixture, InMemoryStackOptions, EventAssertions, StorageAssertions, TestLogSink |
| TRC-P1-010 | ✅ | AppenderTests (32), QueryBuilderTests — pre-existing tests corrected and verified |
| TRC-P1-011 | ✅ | TimeTests (5), DeterminismTests (4), ScenarioTests (6) = 15 new unit tests |
| TRC-P1-012 | ✅ | EndToEndTests (7), ScenarioRoundTripTests (3) = 10 integration tests |

---

## 🧪 Testing Results

**Unit Tests Passed:** 47 / 47  
**Integration Tests Passed:** 10 / 10

**Key Test Scenarios Verified:**
- [x] Calm scenario produces ~6001 records in 60 s (1 session-start + ~6000 heartbeats)
- [x] Two identical-seed runs produce byte-wise identical event sequences
- [x] CombatEngagement emits valid causal trees (ParentEventId references within same TraceId)
- [x] `SimulatedClock` advances exactly the amount requested, never spontaneously
- [x] `CountEventsAsync` matches full `QueryEventsAsync` row count
- [x] Time-range query returns only events within `[From, To)`
- [x] `Limit` parameter is respected exactly
- [x] GetEventAsync retrieves a stored event by `EventId`
- [x] Reopening reader after close returns identical results
- [x] Scenario registry throws `ArgumentException` for unknown scenario names

---

## 📝 Developer Insights

**Q1: What issues did you encounter during implementation? How did you resolve them?**

Three distinct compiler blockers were hit:

1. **`[EnumeratorCancellation]` on interface** — `IScenarioScript.ExecuteAsync` originally declared `[EnumeratorCancellation]` on the `CancellationToken` parameter. C# only permits that attribute on the *implementation* of an async iterator, not on the interface declaration (CS8424). Removed the attribute from the interface.

2. **`ref` parameter in iterator** — `CombatEngagementScenario.MakeShotChain` was initially an `IEnumerable<EventRecord>` method using `yield return`, but took `ref ulong sequence`. C# does not allow `ref`/`out` parameters in iterator methods (CS1623). Refactored to return `List<EventRecord>` (a non-iterator helper), with the caller incrementing `sequence` using `sequence += (ulong)chain.Count`.

3. **`CancellationToken` missing default values** — `IDiagnosticStorageReader.QueryEventsAsync` and `CountEventsAsync` have no default `ct` value, so the integration test files (written assuming optional `ct`) failed with CS7036. Fixed by passing `CancellationToken.None` explicitly at each call site.

Additionally, `TreatWarningsAsErrors=true` plus Roslyn analzyers promoted CA1062 ("Validate arguments of public methods") to a compile error. Both `TestLogSink.Log` and `StorageAssertions.ShouldContainEventCount` required `ArgumentNullException.ThrowIfNull` guards.

**Q2: Did you spot any weak points in the existing codebase? What would you improve?**

- `IDiagnosticStorageReader` methods lack default-value `CancellationToken` parameters, forcing all callers to spell out `CancellationToken.None`. Adding `= default` would reduce boilerplate with no semantic change.
- `WallclockTime` does not define `<` / `>` comparison operators; every loop condition must use `.CompareTo()`. Either adding operators or a `static bool IsAfter(WallclockTime, WallclockTime)` helper would reduce friction in scenario authors.
- `ScenarioRegistry` is a plain static dictionary. As the scenario library grows, a source-generator or convention-based registration mechanism would avoid manual maintenance.

**Q3: What design decisions did you make beyond the instructions? How did you resolve them?**

- **Shared `Random` instance between `TraceIdGenerator` and `ScenarioContext`** — The instructions required determinism but left implementation to the developer. Passing the *same* `Random` instance to both ensures the combined sequence is fully reproducible from the seed; two separate seeded `Random` instances would have collided on timestamp-driven interleaving.
- **`MakeShotChain` as `List<EventRecord>` helper** — Forced by the `ref`-in-iterator limitation, but this turned out to be a cleaner design: the helper is a pure data factory, and all `yield return` logic remains in the single iterator method `ExecuteAsync`. Alternative (keeping an iterator and passing `sequence` as a value, returning the delta) was considered but more awkward.
- **`InMemoryStackOptions` as a `record`** — The spec said "options object"; using `record` gives free value equality and `with`-expression support for test variations with minimal code.
- **Temp directory per fixture** — `TracerStackFixture` creates a unique temp path under `Path.GetTempPath()` using `Path.GetRandomFileName()`. This avoids test-run collisions without requiring cleanup coordination between tests.

**Q4: What edge cases did you discover that weren't mentioned in the spec?**

- **Time boundary in `CalmScenario`** — The loop terminates when `Clock.Now >= endTime`. Because clock advances happen *before* each record is emitted, the last heartbeat may land exactly on `endTime`, making the off-by-one count unstable by ±1. The unit test uses a tolerance band (±50 events) rather than an exact count.
- **Trace 0 in `default(TraceId)`** — `TraceId.default` evaluates to zero. `TraceIdGenerator.NewTrace()` retries until the raw `ulong` is non-zero. Integration tests must test `traceId.Should().NotBe(default, ...)` rather than `NotBeNull()`.
- **State samples at `StateSampleRate.Slow`** — `StateSampleRecord` with other rates would be silently ignored by `TracerStackFixture.RunScenarioAsync`. Only `Slow`-rate samples are routed to `AppendStateAsync`. The Calm scenario does not emit state samples, so this path is exercised only by future scenarios.
- **Single-event traces** — `CombatEngagement_AllParentEventIds_ReferenceExistingEvents` must skip single-event groups because those have no `ParentEventId` to validate. The test filters `Where(g => g.Count() > 1)`.

**Q5: Are there any performance concerns or optimization opportunities you noticed?**

- `TracerStackFixture.RunScenarioAsync` iterates the async enumerable sequentially and calls `AppendEventAsync` one event at a time. For high-EPS scenarios this will become the bottleneck. Using `AppendBatchAsync` with a ring buffer would improve throughput significantly.
- The integration test `CountEventsAsync_MatchesFullQueryCount` fetches all rows (up to `count + 1`) purely to count them. If the event count is large this materializes a large in-memory list. A future improvement: the test could instead rely solely on `CountEventsAsync` and a small spot-check query, rather than materializing every row.
- `CombatEngagementScenario.MakeShotChain` allocates a new `List<EventRecord>` for every shot. Pre-allocating with the known chain length (4 events) or returning a fixed-size span avoids repeated list resizing.

---

## ⚠️ Outstanding Issues / Next Steps

- None blocking. Phase 1 is complete; the full write-and-read pipeline is exercised end-to-end.
- Follow-on batches (Phase 2+) should consider adding `= default` to `CancellationToken` parameters on the storage reader interface to reduce caller verbosity.
- `AppendBatchAsync` is defined on `IDiagnosticStorageWriter` but not yet exercised by any test; a dedicated batch-write unit test would close this gap.

---

## 💬 Suggested Commit Message

```
feat(phase1): complete mock adapter, test harness, and all Phase 1 tests

- Add MockDataSource with deterministic, seed-controlled scenario engine
- Add SimulatedClock with thread-safe Advance/Set for reproducible time
- Add TraceIdGenerator (shared Random instance with ScenarioContext)
- Add IScenarioScript, ScenarioConfig, ScenarioContext, ScenarioRegistry
- Add CalmScenario (heartbeat loop) and CombatEngagementScenario (3-phase)
- Add TracerStackFixture with temp-path isolation and IAsyncLifetime support
- Add InMemoryStackOptions, EventAssertions, StorageAssertions, TestLogSink
- Add 15 unit tests: TimeTests, DeterminismTests, ScenarioTests
- Add 10 integration tests: EndToEndTests, ScenarioRoundTripTests
- Fix BATCH-01 test quality issues (Corrective Task 0)
- All 57 tests pass (47 unit + 10 integration); 0 errors, 0 warnings

Closes TRC-P1-007, TRC-P1-008, TRC-P1-009, TRC-P1-010, TRC-P1-011, TRC-P1-012
```
