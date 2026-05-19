# BATCH-01: Phase 1 Foundation — Scaffold, Core Types, and Storage Layer

**Batch Number:** BATCH-01  
**Tasks:** TRC-P1-001, TRC-P1-002, TRC-P1-003, TRC-P1-004, TRC-P1-005, TRC-P1-006  
**Phase:** Phase 1 — Core Foundation  
**Estimated Effort:** 16–20 hours  
**Priority:** HIGH  
**Dependencies:** None (greenfield)

---

## 📋 Onboarding & Workflow

### Developer Instructions

This is the very first batch for the Tracer project. You are building from a completely empty repository. Your job is to create the entire solution scaffold, all core domain types, the query model, and the DuckDB storage layer — both write and read paths — along with their unit tests. No user-facing functionality exists after this batch, but the foundation must be solid because every subsequent phase builds on it.

### Required Reading (IN ORDER)

1. **Workflow Guide:** `.guides\DEV-GUIDE.md` — how to work with batches
2. **Architecture:** `docs\tracer_architecture_v1.md` — overall design principles, §2 (Core Design Principles), §4 (Terminology), §5 (Data Categories), §17 (Performance Targets), §18 (Build Sequence)
3. **Phase 1 Design:** `docs\tracer_phase1_design.md` — this is your primary reference. Read it completely before writing any code.
4. **Task Definitions:** `docs\TASK-DETAIL.md` — detailed success conditions for TRC-P1-001 through TRC-P1-006
5. **Code Standards:** `.guides\CODE-STANDARDS.md` — review §0 (Test Quality Checklist) and §1 (No Magic Numbers)

### Source Code Location

