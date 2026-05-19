# BATCH-02: Phase 1 Completion — Mock Adapter, Test Harness, All Tests + BATCH-01 Fixes

**Batch Number:** BATCH-02  
**Tasks:** Corrective Task 0 (3 test fixes), TRC-P1-007, TRC-P1-008, TRC-P1-009, TRC-P1-010, TRC-P1-011, TRC-P1-012  
**Phase:** Phase 1 — Core Foundation (completion)  
**Estimated Effort:** 16–20 hours  
**Priority:** HIGH  
**Dependencies:** BATCH-01 complete

---

## 📋 Onboarding & Workflow

### Developer Instructions

BATCH-01 established the solution scaffold, domain types, and DuckDB storage layer. BATCH-02 has two jobs:
1. **Corrective Task 0** — Fix 3 test quality issues found in BATCH-01's review (must be done FIRST)
2. **New Tasks** — Implement the Mock Adapter (`MockDataSource`, `SimulatedClock`, scenario system), the `TestHarness`, and all remaining Phase 1 unit + integration tests

After this batch, Phase 1 is complete: a developer can run a full pipeline test that generates synthetic events through `MockDataSource` → `DuckDbStorageWriter` → `DuckDbStorageReader` → domain query results.

### Required Reading (IN ORDER)

1. **Workflow Guide:** `.guides\DEV-GUIDE.md`
2. **Architecture:** `docs\tracer_architecture_v1.md` — §2, §4, §19 (Test Harness and Mock Adapters), §19.1 (Scenario Generators), §19.2 (Test Fixture), §19.3 (Test Categories)
3. **Phase 1 Design:** `docs\tracer_phase1_design.md` — §5 (Mock Adapter), §6 (TestHarness), §7 (Tests); read completely
4. **Task Definitions:** `docs\TASK-DETAIL.md` — TRC-P1-007 through TRC-P1-012 success conditions
5. **Previous Review:** `.dev\tracer\reviews\BATCH-01-REVIEW.md` — understand what needs fixing first
6. **Code Standards:** `.guides\CODE-STANDARDS.md` — §0 (Test Quality Checklist), §1 (No Magic Numbers)

### Source Code Location