- **Primary Work Area:** `src/` (to be created)
- **Test Projects:** `tests/` (to be created)
- **All paths are relative to the repo root:** `d:\Work\Tracer\`

### Report Submission

**When done, submit your report to:**  
`.dev/tracer/reports/BATCH-01-REPORT.md`

**If you have questions, create:**  
`.dev/tracer/questions/BATCH-01-QUESTIONS.md`

---

## Context

This batch establishes the lowest two layers of the Tracer stack:

1. **Solution scaffold** — the `Tracer.sln`, six `.csproj` files, `Directory.Build.props`, `Directory.Packages.props`, `global.json`, `.editorconfig`, and CI workflow
2. **`Tracer.Core`** — pure domain types and interface contracts; no third-party dependencies permitted
3. **`Tracer.Storage.DuckDB`** — the DuckDB-backed persistence layer (write path: `DuckDbStorageWriter`; read path: `DuckDbStorageReader`; query translation: `EventQueryBuilder`)
4. **Unit tests** — `Tracer.Tests.Unit` test classes for Core and Storage (specified in task success conditions)

**Related Tasks:**
- [TRC-P1-001](../../../docs/TASK-DETAIL.md#trc-p1-001--solution--project-scaffold) — Solution scaffold
- [TRC-P1-002](../../../docs/TASK-DETAIL.md#trc-p1-002--tracercore-domain-types) — Domain types
- [TRC-P1-003](../../../docs/TASK-DETAIL.md#trc-p1-003--tracercore-abstractions--error-types) — Abstractions & error types
- [TRC-P1-004](../../../docs/TASK-DETAIL.md#trc-p1-004--tracercore-query-model) — Query model
- [TRC-P1-005](../../../docs/TASK-DETAIL.md#trc-p1-005--tracerstorageduckdb-schema--appenders) — DuckDB schema & appenders
- [TRC-P1-006](../../../docs/TASK-DETAIL.md#trc-p1-006--tracerstorageduckdb-query-layer) — DuckDB query layer

---

## 🎯 Batch Objectives

Produce a compilable solution that:
- Enforces nullable, warnings-as-errors, and centralized package versions
- Defines all domain vocabulary types (`EventRecord`, `StateSampleRecord`, identity structs, time types, value objects)
- Defines the three core interface seams (`IDiagnosticDataSource`, `IDiagnosticStorageWriter`, `IDiagnosticStorageReader`) and `IClock`
- Implements `DuckDbStorageWriter` (Appender-based ingestion) and `DuckDbStorageReader` (parameterized SQL queries)
- Passes all unit tests in `Tracer.Tests.Unit` for `Core/` and `Storage/` (listed in task success conditions)

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: You MUST complete tasks in sequence with passing tests:**

1. **TRC-P1-001:** Scaffold → build passes → `dotnet build Tracer.sln --configuration Release` exits 0 ✅
2. **TRC-P1-002:** Implement domain types → write specified unit tests → ALL pass ✅
3. **TRC-P1-003:** Implement abstractions → solution builds clean ✅
4. **TRC-P1-004:** Implement query model → add `EventFilter_All_HasNoConstraints` test → passes ✅
5. **TRC-P1-005:** Implement DuckDB schema & appenders → write specified unit tests → ALL pass ✅
6. **TRC-P1-006:** Implement query layer → write specified unit tests → ALL pass ✅

**DO NOT** move to the next task until:
- ✅ Current task implementation complete
- ✅ Current task tests written (as specified in task success conditions)
- ✅ **ALL tests passing** (including all previous task tests)

After each task's tests pass, run `dotnet build Tracer.sln --configuration Release` and verify zero warnings.

---

## ✅ Tasks

### Task 1: Solution & Project Scaffold (TRC-P1-001)

**Task Definition:** See [TASK-DETAIL.md — TRC-P1-001](../../../docs/TASK-DETAIL.md#trc-p1-001--solution--project-scaffold)  
**Design Reference:** [tracer_phase1_design.md §2](../../../docs/tracer_phase1_design.md#2-solution-and-project-layout) — §2.1 (Repository Structure), §2.2 (Project File Conventions), §2.3 (Dependency Graph); §7.4 (CI Configuration); §8 (Coding Standards)

Create the full repository skeleton per the directory layout shown in §2.1 of the phase 1 design. The six projects are:
- `src/Tracer.Core/Tracer.Core.csproj`
- `src/Tracer.Storage.DuckDB/Tracer.Storage.DuckDB.csproj`
- `src/Tracer.Adapters.Mock/Tracer.Adapters.Mock.csproj`
- `src/Tracer.TestHarness/Tracer.TestHarness.csproj`
- `tests/Tracer.Tests.Unit/Tracer.Tests.Unit.csproj`
- `tests/Tracer.Tests.Integration/Tracer.Tests.Integration.csproj`

Key requirements (see TRC-P1-001 success conditions 1–10 in TASK-DETAIL.md):
- `global.json` pins .NET SDK `8.0.x` with `"rollForward": "latestFeature"`
- `Directory.Build.props` sets all listed properties exactly as shown in §2.2
- `Directory.Packages.props` pins all listed package versions exactly as shown in §2.2
- `.editorconfig` enables CA1051 and CA1062 as errors, disables CA2007, sets IDE0079 to error per §8.4
- A CI workflow runs on `windows-latest` per §7.4
- A CI check fails if `Tracer.Core.csproj` contains any third-party `<PackageReference>`

**Verification:** `dotnet build Tracer.sln --configuration Release` exits 0 with zero warnings/errors on a clean checkout.

---

### Task 2: Tracer.Core — Domain Types (TRC-P1-002)

**Task Definition:** See [TASK-DETAIL.md — TRC-P1-002](../../../docs/TASK-DETAIL.md#trc-p1-002--tracercore-domain-types)  
**Design Reference:** [tracer_phase1_design.md §3.1](../../../docs/tracer_phase1_design.md#31-record-types), [§3.2](../../../docs/tracer_phase1_design.md#32-identity-types), [§3.3](../../../docs/tracer_phase1_design.md#33-time-types)

Implement in `src/Tracer.Core/`:
- `Records/DiagnosticRecord.cs`, `Records/EventRecord.cs`, `Records/StateSampleRecord.cs` — per the exact signatures in §3.1
- `Identity/TraceId.cs`, `Identity/EventId.cs`, `Identity/AgentId.cs` — per §3.2
- `Domain/TopicName.cs`, `Domain/EntityId.cs`, `Domain/Severity.cs`, `Domain/SessionMarker.cs` — per §3.2 and §3.4
- `Time/WallclockTime.cs`, `Time/IClock.cs` — per §3.3

All types are in namespaces matching their folder paths. `Tracer.Core.csproj` must have zero third-party package references.

**Unit tests to write** (see TRC-P1-002 success conditions 9–10 for exact method names):
- `tests/Tracer.Tests.Unit/Core/RecordTests.cs` — 4 tests
- `tests/Tracer.Tests.Unit/Core/TraceIdTests.cs` — 7 tests

---

### Task 3: Tracer.Core — Abstractions & Error Types (TRC-P1-003)

**Task Definition:** See [TASK-DETAIL.md — TRC-P1-003](../../../docs/TASK-DETAIL.md#trc-p1-003--tracercore-abstractions--error-types)  
**Design Reference:** [tracer_phase1_design.md §3.5](../../../docs/tracer_phase1_design.md#35-core-abstractions-interfaces), [§11](../../../docs/tracer_phase1_design.md#11-error-handling), [§11.1](../../../docs/tracer_phase1_design.md#111-exception-types), [§11.2](../../../docs/tracer_phase1_design.md#112-argument-validation)

Implement in `src/Tracer.Core/`:
- `Abstractions/IDiagnosticDataSource.cs` — exactly one method as specified in §3.5
- `Abstractions/IDiagnosticStorageWriter.cs` — extends `IAsyncDisposable`; four methods per §3.5
- `Abstractions/IDiagnosticStorageReader.cs` — extends `IAsyncDisposable`; three methods per §3.5
- `Time/IClock.cs` — one property
- `Errors/TracerException.cs`, `Errors/TracerStorageException.cs`, `Errors/TracerScenarioException.cs` — per §11.1

No separate unit tests for TRC-P1-003 — correctness is verified at compile time when DuckDbStorageWriter and MockDataSource implement these interfaces.

---

### Task 4: Tracer.Core — Query Model (TRC-P1-004)

**Task Definition:** See [TASK-DETAIL.md — TRC-P1-004](../../../docs/TASK-DETAIL.md#trc-p1-004--tracercore-query-model)  
**Design Reference:** [tracer_phase1_design.md §3.4](../../../docs/tracer_phase1_design.md#34-filters-and-queries)

Implement in `src/Tracer.Core/Queries/`:
- `EventFilter.cs` — sealed record with all nullable filter properties; `All`, `ForTrace`, `ForEntity` static factories
- `EventQuery.cs` — sealed record with `Filter`, `Limit` (default 1000), `Offset` (default 0), `Order`
- `QueryBucket.cs` — readonly record struct with `FiveMinutes`, `ThirtySeconds`, `FiveSeconds`
- `QueryOrder.cs` — enum with three members: `PublishTimeAscending`, `PublishTimeDescending`, `SequenceNumberAscending`

**Unit test to add to** `tests/Tracer.Tests.Unit/Storage/QueryBuilderTests.cs`:
- `EventFilter_All_HasNoConstraints` (see TRC-P1-004 success condition 9)

---

### Task 5: Tracer.Storage.DuckDB — Schema & Appenders (TRC-P1-005)

**Task Definition:** See [TASK-DETAIL.md — TRC-P1-005](../../../docs/TASK-DETAIL.md#trc-p1-005--tracerstorageduckdb-schema--appenders)  
**Design Reference:** [tracer_phase1_design.md §4](../../../docs/tracer_phase1_design.md#4-tracerstorageduckdb-persistence), [§4.1](../../../docs/tracer_phase1_design.md#41-duckdb-version-and-library), [§4.2](../../../docs/tracer_phase1_design.md#42-schema-version-1), [§4.3](../../../docs/tracer_phase1_design.md#43-duckdbstoragewriter-implementation), [§4.6](../../../docs/tracer_phase1_design.md#46-batchbuffer)

Implement in `src/Tracer.Storage.DuckDB/`:
- `Schema/SchemaV1.cs` — DDL constants for `events`, `slow_state`, `_schema_meta` tables and all six indexes; `Version = 1`
- `DuckDbStorageWriter.cs` — implements `IDiagnosticStorageWriter`; factory method `CreateAsync`; appender-based writes; idempotent init
- `Ingestion/BatchBuffer.cs` — `ShouldFlush` by count/age; `DrainAll`

Key behavior:
- `AppendStateAsync` with `FastRate` throws `NotSupportedException`
- `AppendBatchAsync` silently skips fast-state records, routes events and slow-state
- `FlushAsync` must make records visible to a reader immediately after returning
- `DisposeAsync` is idempotent (no throw on second call)

**Unit tests to write** (see TRC-P1-005 success conditions 10–11 for exact method names):
- `tests/Tracer.Tests.Unit/Storage/SchemaTests.cs` — 4 tests
- `tests/Tracer.Tests.Unit/Storage/AppenderTests.cs` — 6 tests

---

### Task 6: Tracer.Storage.DuckDB — Query Layer (TRC-P1-006)

**Task Definition:** See [TASK-DETAIL.md — TRC-P1-006](../../../docs/TASK-DETAIL.md#trc-p1-006--tracerstorageduckdb-query-layer)  
**Design Reference:** [tracer_phase1_design.md §4.4](../../../docs/tracer_phase1_design.md#44-duckdbstoragereader-implementation), [§4.5](../../../docs/tracer_phase1_design.md#45-eventquerybuilder)

Implement in `src/Tracer.Storage.DuckDB/`:
- `Queries/EventQueryBuilder.cs` — `Build(EventQuery)` and `BuildCount(EventFilter)` returning parameterized SQL; never concatenate user values into SQL directly
- `Internal/Mapping.cs` — `MapEventRecord` row-to-domain mapper
- `DuckDbStorageReader.cs` — implements `IDiagnosticStorageReader`; `OpenAsync` in read-only mode; executes built queries; maps results

**Critical security requirement:** SQL injection must be impossible. Every user-supplied filter value (including `PayloadSearch`) must be a named parameter, never concatenated into the SQL string. See TRC-P1-006 success conditions 5–6.

**Unit tests to write** (see TRC-P1-006 success condition 11 for exact method names):
- `tests/Tracer.Tests.Unit/Storage/QueryBuilderTests.cs` — 8 additional tests (including `Build_SqlInjectionAttempt_IsParameterized`)

---

## 🧪 Testing Requirements

**Test project:** `tests/Tracer.Tests.Unit/Tracer.Tests.Unit.csproj`  
**References:** `Tracer.Core`, `Tracer.Storage.DuckDB`, `Tracer.Adapters.Mock`, `xunit`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`, `FluentAssertions` — does NOT reference `Tracer.TestHarness`

**Total tests in this batch (minimum):** ~25 unit tests across 5 test classes

**Test classes:**
- `Core/RecordTests.cs` — 4 tests (TRC-P1-002)
- `Core/TraceIdTests.cs` — 7 tests (TRC-P1-002)
- `Storage/SchemaTests.cs` — 4 tests (TRC-P1-005)
- `Storage/AppenderTests.cs` — 6 tests (TRC-P1-005)
- `Storage/QueryBuilderTests.cs` — 9 tests total (1 from TRC-P1-004 + 8 from TRC-P1-006)

**Test quality expectations:**

- Every test must verify ACTUAL BEHAVIOR, not just that something compiled or isn't null
- `AppenderTests.cs` tests must write real data to a temp DuckDB file, flush, reopen, and query back — verifying field values match
- `QueryBuilderTests.cs` must check actual SQL strings and parameter collections — not just "no exception"
- The SQL injection test must explicitly verify the malicious string appears only as a parameter value and NOT in the SQL string itself
- `SchemaTests.cs` must query the DuckDB catalog to verify index existence — not just assume they were created

**Not acceptable:**
- Tests that only check `Assert.NotNull(someObject)`
- Tests that only check `Assert.True(result != null)`
- Tests that write to DuckDB but never read back and verify field values
- Tests that build SQL but never check the resulting SQL string content