- **Mock Adapter:** `src\Tracer.Adapters.Mock\` (stub exists from BATCH-01, needs full implementation)
- **Test Harness:** `src\Tracer.TestHarness\` (stub exists from BATCH-01, needs full implementation)
- **Unit Tests:** `tests\Tracer.Tests.Unit\` (existing test files to be fixed; new Mock/ tests to add)
- **Integration Tests:** `tests\Tracer.Tests.Integration\` (new files to create)
- **All paths relative to repo root:** `d:\Work\Tracer\`

### Report Submission

**When done, submit your report to:**  
`.dev\tracer\reports\BATCH-02-REPORT.md`

**If you have questions, create:**  
`.dev\tracer\questions\BATCH-02-QUESTIONS.md`

---

## Context

BATCH-01 built the storage foundation. BATCH-02 completes Phase 1 by adding:
- The **deterministic mock adapter** (SimulatedClock, TraceIdGenerator, MockDataSource, two scenario scripts)
- The **TracerStackFixture** for integration tests
- All **unit tests** for Core, Storage, and Mock assemblies
- All **integration tests** exercising the full pipeline

The Phase 1 success criteria (see `docs\tracer_phase1_design.md §1.3`) require all these to be in place.

**Related Tasks:**
- [TRC-P1-007](../../../docs/TASK-DETAIL.md#trc-p1-007--traceradaptersmock-mockdatasource--simulatedclock) — MockDataSource & SimulatedClock
- [TRC-P1-008](../../../docs/TASK-DETAIL.md#trc-p1-008--traceradaptersmock-scenario-system) — Scenario System
- [TRC-P1-009](../../../docs/TASK-DETAIL.md#trc-p1-009--tracertestharness) — TestHarness
- [TRC-P1-010](../../../docs/TASK-DETAIL.md#trc-p1-010--unit-tests-core--storage) — Unit Tests: Core & Storage
- [TRC-P1-011](../../../docs/TASK-DETAIL.md#trc-p1-011--unit-tests-mock-adapter) — Unit Tests: Mock Adapter
- [TRC-P1-012](../../../docs/TASK-DETAIL.md#trc-p1-012--integration-tests-end-to-end) — Integration Tests: End-to-End

---

## 🎯 Batch Objectives

1. Fix 3 test quality issues from BATCH-01 (Corrective Task 0)
2. Implement the complete Mock Adapter assembly
3. Implement the complete TestHarness assembly
4. Populate all unit tests (Core, Storage, Mock)
5. Implement all integration tests covering the full Phase 1 pipeline
6. Phase 1 success criteria met — `dotnet test` across both test projects passes

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: You MUST complete tasks in sequence with passing tests:**

1. **Corrective Task 0:** Fix the 3 test issues → re-run tests → ALL 32 tests still pass ✅
2. **TRC-P1-007:** SimulatedClock + TraceIdGenerator + MockDataSource → write `TimeTests.cs` tests → ALL pass ✅
3. **TRC-P1-008:** Scenario system (IScenarioScript, ScenarioRegistry, CalmScenario, CombatEngagementScenario) → write `ScenarioTests.cs` → ALL pass ✅
4. **TRC-P1-009:** TracerStackFixture + TestHarness assertions → solution builds ✅
5. **TRC-P1-010 + TRC-P1-011:** Complete DeterminismTests, TimeTests (if not done), ensure all unit tests pass → `dotnet test Tracer.Tests.Unit` green ✅
6. **TRC-P1-012:** Integration tests → `dotnet test Tracer.Tests.Integration` green ✅

**DO NOT** move to the next task until:
- ✅ Current task implementation complete
- ✅ Current task tests written (per success conditions)
- ✅ **ALL tests passing** (including all previous task tests)

---

## ✅ Tasks

### Corrective Task 0: Fix BATCH-01 Test Quality Issues

**Review reference:** `.dev\tracer\reviews\BATCH-01-REVIEW.md`

Fix exactly these three issues before writing any new code:

#### Fix 1: `AppendBatch_MixedRecords_RoutesCorrectly` — Add slow_state verification

**File:** `tests\Tracer.Tests.Unit\Storage\AppenderTests.cs`

After writing 5 events + 3 slow-state records and flushing, add a raw DuckDB connection check that the `slow_state` table has exactly 3 rows. Use the same `QueryScalarAsync` pattern from `SchemaTests.cs`. Update the batch from {2 events, 1 slow-state, 1 fast-state} to {5 events, 3 slow-state, 1 fast-state} to match the spec exactly, and assert both `events` count == 5 and `slow_state` count == 3.

#### Fix 2: `Build_MinSeverityWarning_ExpandsToInClause` — Add negative case + value verification

**File:** `tests\Tracer.Tests.Unit\Storage\QueryBuilderTests.cs`

Add assertions that:
- `sev0` parameter's value equals `"Warning"`
- `sev1` parameter's value equals `"Error"`
- No parameter with value `"Info"` exists: `parameters.Should().NotContain(p => p.Value!.ToString() == "Info")`

#### Fix 3: `AppendEvent_1000Records_RoundTrip` — Add specific field verification

**File:** `tests\Tracer.Tests.Unit\Storage\AppenderTests.cs`

After the round-trip, retrieve a specific known record (e.g., query for `EventId == 500`) and verify these fields exactly match what `MakeEvent(500)` would produce:
- `EventId.Value == 500`
- `TraceId.Value == 42`
- `PublisherNode.Value == "pub"`
- `Topic.Value == "test.topic"`
- `PayloadJson` equals `"{\"seq\":500}"`

---

### Task 1: MockDataSource & SimulatedClock (TRC-P1-007)

**Task Definition:** See [TASK-DETAIL.md — TRC-P1-007](../../../docs/TASK-DETAIL.md#trc-p1-007--traceradaptersmock-mockdatasource--simulatedclock)  
**Design Reference:** [tracer_phase1_design.md §5.1](../../../docs/tracer_phase1_design.md#51-design-principles), [§5.2](../../../docs/tracer_phase1_design.md#52-simulatedclock), [§5.4](../../../docs/tracer_phase1_design.md#54-traceidgenerator-deterministic), [§5.7](../../../docs/tracer_phase1_design.md#57-mockdatasource)

The `src\Tracer.Adapters.Mock\` project already exists with a stub `MockDataSource.cs` and `SimulatedClock.cs`. Replace the stubs with full implementations per the design.

Implement:
- `SimulatedClock` — thread-safe `IClock` impl; `Now`, `Advance(TimeSpan)`, `Set(WallclockTime)` with internal `lock`
- `Generation\TraceIdGenerator` — seeded `Random`; deterministic `NewTrace()` (never returns `TraceId.None`) and `NewEvent()` (starts at 1, monotonically increasing)
- `MockDataSource` — accepts `(string scenarioName, ScenarioConfig config)`; constructs `SimulatedClock`, `Random(config.Seed)`, `TraceIdGenerator`, `ScenarioContext`; delegates `ReadAsync` to `ScenarioRegistry.Get(scenarioName).ExecuteAsync(_context, ct)`; exposes `Clock` property

**Unit tests to write** in `tests\Tracer.Tests.Unit\Core\TimeTests.cs` (see TRC-P1-007 success condition 10 for exact names):
- 5 tests for `SimulatedClock` and `WallclockTime`

---

### Task 2: Scenario System (TRC-P1-008)

**Task Definition:** See [TASK-DETAIL.md — TRC-P1-008](../../../docs/TASK-DETAIL.md#trc-p1-008--traceradaptersmock-scenario-system)  
**Design Reference:** [tracer_phase1_design.md §5.1](../../../docs/tracer_phase1_design.md#51-design-principles), [§5.3](../../../docs/tracer_phase1_design.md#53-scenario-script-abstraction), [§5.5](../../../docs/tracer_phase1_design.md#55-first-scenarios), [§5.6](../../../docs/tracer_phase1_design.md#56-scenarioregistry)

Implement in `src\Tracer.Adapters.Mock\`:
- `Scenarios\IScenarioScript.cs` — `Name { get; }` and `ExecuteAsync(ScenarioContext, CancellationToken)` with `[EnumeratorCancellation]` on ct
- `Scenarios\ScenarioContext.cs` — holds `SimulatedClock`, `TraceIdGenerator`, `ScenarioConfig`, `Random`
- `Scenarios\ScenarioConfig.cs` — with defaults: `Duration = 5min`, `NodeCount = 3`, `EntityCount = 10`, `EventsPerSecond = 100`, `Seed = 42`
- `Scenarios\ScenarioRegistry.cs` — `Get(name)` throws `ArgumentException` for unknown names; `AvailableScenarios` returns collection
- `Scenarios\Scripts\CalmScenario.cs` — first record is session-start; terminates at `StartTime + Duration`; 100 eps; deterministic
- `Scenarios\Scripts\CombatEngagementScenario.cs` — three phases (approach/engagement/withdrawal); causal chains `shot_fired → projectile_spawn → projectile_impact → damage_applied`; all events have non-null `ScenarioPhase`

**Key behavioral requirements:**
- `CalmScenario` with `EventsPerSecond = 100` and `Duration = 60s` yields between 5,950 and 6,050 records total
- `CombatEngagementScenario` produces valid causal trees: every record with a non-null `ParentEventId` has a parent that appears EARLIER in the output
- Both scenarios are deterministic: same seed → same sequence across two independent runs

**Unit tests to write** in `tests\Tracer.Tests.Unit\Mock\ScenarioTests.cs` (see TRC-P1-008 success condition 11 for exact names):
- 6 tests covering session-start, duration termination, event count, causal tree validity, non-null phase, registry exception

---

### Task 3: TestHarness (TRC-P1-009)

**Task Definition:** See [TASK-DETAIL.md — TRC-P1-009](../../../docs/TASK-DETAIL.md#trc-p1-009--tracertestharness)  
**Design Reference:** [tracer_phase1_design.md §6](../../../docs/tracer_phase1_design.md#6-tracertestharness), [§6.1](../../../docs/tracer_phase1_design.md#61-tracerstackfixture), [§6.2](../../../docs/tracer_phase1_design.md#62-fluent-assertions-extensions)

The stub `TracerStackFixture.cs` exists. Replace it with the full implementation:
- `TracerStackFixture.CreateAsync(scenarioName, seed, duration?, options?, ct)` — creates temp directory, `DuckDbStorageWriter`, `MockDataSource`
- `TracerStackFixture.RunScenarioAsync(ct)` — iterates `DataSource.ReadAsync`, dispatches to writer, calls `FlushAsync`, opens `DuckDbStorageReader`
- `TracerStackFixture.DisposeAsync` — closes reader/writer, deletes temp directory; idempotent
- `InMemoryStackOptions` — `NodeCount = 3`, `EntityCount = 10`, `EventsPerSecond = 100`; init-settable
- `Assertions\EventAssertions.cs` — `ShouldFormValidTrace` and `ShouldBeTimeOrdered` extension methods throwing `AssertionException` on violations
- `Assertions\StorageAssertions.cs` — `ShouldContainEventCount` extension method
- `Diagnostics\TestLogSink.cs` — captures log messages; `GetMessages()` returns `List<string>`

**Important:** `Tracer.TestHarness.csproj` must NOT reference `xunit` — it's a test helper library, not a test project.

---

### Task 4: Unit Tests — Core & Storage Complete (TRC-P1-010)

**Task Definition:** See [TASK-DETAIL.md — TRC-P1-010](../../../docs/TASK-DETAIL.md#trc-p1-010--unit-tests-core--storage)  
**Design Reference:** [tracer_phase1_design.md §7.1](../../../docs/tracer_phase1_design.md#71-unit-tests-tracertestsunit)

Ensure ALL tests listed in TRC-P1-010 success conditions 1–6 are present and passing. The Corrective Task 0 fixes handle conditions 4–6 (Storage tests). Conditions 1–3 (Core tests) should already be satisfied from BATCH-01.

Run `dotnet test tests\Tracer.Tests.Unit --configuration Release` and verify it passes.

---

### Task 5: Unit Tests — Mock Adapter (TRC-P1-011)

**Task Definition:** See [TASK-DETAIL.md — TRC-P1-011](../../../docs/TASK-DETAIL.md#trc-p1-011--unit-tests-mock-adapter)  
**Design Reference:** [tracer_phase1_design.md §7.1](../../../docs/tracer_phase1_design.md#71-unit-tests-tracertestsunit) (Mock subsections)

Implement in `tests\Tracer.Tests.Unit\Mock\`:
- `DeterminismTests.cs` — 4 tests (see TRC-P1-011 success condition 1 for exact names):
  - `MockDataSource_SameSeedSameScenario_ProducesIdenticalSequence` — two instances, same seed; element-by-element equality across ALL fields
  - `MockDataSource_DifferentSeeds_ProduceDifferentSequences` — seeds 1 and 2; first records have different `TraceId`
  - `TraceIdGenerator_SameSeed_ProducesSameTraceIds` — five consecutive calls, both generators equal
  - `SimulatedClock_AdvancesMatchAcrossRuns`

**Critical:** `MockDataSource_SameSeedSameScenario_ProducesIdenticalSequence` must compare element-by-element across ALL fields (`EventId`, `TraceId`, `SequenceNumber`, `PublishWallclock`, `PayloadJson`), not just count.

---

### Task 6: Integration Tests — End-to-End (TRC-P1-012)

**Task Definition:** See [TASK-DETAIL.md — TRC-P1-012](../../../docs/TASK-DETAIL.md#trc-p1-012--integration-tests-end-to-end)  
**Design Reference:** [tracer_phase1_design.md §7.2](../../../docs/tracer_phase1_design.md#72-integration-tests-tracertestsintegration), [§1.3](../../../docs/tracer_phase1_design.md#13-success-criteria)

Implement in `tests\Tracer.Tests.Integration\`:

**`EndToEndTests.cs`** — 7 tests using `TracerStackFixture`:
1. `CalmScenario_1Minute_QueryReturnsExpectedEventCount` — count from `CountEventsAsync` equals count from iterating `ReadAsync`
2. `CombatEngagement_QueryByTraceId_ReturnsValidCausalTree` — query by one `TraceId`, result passes `ShouldFormValidTrace()`
3. `QueryByEntity_ReturnsOnlyMatchingEntity` — every returned record has matching `EntityId`
4. `QueryWithTimeRange_ReturnsOnlyEventsInRange` — every returned event's `PublishWallclock` in [From, To)
5. `QueryWithLimit_RespectsLimit` — result has exactly 10 elements
6. `GetEventAsync_KnownEventId_ReturnsMatchingEvent` — retrieved event has same `EventId` and `TraceId`
7. `CountEventsAsync_MatchesFullQueryCount` — count equals full query count

**`ScenarioRoundTripTests.cs`** — 3 tests:
1. `CalmScenario_WriteClosedReopened_QueryResultsIdentical` — dispose writer, reopen reader, results match
2. `CalmScenario_TwoRunsSameSeed_ProduceBytewiseSameEventData` — two separate runs, same seed; element-by-element equality
3. `CombatEngagement_AllParentEventIds_ReferenceExistingEvents` — `ShouldFormValidTrace` on full result set grouped by `TraceId`

**Critical constraint:** `Tracer.Tests.Integration.csproj` must NOT directly reference `Tracer.Storage.DuckDB` or `Tracer.Adapters.Mock` — access through `Tracer.TestHarness` only.

---

## 🧪 Testing Requirements

**Unit tests (Tracer.Tests.Unit):** Minimum 48 tests total after BATCH-02
- Core/RecordTests.cs: 6 tests (existing)
- Core/TraceIdTests.cs: 7 tests (existing)
- Core/TimeTests.cs: 5 tests (NEW — TRC-P1-007)
- Storage/SchemaTests.cs: 4 tests (existing)
- Storage/AppenderTests.cs: 6 tests (existing, fixed)
- Storage/QueryBuilderTests.cs: 9 tests (existing, fixed)
- Mock/DeterminismTests.cs: 4 tests (NEW — TRC-P1-011)
- Mock/ScenarioTests.cs: 6 tests (NEW — TRC-P1-008)

**Integration tests (Tracer.Tests.Integration):** 10 tests
- EndToEndTests.cs: 7 tests
- ScenarioRoundTripTests.cs: 3 tests

**Test quality requirements:**
- `DeterminismTests` must compare all record fields, not just count
- Integration tests must use `TracerStackFixture` — no manual stack construction
- `ShouldFormValidTrace` must be used for causal tree tests (not a manual loop)
- `CalmScenario_TwoRunsSameSeed_ProduceBytewiseSameEventData` must do element-by-element field comparison, not just count
- `QueryWithTimeRange_ReturnsOnlyEventsInRange` must check EVERY record (not just a sample) 

**Run before submitting:**
```
cd d:\Work\Tracer
dotnet test tests\Tracer.Tests.Unit --configuration Release
dotnet test tests\Tracer.Tests.Integration --configuration Release
```
Both must exit 0.

---

## ⚠️ Quality Standards

**❗ REPORT REQUIRED SECTIONS** — This batch's report MUST include Developer Insights Q1–Q5 AND a suggested commit message. These were missing from BATCH-01. Missing them will cause rejection.

**❗ NO MAGIC NUMBERS** — All scenario counts, durations, tolerances must be named constants.

**❗ DETERMINISM IS NON-NEGOTIABLE** — Two `CalmScenario` runs with the same seed MUST produce identical records. If they don't, that's a P1 bug — debug and fix it before submitting.

**❗ CAUSAL TREE VALIDITY** — In `CombatEngagementScenario`, every `ParentEventId` must reference a record that appears EARLIER in the output. Verify this in the test.

**❗ TESTHARNESS ISOLATION** — Integration tests MUST only access storage and mock through `TracerStackFixture`. No direct `new DuckDbStorageWriter(...)` in integration tests.

**❗ COMPLETE THE BATCH** — Do not stop midway. Run tests after each task. Fix all failures. Write the report only when everything is green. No asking for permission to do obvious things.

---

## 📊 Report Requirements

**Submit to:** `.dev\tracer\reports\BATCH-02-REPORT.md`

Include:
1. **Corrective task completion** — confirm all 3 test fixes applied; show updated test still passes
2. **Task completion status** — for each TRC-P1-007 through TRC-P1-012
3. **Test results** — full output of both:
   - `dotnet test tests\Tracer.Tests.Unit --configuration Release`
   - `dotnet test tests\Tracer.Tests.Integration --configuration Release`
4. **Developer Insights (MANDATORY):**
   - **Q1:** What issues did you encounter? How were they resolved?
   - **Q2:** What weak points did you spot in the existing code or design?
   - **Q3:** What design decisions did you make beyond the instructions? What alternatives did you consider?
   - **Q4:** What edge cases did you discover?
   - **Q5:** Any performance observations about the DuckDB layer, scenario execution, or test run times?
5. **Suggested commit message** (MANDATORY)

---

## 🎯 Success Criteria

This batch is DONE when:
- [ ] Corrective Task 0: All 3 BATCH-01 test fixes applied; all 32 existing tests still pass
- [ ] TRC-P1-007: `SimulatedClock`, `TraceIdGenerator`, `MockDataSource` implemented; `TimeTests.cs` (5 tests) pass
- [ ] TRC-P1-008: Full scenario system + both scenarios; `ScenarioTests.cs` (6 tests) pass
- [ ] TRC-P1-009: `TracerStackFixture`, assertions, `TestLogSink` implemented; solution builds
- [ ] TRC-P1-010: All unit tests for Core & Storage passing (`dotnet test Tracer.Tests.Unit` green)
- [ ] TRC-P1-011: `DeterminismTests.cs` (4 tests) pass; `dotnet test --filter "FullyQualifiedName~Mock" Tracer.Tests.Unit` exits 0
- [ ] TRC-P1-012: All integration tests pass; `dotnet test Tracer.Tests.Integration` exits 0 under 30 seconds
- [ ] Both test projects pass clean on `dotnet test ... --configuration Release`
- [ ] Report submitted with Developer Insights and commit message

---

## 📚 Reference Materials

- **Task Definitions:** `docs\TASK-DETAIL.md` — TRC-P1-007 through TRC-P1-012
- **Phase 1 Design:** `docs\tracer_phase1_design.md` — §5 (Mock Adapter), §6 (TestHarness), §7 (Tests), §1.3 (Success Criteria)
- **Architecture:** `docs\tracer_architecture_v1.md` — §19 (Test Harness), §19.1, §19.2, §19.3
- **Previous Review:** `.dev\tracer\reviews\BATCH-01-REVIEW.md`
- **Code Standards:** `.guides\CODE-STANDARDS.md` — §0, §1