**Run and verify before submitting:**
```
cd d:\Work\Tracer
dotnet test tests\Tracer.Tests.Unit --configuration Release
```
All tests must pass. Zero failing tests.

---

## ⚠️ Quality Standards

**❗ NO MAGIC NUMBERS** — All buffer sizes, thresholds, capacity limits must be named constants. See `.guides\CODE-STANDARDS.md §1`.

**❗ SQL INJECTION PREVENTION** — `EventQueryBuilder` must never concatenate user input into SQL. Use named parameters for every user-supplied value. This is tested by `Build_SqlInjectionAttempt_IsParameterized`.

**❗ TRACER.CORE PURITY** — `Tracer.Core.csproj` must have zero third-party package references. Any violation fails the CI check.

**❗ NULLABLE ENABLED** — All projects have `<Nullable>enable</Nullable>`. No `#nullable disable` pragmas. All warnings are errors.

**❗ TEST QUALITY** — Tests must verify correctness, not compilation. Read `.guides\CODE-STANDARDS.md §0` for the full checklist.

**❗ COMPLETE THE BATCH** — Do not stop midway to ask if it is OK to do obvious things like running tests. Run the tests. Fix failures. Do it all until everything passes. Write the report only when everything is done and green.

---

## 📊 Report Requirements

**Submit to:** `.dev/tracer/reports/BATCH-01-REPORT.md`

Include:

1. **Task completion status** — for each of TRC-P1-001 through TRC-P1-006, mark as DONE or PARTIAL with notes
2. **Test results** — output of `dotnet test tests\Tracer.Tests.Unit --configuration Release` (pass count, fail count)
3. **Build verification** — output confirming `dotnet build Tracer.sln --configuration Release` exits 0 with zero warnings
4. **Developer Insights:**
   - **Q1:** What issues did you encounter during implementation? How did you resolve them?
   - **Q2:** Did you spot any weak points or design concerns in the existing design/specs? What would you improve?
   - **Q3:** What design decisions did you make beyond the instructions? What alternatives did you consider?
   - **Q4:** What edge cases did you discover that weren't mentioned in the spec?
   - **Q5:** Are there any performance concerns or optimization opportunities you noticed in the DuckDB schema or query layer?
5. **Suggested commit message**

---

## 🎯 Success Criteria

This batch is DONE when:
- [ ] TRC-P1-001: `Tracer.sln` + 6 projects scaffold complete; `dotnet build --configuration Release` exits 0; CI workflow committed
- [ ] TRC-P1-002: All domain types implemented; `RecordTests.cs` (4 tests) + `TraceIdTests.cs` (7 tests) pass
- [ ] TRC-P1-003: All interfaces and exceptions implemented; solution builds clean
- [ ] TRC-P1-004: Query model types implemented; `QueryBuilderTests.cs` `EventFilter_All_HasNoConstraints` test passes
- [ ] TRC-P1-005: DuckDB schema, writer, batch buffer implemented; `SchemaTests.cs` (4 tests) + `AppenderTests.cs` (6 tests) pass
- [ ] TRC-P1-006: Query builder, reader, mapping implemented; 8 additional `QueryBuilderTests.cs` tests pass including SQL injection test
- [ ] `dotnet test tests\Tracer.Tests.Unit --configuration Release` exits 0 — all tests pass
- [ ] `dotnet build Tracer.sln --configuration Release` exits 0 — zero warnings
- [ ] Report submitted to `.dev/tracer/reports/BATCH-01-REPORT.md`

---

## 📚 Reference Materials

- **Task Definitions:** `docs\TASK-DETAIL.md` — TRC-P1-001 through TRC-P1-006
- **Phase 1 Design:** `docs\tracer_phase1_design.md` — complete technical specification
- **Architecture:** `docs\tracer_architecture_v1.md` — §2, §4, §5, §17, §18
- **Code Standards:** `.guides\CODE-STANDARDS.md` — §0 (Test Quality), §1 (No Magic Numbers)
- **Developer Guide:** `.guides\DEV-GUIDE.md`
