# Tracer — Task Detail Document

**Reference Architecture:** [tracer_architecture_v1.md](./tracer_architecture_v1.md)
**Reference Design:** [tracer_design.md](./tracer_design.md)

**Phase designs:**
- Phase 1: [tracer_phase1_design.md](./tracer_phase1_design.md)
- Phase 2: [tracer_phase2_design.md](./tracer_phase2_design.md)
- Phase 3: [tracer_phase3_design.md](./tracer_phase3_design.md)
- Phase 4: [tracer_phase4_design.md](./tracer_phase4_design.md)
- Phase 5: [tracer_phase5_design.md](./tracer_phase5_design.md)
- Phase 6: [tracer_phase6_design.md](./tracer_phase6_design.md)

> Tasks are appended per phase by the development process. Each task has a unique ID, precise success conditions, and references to phase design sections rather than duplicating content.

---

<!-- PHASE 1 TASKS BEGIN -->

## TRC-P1-001 — Solution & Project Scaffold

**Phase design reference:** [tracer_phase1_design.md §2 — Solution and Project Layout](./tracer_phase1_design.md#2-solution-and-project-layout) (§2.1 Repository Structure, §2.2 Project File Conventions, §2.3 Dependency Graph); [§7.4 CI Configuration](./tracer_phase1_design.md#74-ci-configuration); [§8 Coding Standards](./tracer_phase1_design.md#8-coding-standards-and-conventions)

**Architecture reference:** [tracer_architecture_v1.md §18 — Build Sequence](./tracer_architecture_v1.md#18-build-sequence) (Phase 1 description)

**Description:** Creates the repository skeleton, solution file, and all cross-cutting build tooling that governs every subsequent task. Establishes the six project files (`Tracer.Core`, `Tracer.Storage.DuckDB`, `Tracer.Adapters.Mock`, `Tracer.TestHarness`, `Tracer.Tests.Unit`, `Tracer.Tests.Integration`), the shared `Directory.Build.props`/`Directory.Packages.props` that enforce nullable, warnings-as-errors, and centralized versioning, and the CI skeleton. All other Phase 1 tasks depend on this scaffold being correct.

**Success conditions:**

1. `Tracer.sln` exists at repo root and references all six projects in the paths specified in §2.1.
2. `global.json` pins the .NET SDK to `8.0.x` with `"rollForward": "latestFeature"` as shown in §2.2.
3. `Directory.Build.props` sets `<Nullable>enable</Nullable>`, `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`, `<LangVersion>12</LangVersion>`, `<EnableNETAnalyzers>true</EnableNETAnalyzers>`, `<AnalysisLevel>latest</AnalysisLevel>`, `<ImplicitUsings>enable</ImplicitUsings>`, `<InvariantGlobalization>true</InvariantGlobalization>` for all projects, exactly as in §2.2.
4. `Directory.Packages.props` enables `ManagePackageVersionsCentrally` and `CentralPackageTransitivePinningEnabled`, and pins all package versions listed in §2.2 (`DuckDB.NET.Data`, `Microsoft.Extensions.Logging.Abstractions`, `Microsoft.Extensions.Time.Testing`, `xunit`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`, `FluentAssertions`).
5. `.editorconfig` enables `CA1051` and `CA1062` as errors, disables `CA2007`, and sets `IDE0079` to error as per §8.4.
6. `dotnet build Tracer.sln --configuration Release` succeeds with zero warnings and zero errors on a clean checkout.
7. `dotnet format Tracer.sln --verify-no-changes` exits with code 0.
8. A CI workflow file runs `dotnet restore`, `dotnet build --no-restore --configuration Release`, and `dotnet test --no-build --configuration Release` on `windows-latest` as per §7.4; the workflow file is committed to the repository.
9. A CI build step (script or MSBuild target) fails the pipeline if `Tracer.Core.csproj` contains any third-party `<PackageReference>` entry; this check passes on a clean scaffold where `Tracer.Core` has no package references.
10. All Phase 1 integration tests pass.

**Dependencies:** none

---

## TRC-P1-002 — Tracer.Core: Domain Types

**Phase design reference:** [tracer_phase1_design.md §3.1 — Record Types](./tracer_phase1_design.md#31-record-types); [§3.2 — Identity Types](./tracer_phase1_design.md#32-identity-types); [§3.3 — Time Types](./tracer_phase1_design.md#33-time-types)

**Architecture reference:** [tracer_architecture_v1.md §4 — Terminology](./tracer_architecture_v1.md#4-terminology); [§5 — Data Categories](./tracer_architecture_v1.md#5-data-categories)

**Description:** Implements the core vocabulary of Tracer: the `DiagnosticRecord` abstract record base and its two sealed subtypes (`EventRecord`, `StateSampleRecord`), the strongly-typed identity structs (`TraceId`, `EventId`, `AgentId`), the domain value objects (`TopicName`, `EntityId`, `Severity`, `SessionMarker`), and the `WallclockTime` time type with nanosecond-since-epoch semantics. No infrastructure or third-party package dependencies are permitted in this assembly; every other component speaks in terms of these types.

**Success conditions:**

1. `DiagnosticRecord` is `abstract record` with all `required` properties from §3.1 (`SequenceNumber`, `PublishWallclock`, `ReceiveWallclock`, `PublisherNode`, `SubscriberNode`, `Topic`); `EventRecord` and `StateSampleRecord` are `sealed record` subtypes with their respective additional properties.
2. `TraceId` and `EventId` are `readonly record struct` each with a `None` static property (value 0), an `IsNone` property, and `ToString()` returning a 16-character uppercase hex string as specified in §3.2.
3. `AgentId` constructor rejects null/whitespace (throws `ArgumentException`) and values longer than 64 characters (throws `ArgumentException` with descriptive message) per §3.2.
4. `TopicName` and `EntityId` constructors each reject null/whitespace with `ArgumentException` as specified in §3.2.
5. `WallclockTime.FromDateTimeOffset(DateTimeOffset)` and `ToDateTimeOffset()` round-trip without precision loss at nanosecond level (a `DateTimeOffset` value survives a convert-to-`WallclockTime`-and-back cycle with the same tick count, modulo 100ns truncation inherent in `DateTimeOffset`).
6. `WallclockTime` subtraction via `operator -` yields a `TimeSpan`; addition via `operator +` with a `TimeSpan` yields a `WallclockTime`; both use the nanosecond representation without loss per §3.3.
7. `WallclockTime.CompareTo` is consistent with comparing the underlying `NanosecondsSinceEpoch` `long` values directly.
8. `Tracer.Core.csproj` has zero third-party `<PackageReference>` entries; the CI check from TRC-P1-001 passes on this project.
9. `RecordTests` test class (`Tracer.Tests.Unit/Core/RecordTests.cs`) exists with the following passing test methods:
   - `EventRecord_WithNullParentEventId_IsValid` — constructs an `EventRecord` with `ParentEventId = null`; no exception is thrown.
   - `StateSampleRecord_FastRate_CanBeConstructed` — constructs a `StateSampleRecord` with `Rate = StateSampleRate.Fast`; no exception (domain type imposes no restriction on rate).
   - `EventRecord_EqualityByValue` — two `EventRecord` instances constructed with identical field values are `==` and `.Equals()` returns true.
   - `WallclockTime_RoundTripDateTimeOffset_LosslessWithinTickResolution` — a known `DateTimeOffset` converted to `WallclockTime` and back produces a `DateTimeOffset` equal within 100ns.
   - `WallclockTime_Subtraction_YieldsTimeSpan` — `(t2 - t1)` where `t2 = t1 + 1 second` yields a `TimeSpan` of 1 second.
   - `WallclockTime_Addition_YieldsCorrectTime` — `t + TimeSpan.FromSeconds(5)` advances the nanosecond counter by exactly 5,000,000,000.
10. `TraceIdTests` test class (`Tracer.Tests.Unit/Core/TraceIdTests.cs`) exists with the following passing test methods:
    - `TraceId_None_ValueIsZero` — `TraceId.None.Value == 0` and `TraceId.None.IsNone == true`.
    - `TraceId_FormatsAs16CharUppercaseHex` — `new TraceId(255).ToString() == "00000000000000FF"`.
    - `TraceId_Equality_WorksAcrossConstructionPaths` — `new TraceId(42) == new TraceId(42)`.
    - `AgentId_RejectsNullOrEmpty` — `new AgentId("")` throws `ArgumentException`.
    - `AgentId_RejectsOver64Chars` — `new AgentId(new string('x', 65))` throws `ArgumentException`.
    - `EntityId_RejectsEmpty` — `new EntityId("")` throws `ArgumentException`.
    - `TopicName_RejectsEmpty` — `new TopicName("")` throws `ArgumentException`.
11. All Phase 1 integration tests pass.

**Dependencies:** TRC-P1-001

---

## TRC-P1-003 — Tracer.Core: Abstractions & Error Types

**Phase design reference:** [tracer_phase1_design.md §3.5 — Core Abstractions (Interfaces)](./tracer_phase1_design.md#35-core-abstractions-interfaces); [§11 — Error Handling](./tracer_phase1_design.md#11-error-handling); [§11.1 — Exception Types](./tracer_phase1_design.md#111-exception-types); [§11.2 — Argument Validation](./tracer_phase1_design.md#112-argument-validation)

**Architecture reference:** [tracer_architecture_v1.md §2 — Core Design Principles](./tracer_architecture_v1.md#2-core-design-principles) (decoupled from simulation, mock-first)

**Description:** Defines the three primary interface contracts (`IDiagnosticDataSource`, `IDiagnosticStorageWriter`, `IDiagnosticStorageReader`) and `IClock` that decouple all Tracer components from their implementations. Also establishes the exception hierarchy (`TracerException`, `TracerStorageException`, `TracerScenarioException`) that provides catchable, domain-specific error types. These are the seams at which mock and production implementations plug in — their signatures must not change in later phases without a compatibility decision.

**Success conditions:**

1. `IDiagnosticDataSource` in namespace `Tracer.Core.Abstractions` declares exactly `IAsyncEnumerable<DiagnosticRecord> ReadAsync(CancellationToken ct)` and no other members, as specified in §3.5.
2. `IDiagnosticStorageWriter` in namespace `Tracer.Core.Abstractions` extends `IAsyncDisposable` and declares `AppendEventAsync(EventRecord, CancellationToken)`, `AppendStateAsync(StateSampleRecord, CancellationToken)`, `AppendBatchAsync(IReadOnlyList<DiagnosticRecord>, CancellationToken)`, and `FlushAsync(CancellationToken)` with return types `Task`, matching §3.5 exactly.
3. `IDiagnosticStorageReader` in namespace `Tracer.Core.Abstractions` extends `IAsyncDisposable` and declares `QueryEventsAsync(EventQuery, CancellationToken)`, `GetEventAsync(EventId, CancellationToken)`, and `CountEventsAsync(EventFilter, CancellationToken)` with the return types from §3.5.
4. `IClock` in namespace `Tracer.Core.Time` declares only `WallclockTime Now { get; }`.
5. `TracerException` in namespace `Tracer.Core.Errors` is a non-sealed `Exception` subclass with a `(string message)` constructor and a `(string message, Exception inner)` constructor.
6. `TracerStorageException` and `TracerScenarioException` each extend `TracerException` with the same two-constructor pattern; both are `sealed`.
7. Public methods on any `IDiagnosticStorageWriter` implementation validate arguments at the method boundary: `ArgumentNullException.ThrowIfNull(record)` and `ct.ThrowIfCancellationRequested()` are called before any internal operation, as specified in §11.2.
8. `Tracer.Core.csproj` still has zero third-party `<PackageReference>` entries after this task is complete.
9. No test class is required solely for these interface declarations; their contracts are verified at compile time by `DuckDbStorageWriter` (TRC-P1-005) satisfying `IDiagnosticStorageWriter` and `MockDataSource` (TRC-P1-007) satisfying `IDiagnosticDataSource`. The CI build acts as the verification gate.
10. All Phase 1 integration tests pass.

**Dependencies:** TRC-P1-002

---

## TRC-P1-004 — Tracer.Core: Query Model

**Phase design reference:** [tracer_phase1_design.md §3.4 — Filters and Queries](./tracer_phase1_design.md#34-filters-and-queries)

**Architecture reference:** [tracer_architecture_v1.md §17 — Performance Targets](./tracer_architecture_v1.md#17-performance-targets) (filter application < 300ms; no client-side filtering of bulk data)

**Description:** Implements the query-model types that express retrieval intent to the storage layer: `EventFilter` (what records to include), `EventQuery` (filter plus pagination and ordering), `QueryBucket` (time-bucket width for aggregation), and the `QueryOrder` enum. The fluent factory methods on `EventFilter` make test code concise and readable without duplicating filter construction logic.

**Success conditions:**

1. `EventFilter` is `sealed record` in namespace `Tracer.Core.Queries` with all nullable filter properties from §3.4: `From`, `To` (`WallclockTime?`), `Topic` (`TopicName?`), `PublisherNode`, `SubscriberNode` (`AgentId?`), `TraceId` (`TraceId?`), `EntityId` (`EntityId?`), `OwningPlayerId` (`string?`), `MinSeverity` (`Severity?`), `PayloadSearch` (`string?`).
2. `EventFilter.All` static property returns a new `EventFilter` instance with every property null or default.
3. `EventFilter.ForTrace(TraceId)` static factory returns an `EventFilter` with only `TraceId` set.
4. `EventFilter.ForEntity(EntityId)` static factory returns an `EventFilter` with only `EntityId` set.
5. `EventQuery` is `sealed record` in namespace `Tracer.Core.Queries` with properties `Filter` (`EventFilter`, required), `Limit` (default 1000), `Offset` (default 0), `Order` (`QueryOrder`, default `PublishTimeAscending`).
6. `QueryOrder` enum has exactly three members: `PublishTimeAscending`, `PublishTimeDescending`, `SequenceNumberAscending`.
7. `QueryBucket` is `readonly record struct` in namespace `Tracer.Core.Queries` with static factory properties `FiveMinutes`, `ThirtySeconds`, and `FiveSeconds` returning the appropriate `TimeSpan` widths.
8. Two `EventQuery` instances with equal `Filter`, `Limit`, `Offset`, and `Order` values compare as equal via record structural equality.
9. `QueryBuilderTests` test class (`Tracer.Tests.Unit/Storage/QueryBuilderTests.cs`) includes the following passing test method at minimum (more methods are added in TRC-P1-006):
    - `EventFilter_All_HasNoConstraints` — `EventFilter.All` has every nullable property equal to `null` and every integer/enum property at its default value.
10. All Phase 1 integration tests pass.

**Dependencies:** TRC-P1-002

---

## TRC-P1-005 — Tracer.Storage.DuckDB: Schema & Appenders

**Phase design reference:** [tracer_phase1_design.md §4 — Tracer.Storage.DuckDB: Persistence](./tracer_phase1_design.md#4-tracerstorageduckdb-persistence); [§4.1 — DuckDB Version and Library](./tracer_phase1_design.md#41-duckdb-version-and-library); [§4.2 — Schema (Version 1)](./tracer_phase1_design.md#42-schema-version-1); [§4.3 — DuckDbStorageWriter Implementation](./tracer_phase1_design.md#43-duckdbstoragewriter-implementation); [§4.6 — BatchBuffer](./tracer_phase1_design.md#46-batchbuffer)

**Architecture reference:** [tracer_architecture_v1.md §5 — Data Categories](./tracer_architecture_v1.md#5-data-categories) (events and slow state columns); [§17.1 — How Targets Are Met](./tracer_architecture_v1.md#171-how-targets-are-met) (insertion in time-order, targeted indexes)

**Description:** Creates the `Tracer.Storage.DuckDB` project containing `SchemaV1` DDL constants, `DuckDbStorageWriter` implementing `IDiagnosticStorageWriter` via the DuckDB.NET Appender API, and `BatchBuffer` for grouping records ahead of appender flushes. This task also defines the six index definitions. The writer is the only write path into DuckDB for all of Phase 1; correctness of its append and flush semantics is foundational for all integration tests.

**Success conditions:**

1. `SchemaV1` in namespace `Tracer.Storage.DuckDB.Schema` defines `CreateEventsTable`, `CreateSlowStateTable`, `CreateSchemaMetaTable`, and `CreateIndexes` SQL string constants matching the column names and types from §4.2, including `TIMESTAMP_NS` for all wallclock columns and `JSON` for payload columns.
2. `SchemaV1.Version` is the integer constant `1`.
3. `DuckDbStorageWriter.CreateAsync(string dbPath, ILogger<DuckDbStorageWriter>, CancellationToken)` opens a DuckDB connection at the given path, executes all four DDL statements, and inserts one row into `_schema_meta`; calling it on an already-initialized file is idempotent — exactly one row exists in `_schema_meta` after two calls on the same file.
4. `AppendEventAsync(EventRecord, CancellationToken)` writes all columns of the record to the `events` table appender; nullable domain fields (`ParentEventId`, `EntityId`, `OwningPlayerId`, `ScenarioPhase`, `Severity`, `NotableLabel`) are written as SQL NULL when the corresponding C# property is null.
5. `AppendStateAsync(StateSampleRecord, CancellationToken)` throws `NotSupportedException` (with descriptive message) when `record.Rate == StateSampleRate.Fast`; for slow records it writes all columns to the `slow_state` table appender.
6. `AppendBatchAsync(IReadOnlyList<DiagnosticRecord>, CancellationToken)` routes each element to the correct appender based on runtime type; fast-state `StateSampleRecord` items are silently skipped (no exception); event and slow-state items are written.
7. `FlushAsync(CancellationToken)` causes all previously appended records to be visible to a `DuckDbStorageReader` opened against the same file path immediately after `FlushAsync` returns.
8. `DisposeAsync` closes all appenders and the connection; a second `DisposeAsync` call on the same instance does not throw.
9. `BatchBuffer` in namespace `Tracer.Storage.DuckDB.Ingestion` has `ShouldFlush` returning `true` when internal count reaches `maxRecords` or when `maxAge` has elapsed since the first record was added; `DrainAll()` returns all buffered records and resets the buffer to empty.
10. `SchemaTests` test class (`Tracer.Tests.Unit/Storage/SchemaTests.cs`) has passing methods:
    - `CreateAsync_FreshDatabase_WritesSchemaMetaRow` — creates a writer on a temp path, disposes it, opens the file with a raw DuckDB connection, queries `_schema_meta`, verifies exactly one row with `schema_version = 1`.
    - `CreateAsync_ExistingDatabase_IsIdempotent` — calls `CreateAsync` twice on the same file path; no exception; exactly one row in `_schema_meta`.
    - `SchemaV1_Version_IsOne` — `Assert.Equal(1, SchemaV1.Version)`.
    - `AllIndexes_AreCreated` — after `CreateAsync`, queries the DuckDB catalog (e.g., `PRAGMA show_tables` or `duckdb_indexes()`) and confirms all six index names from §4.2 exist.
11. `AppenderTests` test class (`Tracer.Tests.Unit/Storage/AppenderTests.cs`) has passing methods:
    - `AppendEvent_1000Records_RoundTrip` — writes 1000 `EventRecord` instances, calls `FlushAsync`, opens a reader, queries all events; count is 1000 and fields of a sampled record match the written values.
    - `AppendEvent_NullFields_StoredAsNull` — writes an event with `ParentEventId = null`, `EntityId = null`, `Severity = null`; reads it back; all three columns are null.
    - `AppendState_FastRate_ThrowsNotSupported` — calling `AppendStateAsync` with a fast-rate record throws `NotSupportedException`.
    - `AppendBatch_MixedRecords_RoutesCorrectly` — batch of 5 events + 3 slow-state records; after flush, `events` table has 5 rows, `slow_state` table has 3 rows.
    - `Writer_DisposeAsync_IsIdempotent` — disposes the writer twice; no exception on the second call.
    - `Reader_SeesData_OnlyAfterWriterFlush` — writes events but does not flush; opens a reader (or queries before flush); event count is 0; then flushes, reopens reader; count is non-zero.
12. All Phase 1 integration tests pass.

**Dependencies:** TRC-P1-003, TRC-P1-004

---

## TRC-P1-006 — Tracer.Storage.DuckDB: Query Layer

**Phase design reference:** [tracer_phase1_design.md §4.4 — DuckDbStorageReader Implementation](./tracer_phase1_design.md#44-duckdbstoragereader-implementation); [§4.5 — EventQueryBuilder](./tracer_phase1_design.md#45-eventquerybuilder)

**Architecture reference:** [tracer_architecture_v1.md §17 — Performance Targets](./tracer_architecture_v1.md#17-performance-targets) (filter < 300ms; causal tree < 500ms); [§17.1](./tracer_architecture_v1.md#171-how-targets-are-met) (no client-side filtering; all filtering in DuckDB)

**Description:** Implements `EventQueryBuilder` (converts `EventQuery`/`EventFilter` to parameterized SQL — never by string concatenation of user input), `BucketAggregator` (time-bucketed event counts), and `DuckDbStorageReader` which executes those queries and maps rows back to `EventRecord` domain objects via `Mapping.MapEventRecord`. This task completes the read path: the DuckDB layer is fully bidirectional after this task.

**Success conditions:**

1. `EventQueryBuilder.Build(EventQuery)` returns SQL starting with `SELECT * FROM events WHERE 1=1`; each active filter field on the `EventFilter` appends exactly one additional `AND` clause using a named parameter; `LIMIT $limit OFFSET $offset` is always appended.
2. `EventQueryBuilder.BuildCount(EventFilter)` returns SQL starting with `SELECT COUNT(*) FROM events WHERE 1=1` and the same filter clauses, with no `LIMIT` or `OFFSET`.
3. Time filters use `publish_wallclock >= $from` and `publish_wallclock < $to`; the parameter values are `DateTimeOffset`-typed (not raw integers).
4. `MinSeverity` filter generates an `IN (...)` clause enumerating all `Severity` values `>= MinSeverity`; parameters are named `$sev0`, `$sev1`, etc.; for `MinSeverity = Warning` the clause contains `Warning` and `Error` but not `Info`.
5. `PayloadSearch` filter generates `AND payload::VARCHAR LIKE $search` with the value wrapped in `%…%` and with `%` and `_` in the user-supplied string escaped as `\%` and `\_` respectively.
6. No filter value is ever concatenated directly into the SQL string; every user-supplied value is a named parameter entry in the returned parameter list.
7. `DuckDbStorageReader.OpenAsync(string dbPath, ILogger<DuckDbStorageReader>, CancellationToken)` opens the DuckDB file in read-only mode (`ACCESS_MODE=READ_ONLY`).
8. `DuckDbStorageReader.QueryEventsAsync` executes the built SQL and maps every result row to `EventRecord` via `Mapping.MapEventRecord`, preserving null columns as null C# properties.
9. `DuckDbStorageReader.GetEventAsync(EventId, CancellationToken)` returns the matching `EventRecord` by `event_id`, or `null` if no row is found.
10. `DuckDbStorageReader.CountEventsAsync(EventFilter, CancellationToken)` returns the integer count from `BuildCount`, cast to `long`.
11. `QueryBuilderTests` test class (`Tracer.Tests.Unit/Storage/QueryBuilderTests.cs`) has passing methods (in addition to the one from TRC-P1-004):
    - `Build_NoFilters_ContainsLimitAndOffset` — `EventQuery` with `EventFilter.All`; returned SQL contains `LIMIT $limit` and `OFFSET $offset` as parameters.
    - `Build_TimeRange_AppendsWallclockClauses` — filter with `From` and `To` set; SQL contains `>= $from` and `< $to`.
    - `Build_TraceIdFilter_AppendsSingleAndClause` — filter with `TraceId` set; SQL contains exactly one extra AND clause `trace_id = $tid`.
    - `Build_MinSeverityWarning_ExpandsToInClause` — `MinSeverity = Severity.Warning`; SQL IN clause parameters contain `Warning` and `Error`; parameter named `$sev0` exists; no parameter for `Info`.
    - `Build_PayloadSearch_EscapesLikeSpecialChars` — `PayloadSearch = "a%b_c"`; the `$search` parameter value is `%a\%b\_c%`.
    - `Build_MultipleFilters_CombineWithAnd` — filter with both `TraceId` and `EntityId` set; SQL contains two AND clauses.
    - `BuildCount_AnyFilter_ReturnsSELECTCOUNT` — SQL begins with `SELECT COUNT(*)`.
    - `Build_SqlInjectionAttempt_IsParameterized` — `PayloadSearch = "'; DROP TABLE events; --"`; verifies the string appears as a parameter value and does NOT appear in the SQL string itself.
12. All Phase 1 integration tests pass.

**Dependencies:** TRC-P1-005

---

## TRC-P1-007 — Tracer.Adapters.Mock: MockDataSource & SimulatedClock

**Phase design reference:** [tracer_phase1_design.md §5.1 — Design Principles](./tracer_phase1_design.md#51-design-principles); [§5.2 — SimulatedClock](./tracer_phase1_design.md#52-simulatedclock); [§5.4 — TraceIdGenerator (Deterministic)](./tracer_phase1_design.md#54-traceidgenerator-deterministic); [§5.7 — MockDataSource](./tracer_phase1_design.md#57-mockdatasource)

**Architecture reference:** [tracer_architecture_v1.md §2 — Core Design Principles](./tracer_architecture_v1.md#2-core-design-principles) (mock-first development); [§19 — Test Harness and Mock Adapters](./tracer_architecture_v1.md#19-test-harness-and-mock-adapters)

**Description:** Implements the time-control and ID-generation primitives that make deterministic testing possible. `SimulatedClock` is a thread-safe, manually-advanced `IClock` implementation that never spontaneously changes. `TraceIdGenerator` wraps a seeded `Random` to produce deterministic, non-zero trace IDs and monotonically-increasing event IDs. `MockDataSource` wires these into the `IDiagnosticDataSource` contract by delegating to a named scenario script resolved from `ScenarioRegistry`.

**Success conditions:**

1. `SimulatedClock` in namespace `Tracer.Adapters.Mock` implements `IClock`; `Now` returns the current simulated time as `WallclockTime`; `Advance(TimeSpan delta)` adds exactly `delta.Ticks * 100L` nanoseconds to the internal counter; `Set(WallclockTime time)` replaces the counter value unconditionally.
2. `SimulatedClock` serializes `Now`, `Advance`, and `Set` with an internal `lock`; concurrent calls from two threads on the same instance do not corrupt the counter (no partial updates observable).
3. `SimulatedClock.Now` called twice in succession without any `Advance` or `Set` call returns the same `WallclockTime` value.
4. `TraceIdGenerator` in namespace `Tracer.Adapters.Mock.Generation` accepts a `Random` in its constructor; two instances constructed with separate `new Random(42)` instances produce identical `NewTrace()` and `NewEvent()` sequences.
5. `TraceIdGenerator.NewEvent()` returns `EventId` values starting from 1 and incrementing by 1 per call; never returns `EventId.None`.
6. `TraceIdGenerator.NewTrace()` never returns `TraceId.None` (value 0); the loop in §5.4 retries until a non-zero value is produced.
7. `MockDataSource` in namespace `Tracer.Adapters.Mock` accepts `(string scenarioName, ScenarioConfig config)` in its constructor; constructs a `SimulatedClock` from `config.StartTime`, a `Random` seeded with `config.Seed`, a `TraceIdGenerator`, and a `ScenarioContext` combining all of them; resolves the scenario script via `ScenarioRegistry.Get(scenarioName)`.
8. `MockDataSource.ReadAsync(CancellationToken)` delegates directly to `_script.ExecuteAsync(_context, ct)` and returns the resulting `IAsyncEnumerable<DiagnosticRecord>`.
9. `MockDataSource.Clock` property exposes the internal `SimulatedClock` instance for test-side clock inspection and control.
10. `TimeTests` test class (`Tracer.Tests.Unit/Core/TimeTests.cs`) has passing methods:
    - `SimulatedClock_AdvancesExactly_WhenTold` — advance by 1 second; `Now.NanosecondsSinceEpoch` increases by exactly 1,000,000,000.
    - `SimulatedClock_DoesNotAdvanceSpontaneously` — read `Now` twice without intervening `Advance`; both values are equal.
    - `SimulatedClock_TwoInstancesAtSameInitial_ReturnSameNow` — two clocks initialized to the same `WallclockTime`; `Now` returns equal values.
    - `SimulatedClock_Set_ReplacesCurrentTime` — call `Set` with a specific time; `Now` returns exactly that time.
    - `WallclockTime_CompareTo_ConsistentWithLongCompare` — `a.CompareTo(b)` has the same sign as `a.NanosecondsSinceEpoch.CompareTo(b.NanosecondsSinceEpoch)`.
11. All Phase 1 integration tests pass.

**Dependencies:** TRC-P1-003

---

## TRC-P1-008 — Tracer.Adapters.Mock: Scenario System

**Phase design reference:** [tracer_phase1_design.md §5.1 — Design Principles](./tracer_phase1_design.md#51-design-principles); [§5.3 — Scenario Script Abstraction](./tracer_phase1_design.md#53-scenario-script-abstraction); [§5.5 — First Scenarios](./tracer_phase1_design.md#55-first-scenarios); [§5.6 — ScenarioRegistry](./tracer_phase1_design.md#56-scenarioregistry)

**Architecture reference:** [tracer_architecture_v1.md §19.1 — Scenario Generators](./tracer_architecture_v1.md#191-scenario-generators) (deterministic, scenario-shaped data); [§19 — Test Harness and Mock Adapters](./tracer_architecture_v1.md#19-test-harness-and-mock-adapters)

**Description:** Implements the full scenario framework — the `IScenarioScript`/`ScenarioContext`/`ScenarioConfig` abstractions, `ScenarioRegistry`, and both Phase 1 scenarios. `CalmScenario` produces a steady baseline heartbeat load with a session-start event, exercising basic record flow. `CombatEngagementScenario` produces a three-phase combat sequence with causal chains (`shot_fired` → `projectile_spawn` → `projectile_impact` → `damage_applied`) exercising trace ID propagation and parent-event linkage. Both scenarios are deterministic given their seed.

**Success conditions:**

1. `IScenarioScript` in namespace `Tracer.Adapters.Mock.Scenarios` declares `string Name { get; }` and `IAsyncEnumerable<DiagnosticRecord> ExecuteAsync(ScenarioContext context, CancellationToken ct)` with the `[EnumeratorCancellation]` attribute on `ct`, exactly as specified in §5.3.
2. `ScenarioConfig` has default property values from §5.3: `Duration = TimeSpan.FromMinutes(5)`, `NodeCount = 3`, `EntityCount = 10`, `EventsPerSecond = 100`, `Seed = 42`.
3. `ScenarioRegistry.Get("Calm")` and `ScenarioRegistry.Get("CombatEngagement")` each return a fresh `IScenarioScript` instance with the correct `Name` property; `Get` with any other name throws `ArgumentException`.
4. `ScenarioRegistry.AvailableScenarios` returns a collection containing at minimum `"Calm"` and `"CombatEngagement"`.
5. `CalmScenario.ExecuteAsync` yields a session-start event (topic `system.session_start`, non-null `NotableLabel = "Calm session started"`) as the very first record before any heartbeat events.
6. `CalmScenario` terminates after the simulated clock reaches `StartTime + Duration`; the last emitted record has `PublishWallclock < StartTime + Duration + one event interval`.
7. `CalmScenario` with `EventsPerSecond = 100` and `Duration = TimeSpan.FromSeconds(60)` yields between 5,950 and 6,050 records total (±1% tolerance, accounting for the session-start event).
8. `CombatEngagementScenario.ExecuteAsync` produces events spanning three scenario phases: `"approach"`, `"engagement"`, and `"withdrawal"`; every emitted `EventRecord` has a non-null `ScenarioPhase` property set to one of these three strings.
9. `CombatEngagementScenario` produces at least one causal chain where a root event with topic `shot_fired` has descendant events (`projectile_spawn`, `projectile_impact`, `damage_applied`) all sharing the same `TraceId` and each having a `ParentEventId` that refers to the preceding event in the chain.
10. Both scenarios are deterministic: two invocations of `IScenarioScript.ExecuteAsync` with `ScenarioContext` values built from the same `(scenarioName, seed, config)` produce record sequences that are equal element-by-element across all fields.
11. `ScenarioTests` test class (`Tracer.Tests.Unit/Mock/ScenarioTests.cs`) has passing methods:
    - `CalmScenario_FirstRecord_IsSessionStart` — first element from `ExecuteAsync` has `Topic.Value == "system.session_start"`.
    - `CalmScenario_Duration_TerminatesWithinConfiguredTime` — 60s config; last record's `PublishWallclock` is less than `StartTime + 61s`.
    - `CalmScenario_EventCount_WithinTolerance` — seed 42, 60s duration, 100 eps; total record count is between 5,950 and 6,050.
    - `CombatEngagement_CausalTrees_AreValid` — for every record with a non-null `ParentEventId`, an `EventRecord` with that `EventId` appears earlier in the same scenario's output.
    - `CombatEngagement_AllEvents_HaveNonNullScenarioPhase` — every `EventRecord` yielded by `CombatEngagementScenario` has a non-null `ScenarioPhase`.
    - `ScenarioRegistry_Get_UnknownName_ThrowsArgumentException` — `ScenarioRegistry.Get("NonExistent")` throws `ArgumentException`.
12. All Phase 1 integration tests pass.

**Dependencies:** TRC-P1-007

---

## TRC-P1-009 — Tracer.TestHarness

**Phase design reference:** [tracer_phase1_design.md §6 — Tracer.TestHarness](./tracer_phase1_design.md#6-tracertestharness); [§6.1 — TracerStackFixture](./tracer_phase1_design.md#61-tracerstackfixture); [§6.2 — Fluent Assertions Extensions](./tracer_phase1_design.md#62-fluent-assertions-extensions)

**Architecture reference:** [tracer_architecture_v1.md §19 — Test Harness and Mock Adapters](./tracer_architecture_v1.md#19-test-harness-and-mock-adapters); [§19.2 — Test Fixture](./tracer_architecture_v1.md#192-test-fixture)

**Description:** Implements `TracerStackFixture`, the integration-test scaffolding that wires `MockDataSource` → `DuckDbStorageWriter` → `DuckDbStorageReader` into a single disposable unit backed by a temporary directory. Also provides `InMemoryStackOptions` for fixture configuration, the `EventAssertions` and `StorageAssertions` fluent extension methods, and `TestLogSink`. All current and future integration tests build on this harness rather than constructing the stack manually.

**Success conditions:**

1. `TracerStackFixture.CreateAsync(string scenarioName, int seed, TimeSpan? duration, InMemoryStackOptions? options, CancellationToken)` creates a temp directory under `Path.GetTempPath()`, constructs a `DuckDbStorageWriter` targeting a `.duckdb` file inside it, constructs a `MockDataSource` with a matching `ScenarioConfig`, and returns a fully initialized fixture without yet running the scenario.
2. `TracerStackFixture.RunScenarioAsync(CancellationToken)` iterates `DataSource.ReadAsync`, dispatches each record to the writer via `AppendEventAsync` or `AppendStateAsync`, calls `FlushAsync` on the writer, then opens a `DuckDbStorageReader` against the same file; the `Reader` property is non-null and query-ready on return.
3. `TracerStackFixture.DisposeAsync` closes the reader (if open), closes the writer, and deletes the temp directory; a second `DisposeAsync` call on the same instance does not throw.
4. `InMemoryStackOptions` in namespace `Tracer.TestHarness` has default values `NodeCount = 3`, `EntityCount = 10`, `EventsPerSecond = 100`; all properties are `init`-settable.
5. `EventAssertions.ShouldFormValidTrace(this IEnumerable<EventRecord>)` throws `FluentAssertions.Execution.AssertionException` when any record has a `ParentEventId` that does not match any `EventId` present in the enumeration; passes without exception when all parent links resolve.
6. `EventAssertions.ShouldBeTimeOrdered(this IEnumerable<EventRecord>)` throws `AssertionException` when any element has a `PublishWallclock` earlier than its predecessor's; passes when events are non-decreasing in time.
7. `StorageAssertions` in namespace `Tracer.TestHarness.Assertions` provides at minimum `ShouldContainEventCount(this DuckDbStorageReader reader, long expected, CancellationToken ct)` which calls `CountEventsAsync(EventFilter.All, ct)` and asserts equality with FluentAssertions.
8. `TestLogSink` in namespace `Tracer.TestHarness.Diagnostics` captures log messages written to it in a `List<string>` accessible via `GetMessages()`; it does not throw on any log call regardless of level.
9. `Tracer.TestHarness.csproj` references `Tracer.Core`, `Tracer.Storage.DuckDB`, `Tracer.Adapters.Mock`, `Microsoft.Extensions.Logging.Abstractions`, and `FluentAssertions`; it does NOT reference `xunit` (xunit is exclusively a test-project dependency).
10. All Phase 1 integration tests pass.

**Dependencies:** TRC-P1-006, TRC-P1-008

---

## TRC-P1-010 — Unit Tests: Core & Storage

**Phase design reference:** [tracer_phase1_design.md §7.1 — Unit Tests (Tracer.Tests.Unit)](./tracer_phase1_design.md#71-unit-tests-tracertestsunit) (Core and Storage subsections); [§7.3 — Test Performance](./tracer_phase1_design.md#73-test-performance)

**Architecture reference:** [tracer_architecture_v1.md §19.3 — Test Categories](./tracer_architecture_v1.md#193-test-categories) (unit tests: per-component, mocked dependencies)

**Description:** Populates `Tracer.Tests.Unit` with the `Core/` and `Storage/` test suites. These tests are pure (no external dependencies, no DuckDB file I/O) except for `SchemaTests` and `AppenderTests` which open temporary DuckDB files. Every test verifies a single behavior and must complete in well under 1 second individually. The test project has no reference to `Tracer.TestHarness`.

**Success conditions:**

1. `RecordTests` class (`Tracer.Tests.Unit/Core/RecordTests.cs`) has all test methods listed in TRC-P1-002 success condition 9; all pass.
2. `TraceIdTests` class (`Tracer.Tests.Unit/Core/TraceIdTests.cs`) has all test methods listed in TRC-P1-002 success condition 10; all pass.
3. `TimeTests` class (`Tracer.Tests.Unit/Core/TimeTests.cs`) has all test methods listed in TRC-P1-007 success condition 10; all pass.
4. `SchemaTests` class (`Tracer.Tests.Unit/Storage/SchemaTests.cs`) has all test methods listed in TRC-P1-005 success condition 10; all pass.
5. `AppenderTests` class (`Tracer.Tests.Unit/Storage/AppenderTests.cs`) has all test methods listed in TRC-P1-005 success condition 11; all pass.
6. `QueryBuilderTests` class (`Tracer.Tests.Unit/Storage/QueryBuilderTests.cs`) has all test methods listed in TRC-P1-004 success condition 9 and TRC-P1-006 success condition 11; all pass, including the SQL injection parameterization test.
7. `Tracer.Tests.Unit.csproj` references `Tracer.Core`, `Tracer.Storage.DuckDB`, `Tracer.Adapters.Mock`, `xunit`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`, and `FluentAssertions`; it does NOT reference `Tracer.TestHarness`.
8. `dotnet test Tracer.Tests.Unit --configuration Release` exits with code 0; total elapsed time is under 10 seconds on the CI machine.
9. `dotnet format Tracer.Tests.Unit --verify-no-changes` exits with code 0.
10. All Phase 1 integration tests pass.

**Dependencies:** TRC-P1-006, TRC-P1-007

---

## TRC-P1-011 — Unit Tests: Mock Adapter

**Phase design reference:** [tracer_phase1_design.md §7.1 — Unit Tests (Tracer.Tests.Unit)](./tracer_phase1_design.md#71-unit-tests-tracertestsunit) (Mock subsections: DeterminismTests and ScenarioTests); [§5.1 — Design Principles](./tracer_phase1_design.md#51-design-principles)

**Architecture reference:** [tracer_architecture_v1.md §19.1 — Scenario Generators](./tracer_architecture_v1.md#191-scenario-generators) (deterministic given a seed; scenario-shaped data)

**Description:** Implements `DeterminismTests` and `ScenarioTests` in `Tracer.Tests.Unit/Mock/`. These tests verify the mock adapter's two core guarantees: determinism (same seed → identical output across independent instantiations) and structural correctness (scenarios produce records with valid causal linkages and match their declared behavioral shapes). No DuckDB file I/O is performed; tests operate only against in-memory scenario execution.

**Success conditions:**

1. `DeterminismTests` class (`Tracer.Tests.Unit/Mock/DeterminismTests.cs`) has passing methods:
   - `MockDataSource_SameSeedSameScenario_ProducesIdenticalSequence` — creates two `MockDataSource` instances with `("Calm", seed = 42, Duration = 30s)`, collects both async sequences into `List<DiagnosticRecord>`, asserts the lists are equal element-by-element across all fields (`EventId`, `TraceId`, `SequenceNumber`, `PublishWallclock`, `PayloadJson`).
   - `MockDataSource_DifferentSeeds_ProduceDifferentSequences` — seeds 1 and 2 with the same scenario; the first records from each source have different `TraceId` values.
   - `TraceIdGenerator_SameSeed_ProducesSameTraceIds` — two `TraceIdGenerator` instances each wrapping `new Random(42)`; five consecutive `NewTrace()` calls produce identical `TraceId` sequences across the two generators.
   - `SimulatedClock_AdvancesMatchAcrossRuns` — two `SimulatedClock` instances initialized to the same time, subjected to the same sequence of `Advance` calls; `Now` is equal at each step.
2. `ScenarioTests` class (`Tracer.Tests.Unit/Mock/ScenarioTests.cs`) has all test methods listed in TRC-P1-008 success condition 11; all pass.
3. The `DeterminismTests` and `ScenarioTests` test methods together complete within the 10-second unit test suite budget (contributing no more than 5 seconds to the total).
4. `dotnet test --filter "FullyQualifiedName~Mock" Tracer.Tests.Unit` exits with code 0.
5. All Phase 1 integration tests pass.

**Dependencies:** TRC-P1-008

---

## TRC-P1-012 — Integration Tests: End-to-End

**Phase design reference:** [tracer_phase1_design.md §7.2 — Integration Tests (Tracer.Tests.Integration)](./tracer_phase1_design.md#72-integration-tests-tracertestsintegration); [§7.3 — Test Performance](./tracer_phase1_design.md#73-test-performance); [§1.3 — Success Criteria](./tracer_phase1_design.md#13-success-criteria)

**Architecture reference:** [tracer_architecture_v1.md §19.3 — Test Categories](./tracer_architecture_v1.md#193-test-categories) (integration tests: full stack with mock adapters; each test < 1 second); [§1.3 — Success Criteria items 1–4](./tracer_architecture_v1.md#1-purpose-and-scope)

**Description:** Implements `EndToEndTests` and `ScenarioRoundTripTests` in `Tracer.Tests.Integration`, exercising the complete pipeline — `MockDataSource` → `DuckDbStorageWriter` → `DuckDbStorageReader` → domain query results — via `TracerStackFixture`. These are the primary correctness validation for Phase 1: they verify ingestion accuracy, query filter correctness, causal tree validity, and end-to-end determinism (same seed → byte-identical query results across two independent runs).

**Success conditions:**

1. `EndToEndTests` class (`Tracer.Tests.Integration/EndToEndTests.cs`) has passing methods:
   - `CalmScenario_1Minute_QueryReturnsExpectedEventCount` — fixture with Calm scenario, 60s duration; `CountEventsAsync(EventFilter.All, ct)` returns a value matching the count obtained by iterating all records from `ReadAsync` before writing.
   - `CombatEngagement_QueryByTraceId_ReturnsValidCausalTree` — fixture with CombatEngagement; extracts one `TraceId` from the written data; calls `QueryEventsAsync` with `EventFilter.ForTrace(traceId)`; result passes `ShouldFormValidTrace()`.
   - `QueryByEntity_ReturnsOnlyMatchingEntity` — filter with a specific `EntityId`; every returned `EventRecord` has `EntityId == theFilter.EntityId`.
   - `QueryWithTimeRange_ReturnsOnlyEventsInRange` — filter with `From` and `To` set; every returned event has `PublishWallclock >= From` and `PublishWallclock < To`.
   - `QueryWithLimit_RespectsLimit` — `EventQuery` with `Limit = 10`; result list has exactly 10 elements (scenario must produce ≥10 events, which all Phase 1 scenarios do).
   - `GetEventAsync_KnownEventId_ReturnsMatchingEvent` — reads any event from a prior query; calls `GetEventAsync(event.EventId)`; returned event has the same `EventId` and `TraceId`.
   - `CountEventsAsync_MatchesFullQueryCount` — `CountEventsAsync(EventFilter.All)` equals the count of records returned by `QueryEventsAsync` with `EventFilter.All` and a sufficiently large `Limit`.
2. `ScenarioRoundTripTests` class (`Tracer.Tests.Integration/ScenarioRoundTripTests.cs`) has passing methods:
   - `CalmScenario_WriteClosedReopened_QueryResultsIdentical` — writes Calm scenario, disposes writer, reopens a new reader, queries all events; the count and field values of a sampled set of records match those from the pre-close query.
   - `CalmScenario_TwoRunsSameSeed_ProduceBytewiseSameEventData` — runs Calm scenario (seed 42, 60s) twice, each into a separate temp DuckDB file; queries both with `EventFilter.All`; the resulting `IReadOnlyList<EventRecord>` are equal element-by-element across all fields.
   - `CombatEngagement_AllParentEventIds_ReferenceExistingEvents` — full CombatEngagement round-trip; for every `EventRecord` in the query results with a non-null `ParentEventId`, an `EventRecord` with that `EventId` also exists in the result set; verified using `ShouldFormValidTrace` on the full result set grouped by `TraceId`.
3. `Tracer.Tests.Integration.csproj` references `Tracer.TestHarness`, `xunit`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`, and `FluentAssertions`; it does NOT directly reference `Tracer.Storage.DuckDB` or `Tracer.Adapters.Mock` (those are accessed through the harness abstraction).
4. `dotnet test Tracer.Tests.Integration --configuration Release` exits with code 0; total elapsed time is under 30 seconds on the CI machine.
5. `dotnet format Tracer.Tests.Integration --verify-no-changes` exits with code 0.
6. All Phase 1 integration tests pass.

**Dependencies:** TRC-P1-009, TRC-P1-010, TRC-P1-011

<!-- PHASE 1 TASKS END -->

<!-- PHASE 2 TASKS BEGIN -->

## TRC-P2-001 — New Core Abstractions in `Tracer.Core`

**Design:** [tracer_phase2_design.md §3 — New Core Abstractions](./tracer_phase2_design.md#3-new-core-abstractions) (§3.1 IAgentTransport, §3.2 ITelemetryUploadService, §3.3 IntervalTimestamp and IntervalManifest, §3.4 Phase 2 Additions to IDiagnosticStorageWriter)

**Architecture:** [tracer_architecture_v1.md §5 — Data Categories](./tracer_architecture_v1.md#5-data-categories) (fast state, slow state, events); [§18 — Build Sequence](./tracer_architecture_v1.md#18-build-sequence) (Phase 2 seam abstractions)

Extends `Tracer.Core` with the two new interface seams (`IAgentTransport`, `ITelemetryUploadService`) and all supporting domain types required by Phase 2: `IntervalTimestamp`, `CaptureGap`, `IntervalManifest`, `SessionMarker`, and their associated enums. Also adds `AppendFastStateAsync` to `IDiagnosticStorageWriter` so the Phase 2 storage contract is settled before any implementation work begins. `Tracer.Core.csproj` must still carry zero third-party package references after this task.

**Success conditions:**

1. `IAgentTransport` in `Tracer.Core.Abstractions` extends `IAsyncDisposable` and declares exactly `IAsyncEnumerable<DiagnosticRecord> ReadAsync(CancellationToken ct)` and `TransportHealth GetHealth()`; `TransportHealth` is a `sealed record` with the five properties from §3.1 (`PendingCount`, `Capacity`, `TotalReceived`, `TotalDropped`, `LastReceivedAt`), all `required`.
2. `ITelemetryUploadService` in `Tracer.Core.Abstractions` declares `Task<UploadIntentId> RequestUploadAsync(UploadRequest, CancellationToken)` and `Task<UploadStatus> GetStatusAsync(UploadIntentId, CancellationToken)`; `UploadRequest` and `FileToUpload` are `sealed record` types with `required` properties matching §3.2; `UploadIntentId` is a `readonly record struct`; `UploadStatus` enum contains `Unknown`, `Pending`, `InProgress`, `Complete`, `Failed`.
3. `IntervalTimestamp` in `Tracer.Core.Domain` is a `readonly record struct` whose constructor rejects strings that do not match the `YYYYMMDDTHHMMSSZ` format (exactly 16 characters, parseable as UTC); `FromUtc(DateTimeOffset)` throws `ArgumentException` when `Offset != TimeSpan.Zero`; `ToDateTimeOffset()` round-trips with `FromUtc` without loss for any UTC `DateTimeOffset` with zero fractional seconds; `TryParse(string, out IntervalTimestamp)` returns `false` for malformed input without throwing.
4. `CaptureGap` is `sealed record` in `Tracer.Core.Domain` with `required` properties `StartUtc`, `EndUtc` (`WallclockTime`), `Reason` (`CaptureGapReason`), `DroppedRecordCount` (`long`), and optional `Detail` (`string?`); `CaptureGapReason` enum has exactly the five members from §3.3: `BackpressureFastStateDropped`, `BackpressureSlowStateDropped`, `BackpressureEventsDropped`, `UnrecoveredCrashGap`, `TransportDisconnected`.
5. `IntervalManifest` is `sealed record` in `Tracer.Core.Domain` with all `required` properties from §3.3; `ManifestFinalizationReason` enum has exactly `ScheduledRotation`, `GracefulShutdown`, `RecoveryAfterCrash`; `SessionMarker` is `sealed record` with `SessionId`, `Type` (`SessionMarkerType`), `Wallclock`, and nullable `Label`; `SessionMarkerType` has `Start` and `End`.
6. `IDiagnosticStorageWriter` in `Tracer.Core.Abstractions` gains `Task AppendFastStateAsync(StateSampleRecord, CancellationToken)` as the fifth method per §3.4; the Phase 1 `DuckDbStorageWriter` satisfies the updated interface by throwing `NotSupportedException` for this method until replaced in TRC-P2-002; the solution still builds with zero warnings.
7. `Tracer.Core.csproj` has zero third-party `<PackageReference>` entries; the CI purity check from TRC-P1-001 passes.
8. `IntervalTimestampTests` test class (`Tracer.Tests.Unit/Core/IntervalTimestampTests.cs`) exists with passing test methods:
   - `IntervalTimestamp_ValidFormat_RoundTripsToDateTimeOffset` — constructs from `"20260519T140000Z"`, calls `ToDateTimeOffset()`, verifies year/month/day/hour/minute/second fields match.
   - `IntervalTimestamp_MalformedString_ThrowsArgumentException` — `new IntervalTimestamp("bad")` throws `ArgumentException`.
   - `IntervalTimestamp_NonUtcDateTimeOffset_ThrowsArgumentException` — `IntervalTimestamp.FromUtc(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.FromHours(1)))` throws `ArgumentException`.
   - `IntervalTimestamp_TryParse_ReturnsFalseForInvalidInput` — `TryParse("not-a-ts", out _)` returns `false` and does not throw.
   - `IntervalTimestamp_TryParse_ReturnsTrueForValidInput` — `TryParse("20260101T120000Z", out var ts)` returns `true` and `ts.Value == "20260101T120000Z"`.
   - `CaptureGap_CanBeConstructedWithAllReasons` — constructs one `CaptureGap` per `CaptureGapReason` enum value; no exception thrown for any value.
9. All Phase 1 and Phase 2 integration tests pass.

**Dependencies:** TRC-P1-003

---

## TRC-P2-002 — Fast-State Parquet Writers

**Design:** [tracer_phase2_design.md §4 — The Storage Side: Parquet for Fast State](./tracer_phase2_design.md#4-the-storage-side-parquet-for-fast-state) (§4.1 Why Parquet, §4.2 Topic-Specific Schemas, §4.3 FastStateParquetWriter, §4.4 Updated DuckDbStorageWriter)

**Architecture:** [tracer_architecture_v1.md §5 — Data Categories](./tracer_architecture_v1.md#5-data-categories) (§5.3 fast state as Parquet per topic per interval)

Implements the Parquet write path for fast-state samples inside `Tracer.Storage.DuckDB`. Adds `ParquetTopicSchema`, `ParquetColumn`, `ParquetType`, `ParquetSchemas` (schema builder), `ColumnExtractor` (JSON-path extractor), `FastStateParquetWriter`, `WellKnownTopicSchemas` (with the `Transforms` schema), and a `NullFastStateWriter` for unregistered topics. Updates `DuckDbStorageWriter` to implement `AppendFastStateAsync` by lazily creating per-topic writers in a `fast_state/` subdirectory. Adds `Parquet.Net 4.24.0` to `Directory.Packages.props`.

**Success conditions:**

1. `ParquetTopicSchema` in `Tracer.Storage.DuckDB.Parquet` is `sealed record` with `TopicName` and `Columns` (`IReadOnlyList<ParquetColumn>`); `ParquetColumn` has `Name`, `Type` (`ParquetType`), `Nullable` (default `false`), `JsonPath`; `ParquetType` enum covers exactly `Int32`, `Int64`, `UInt64`, `Float`, `Double`, `Bool`, `String`, `TimestampNs`; `WellKnownTopicSchemas.Transforms` is a `ParquetTopicSchema` for `"topic.transforms"` with the seven quaternion/position columns from §4.2.
2. `FastStateParquetWriter.CreateAsync(string outputPath, ParquetTopicSchema, ILogger<FastStateParquetWriter>, CancellationToken)` creates `outputPath` and its parent directory; the file exists on disk before the method returns.
3. `FastStateParquetWriter.AppendAsync(StateSampleRecord, CancellationToken)` buffers records internally; when accumulated count reaches `RowGroupFlushThreshold` (10 000), it automatically flushes one complete Parquet row group to the file without an explicit flush call from the caller.
4. `FastStateParquetWriter.DisposeAsync` flushes any remaining buffered rows (even if below threshold), finalizes the Parquet file footer, and closes the underlying stream; after disposal the file is a valid Parquet file readable by DuckDB via `read_parquet(path)`; a second `DisposeAsync` call does not throw.
5. `FastStateParquetWriter.TotalRowsWritten` returns the cumulative count of records accepted via `AppendAsync` regardless of whether they have been flushed; the property is thread-safe.
6. `ColumnExtractor.ExtractRow(StateSampleRecord, ParquetTopicSchema)` produces a row containing all five standard columns (`publish_wallclock`, `receive_wallclock`, `publisher_node`, `instance_key`, `sequence_number`) plus one value per `ParquetTopicSchema.Columns` entry extracted from `record.PayloadJson` using each column's `JsonPath`; a missing JSON path yields the column type's zero value for non-nullable columns or `null` for nullable ones.
7. `DuckDbStorageWriter.AppendFastStateAsync(StateSampleRecord, CancellationToken)` throws `ArgumentException` when `record.Rate != StateSampleRate.Fast`; for fast-state records with a registered schema it routes to the matching `FastStateParquetWriter` (creating it lazily on first use for that topic) in a `fast_state/` subdirectory of the interval directory; records for unregistered topics are silently dropped and a warning is logged exactly once per unknown topic per writer lifetime.
8. `DuckDbStorageWriter.DisposeAsync` awaits `DisposeAsync` on every `FastStateParquetWriter` it created before closing the DuckDB connection; the Parquet files are finalized and present on disk after disposal.
9. `FastStateParquetWriterTests` test class (`Tracer.Tests.Unit/Storage/FastStateParquetWriterTests.cs`) exists with passing test methods:
   - `FastStateParquetWriter_CreateAsync_FileExistsOnDisk` — creates writer on a temp path; file exists before any `AppendAsync`.
   - `FastStateParquetWriter_Append100_DisposeAsync_TotalRowsIs100` — appends 100 records; disposes; `TotalRowsWritten == 100`.
   - `FastStateParquetWriter_DisposeAsync_IsIdempotent` — disposes twice; no exception on second call.
   - `ColumnExtractor_KnownJsonPath_ExtractsCorrectValue` — constructs a `StateSampleRecord` with payload `{"position":{"x":1.5}}`; uses `WellKnownTopicSchemas.Transforms`; asserts the extracted `pos_x` column value is `1.5f`.
   - `DuckDbStorageWriter_AppendFastStateAsync_NonFastRate_ThrowsArgumentException` — calling `AppendFastStateAsync` with `StateSampleRate.Slow` throws `ArgumentException`.
10. All Phase 1 and Phase 2 integration tests pass.

**Dependencies:** TRC-P2-001, TRC-P1-005

---

## TRC-P2-003 — Agent Configuration & DI

**Design:** [tracer_phase2_design.md §5 — The TracerAgent: Architecture](./tracer_phase2_design.md#5-the-traceragent-architecture) (§5.1 Process Lifecycle, §5.2 Program.cs, §5.3 AgentHostBuilder, §5.4 AgentConfig, §5.5 Example agent.json); [§8 — Mock Adapters for Phase 2](./tracer_phase2_design.md#8-mock-adapters-for-phase-2) (§8.1 InProcessChannelTransport, §8.2 LocalFileSystemUploadService)

**Architecture:** [tracer_architecture_v1.md §18 — Build Sequence](./tracer_architecture_v1.md#18-build-sequence) (Phase 2: TracerAgent as Windows service)

Creates the `Tracer.Agent` assembly with its `Program.cs` entrypoint, `AgentHostBuilder`, configuration hierarchy, and validation. Also adds the two Phase 2 mock adapter implementations (`InProcessChannelTransport`, `LocalFileSystemUploadService`) to `Tracer.Adapters.Mock`. The agent can be launched as a Windows service or a console app; the `LOG_FILE=` line is the first output to stdout. Adds all new NuGet packages from §2.1 to `Directory.Packages.props`.

**Success conditions:**

1. `AgentConfig` in `Tracer.Agent.Configuration` has the properties and defaults from §5.4: `NodeId`, `DataRoot`, `LogsRoot` marked `[Required]`; `IntervalDuration` defaulting to `TimeSpan.FromHours(1)`; `KeepLastNIntervals = 24`; `DiskWatermarkPercent = 10`; `LogToConsole = false`; nested `TransportConfig`, `UploadServiceConfig`, and `BackpressureConfig` each with the defaults from §5.4.
2. `ConfigValidation.Validate(AgentConfig)` throws `InvalidOperationException` (with a descriptive message) when `NodeId` is null or whitespace; when `DataRoot` or `LogsRoot` is not a fully-qualified absolute path (`Path.IsPathFullyQualified` returns `false`); when `IntervalDuration < TimeSpan.FromMinutes(1)` or `> TimeSpan.FromHours(24)`; or when `TimeSpan.FromDays(1).Ticks % IntervalDuration.Ticks != 0`.
3. `AgentHostBuilder.Build(string[])` returns an `IHost` whose DI container successfully resolves `AgentConfig`, `IClock`, `IAgentTransport`, `ITelemetryUploadService`, `IntervalScheduler`, `IntervalRotator`, `StartupRecoveryService`, `IngestionPipeline`, `BackpressureMonitor`, `DropPolicy`, `ManifestWriter`, and `AgentHostedService` without exception when given a valid config file via `--config <path>`.
4. Config path resolution uses the value of `--config <path>` when provided (absolute path only — a relative path causes `ArgumentException`); falls back to `Path.Combine(Environment.GetFolderPath(SpecialFolder.CommonApplicationData), "Tracer", "agent", "config.json")` when the argument is absent.
5. `InProcessChannelTransport` in `Tracer.Adapters.Mock.Transport` implements `IAgentTransport`; its internal channel is bounded with `FullMode = BoundedChannelFullMode.DropOldest`; `WriteAsync` enqueues records and increments `TotalReceived`; `ReadAsync` yields from the channel via `ReadAllAsync`; `GetHealth().PendingCount` reflects the current unread item count.
6. `InProcessChannelTransport.Complete()` marks the channel writer as complete; subsequent `ReadAsync` drains remaining items then completes the async sequence; `DisposeAsync` calls `Complete()` before returning.
7. `LocalFileSystemUploadService` in `Tracer.Adapters.Mock.Upload` implements `ITelemetryUploadService`; `RequestUploadAsync` generates a new `UploadIntentId`, copies each file listed in `UploadRequest.Files` to `Path.Combine(_fakeNasRoot, request.NodeId.Value, request.Interval.Value, Path.GetFileName(file.Path))`, records the intent as `Complete`, and returns the ID; `GetStatusAsync` returns the recorded status for known IDs and `UploadStatus.Unknown` for unknown ones.
8. `AgentConfigTests` test class (`Tracer.Tests.Unit/Agent/AgentConfigTests.cs`) exists with passing test methods:
   - `ConfigValidation_MissingNodeId_Throws` — `Validate` called with `NodeId = ""` throws `InvalidOperationException`.
   - `ConfigValidation_RelativeDataRoot_Throws` — `DataRoot = "relative/path"` throws `InvalidOperationException`.
   - `ConfigValidation_IntervalTooShort_Throws` — `IntervalDuration = TimeSpan.FromSeconds(30)` throws `InvalidOperationException`.
   - `ConfigValidation_NonDivisibleInterval_Throws` — `IntervalDuration = TimeSpan.FromMinutes(11)` throws `InvalidOperationException`.
   - `ConfigValidation_ValidConfig_DoesNotThrow` — fully-populated config with absolute paths and `IntervalDuration = TimeSpan.FromHours(1)` passes validation without exception.
   - `InProcessChannelTransport_WriteAndRead_RoundTrips` — writes one `EventRecord`; `ReadAsync` yields it; `GetHealth().TotalReceived == 1`.
9. All Phase 1 and Phase 2 integration tests pass.

**Dependencies:** TRC-P2-001, TRC-P1-003

---

## TRC-P2-004 — Ingestion Pipeline

**Design:** [tracer_phase2_design.md §6.4 — The IngestionPipeline](./tracer_phase2_design.md#64-the-ingestionpipeline); [§6.5 — BackpressureMonitor and DropPolicy](./tracer_phase2_design.md#65-backpressuremonitor-and-droppolicy)

Implements the inner loop of the agent: `IngestionPipeline` reads from `IAgentTransport`, consults `BackpressureMonitor` for the current saturation level, applies `DropPolicy` to decide per-record fate, and routes accepted records via `RecordRouter` to the writer methods on `DuckDbStorageWriter`. Drop decisions are converted to `CaptureGap` notifications on `IntervalRotator`. Write exceptions are caught per-record and converted to gaps so the pipeline never crashes.

**Success conditions:**

1. `BackpressureMonitor.CurrentLevel()` reads `IAgentTransport.GetHealth().PendingCount` and returns `BackpressureLevel.Healthy` when below `InflightThresholdRecords`; `FastStateAtRisk` at or above `InflightThresholdRecords`; `SlowStateAtRisk` at or above `FastStateDropThresholdRecords`; `EventsAtRisk` at or above `SlowStateDropThresholdRecords`; `Saturated` at or above `EventsDropThresholdRecords`; thresholds come from `AgentConfig.Backpressure`.
2. `DropPolicy.ShouldDrop(DiagnosticRecord, BackpressureLevel, out CaptureGapReason)` returns `false` for all record types at `BackpressureLevel.Healthy`; returns `true` with `reason = BackpressureFastStateDropped` for fast-state `StateSampleRecord` at `FastStateAtRisk` and above; returns `true` with `reason = BackpressureSlowStateDropped` for slow-state `StateSampleRecord` at `SlowStateAtRisk` and above; returns `true` with `reason = BackpressureEventsDropped` for `EventRecord` at `Saturated` only; does not drop `EventRecord` at any level below `Saturated`.
3. `RecordRouter` in `Tracer.Agent.Ingestion` dispatches `EventRecord` to `writer.AppendEventAsync`; slow-rate `StateSampleRecord` to `writer.AppendStateAsync`; fast-rate `StateSampleRecord` to `writer.AppendFastStateAsync`; calls `IntervalRotator.NotifyRecordWritten(record)` after each successful write.
4. `IngestionPipeline.RunAsync(CancellationToken)` processes records from `_transport.ReadAsync` sequentially; when `DropPolicy.ShouldDrop` returns `true`, calls `_rotator.NotifyCaptureGap` with a gap covering the dropped record's `PublishWallclock` and the appropriate reason; does not call any writer method for that record.
5. Any exception thrown by a writer method inside `RunAsync` is caught, logged at `Error` level, and recorded as a `CaptureGap`; the pipeline continues processing the next record without re-throwing; `OperationCanceledException` caused by the cancellation token causes `RunAsync` to return cleanly.
6. When `IntervalRotator.CurrentWriter` is `null` during `ProcessOneAsync`, the record is dropped with `CaptureGapReason.TransportDisconnected` and no writer call is attempted.
7. `DropPolicyTests` test class (`Tracer.Tests.Unit/Agent/DropPolicyTests.cs`) exists with passing test methods:
   - `DropPolicy_Healthy_DoesNotDropAnything` — `EventRecord`, fast-state, and slow-state records each return `false` at `BackpressureLevel.Healthy`.
   - `DropPolicy_FastStateAtRisk_DropsFastStateOnly` — fast-state returns `true`; slow-state and `EventRecord` return `false`.
   - `DropPolicy_SlowStateAtRisk_DropsSlowAndFast` — fast-state and slow-state return `true`; `EventRecord` returns `false`.
   - `DropPolicy_Saturated_DropsAll` — all three record types return `true`.
   - `DropPolicy_FastStateAtRisk_ReasonIsFastStateDropped` — out-param `reason` is `CaptureGapReason.BackpressureFastStateDropped` when fast-state is dropped.
8. `RecordRouterTests` test class (`Tracer.Tests.Unit/Agent/RecordRouterTests.cs`) exists with passing test methods:
   - `RecordRouter_EventRecord_CallsAppendEventAsync` — routes an `EventRecord` to a mock writer; `AppendEventAsync` is called exactly once; `AppendStateAsync` and `AppendFastStateAsync` are not called.
   - `RecordRouter_SlowStateSample_CallsAppendStateAsync` — slow-rate `StateSampleRecord` routes to `AppendStateAsync`; no other append method is called.
   - `RecordRouter_FastStateSample_CallsAppendFastStateAsync` — fast-rate `StateSampleRecord` routes to `AppendFastStateAsync`; no other append method is called.
9. All Phase 1 and Phase 2 integration tests pass.

**Dependencies:** TRC-P2-003, TRC-P2-002

---

## TRC-P2-005 — Interval Rotation Lifecycle

**Design:** [tracer_phase2_design.md §6 — Interval Lifecycle: The Heart of Phase 2](./tracer_phase2_design.md#6-interval-lifecycle-the-heart-of-phase-2) (§6.1 Interval Boundaries, §6.2 The IntervalDirectory, §6.3 IntervalRotator, §6.6 AgentHostedService — The Loop)

**Architecture:** [tracer_architecture_v1.md §18 — Build Sequence](./tracer_architecture_v1.md#18-build-sequence) (Phase 2: interval rotation, manifest generation)

Implements the full rotation protocol: `IntervalScheduler` computes wall-clock-aligned boundaries from `IClock.Now`; `IntervalDirectory` owns one interval's folder layout and sentinel files; `IntervalRotator` opens, flushes, finalizes, and replaces the current writer under a `SemaphoreSlim` lock; `ManifestWriter` serializes `IntervalManifest` to UTF-8 JSON; `UploadIntentDispatcher` hands completed intervals to `ITelemetryUploadService`; `AgentHostedService` orchestrates recovery, ingestion, rotation, and retention loops as a `BackgroundService`.

**Success conditions:**

1. `IntervalScheduler.CurrentIntervalStart()` returns the largest UTC interval boundary (aligned to `IntervalDuration`) that is ≤ `IClock.Now.ToDateTimeOffset()`; for `IntervalDuration = 1 hour` and clock at 14:37 UTC the result is `20260519T140000Z` (or the appropriate date); for a clock exactly on a boundary the result equals that boundary.
2. `IntervalScheduler.NextIntervalBoundary()` equals `CurrentIntervalStart().ToDateTimeOffset() + IntervalDuration` expressed as `WallclockTime`; `TimeUntilNextBoundary()` is always ≥ `TimeSpan.Zero` when called strictly before the boundary.
3. `IntervalScheduler` constructor throws `ArgumentOutOfRangeException` when `IntervalDuration < TimeSpan.FromMinutes(1)` or `> TimeSpan.FromHours(24)`; throws `ArgumentException` when `TimeSpan.FromDays(1).Ticks % IntervalDuration.Ticks != 0`.
4. `IntervalDirectory.RootPath` equals `Path.Combine(dataRoot, "intervals", timestamp.Value)`; derived paths (`EventsDbPath`, `SlowStateDbPath`, `FastStateDirectory`, `ManifestPath`, `ReadySentinelPath`) match §6.2; `IsReady` returns `true` iff the `_ready` file exists; `HasManifest` returns `true` iff `manifest.json` exists; `WriteReadySentinel()` creates a zero-byte `_ready` file causing `IsReady == true`.
5. `IntervalRotator.OpenCurrentAsync(CancellationToken)` creates the interval directory (including `fast_state/` subdirectory) and instantiates `DuckDbStorageWriter`; calling it a second time on the same instance throws `InvalidOperationException`.
6. `IntervalRotator.RotateAsync(ManifestFinalizationReason, CancellationToken)` (a) calls `FlushAsync` and `DisposeAsync` on the current writer, (b) calls `ManifestWriter.WriteAsync` with a manifest built from `SnapshotCurrentStats`, (c) calls `IntervalDirectory.WriteReadySentinel`, (d) calls `UploadIntentDispatcher.DispatchAsync`, (e) opens the next interval via `OpenInternalAsync`; after return `CurrentDirectory` is the new interval, `IsReady == true` on the closed interval.
7. `IntervalRotator.NotifyRecordWritten(DiagnosticRecord)` increments `_eventCountInCurrent` for `EventRecord`, `_slowStateCountInCurrent` for slow-rate `StateSampleRecord`, and adds to `_fastStateTopicsInCurrent` for fast-rate `StateSampleRecord`; session-start events (topic `"system.session_start"`) are parsed for a `sessionId` payload field and appended to `_sessionMarkersInCurrent`.
8. `ManifestWriter.WriteAsync(string path, IntervalManifest, CancellationToken)` serializes the manifest as indented UTF-8 JSON to `path`; `IntervalTimestamp` values appear as their `Value` string (not as a JSON object); the file is a valid `IntervalManifest` when deserialized with `System.Text.Json`.
9. `AgentHostedService.ExecuteAsync` calls `StartupRecoveryService.RecoverAsync`, then `IntervalRotator.OpenCurrentAsync`, then starts ingestion and retention loops; when the cancellation token fires it awaits background tasks and calls `IntervalRotator.RotateAsync(GracefulShutdown, CancellationToken.None)` before returning.
10. `IntervalSchedulerTests` test class (`Tracer.Tests.Unit/Agent/IntervalSchedulerTests.cs`) exists with passing test methods:
    - `IntervalScheduler_AtHourBoundary_CurrentStartEqualsNow` — clock set to exactly 14:00:00 UTC; `CurrentIntervalStart().Value` ends with `T140000Z`.
    - `IntervalScheduler_BetweenBoundaries_CurrentStartIsPriorHour` — clock at 14:37 UTC with 1-hour duration; `CurrentIntervalStart()` is at 14:00 UTC.
    - `IntervalScheduler_NextBoundary_IsOneHourAfterCurrentStart` — `NextIntervalBoundary()` equals `CurrentIntervalStart().ToDateTimeOffset() + 1 hour`.
    - `IntervalScheduler_30MinDuration_ConstructsWithoutError` — 30-minute `IntervalDuration` does not throw.
    - `IntervalScheduler_NonDivisibleDuration_Throws` — 11-minute duration throws `ArgumentException`.
11. `ManifestWriterTests` test class (`Tracer.Tests.Unit/Agent/ManifestWriterTests.cs`) exists with passing test methods:
    - `ManifestWriter_WriteAndDeserialize_RoundTrips` — writes a manifest with known scalar values; reads and deserializes; all scalar fields match.
    - `ManifestWriter_IntervalTimestamp_SerializesAsString` — the written JSON file contains `"intervalStart":"20260519T140000Z"` (a string, not an object).
    - `ManifestWriter_CaptureGaps_IncludedInJson` — writes a manifest with one `CaptureGap`; the deserialized manifest has exactly one gap entry with the matching `Reason`.
12. All Phase 1 and Phase 2 integration tests pass.

**Dependencies:** TRC-P2-003, TRC-P2-002

---

## TRC-P2-006 — Startup Recovery

**Design:** [tracer_phase2_design.md §7 — Startup Recovery](./tracer_phase2_design.md#7-startup-recovery) (§7.1 StartupRecoveryService, §7.2 Determining "Last Checkpoint" Data Visibility)

`StartupRecoveryService` runs once at agent startup, before the first interval is opened. It scans `intervals/` for orphaned folders (present but lacking a `_ready` sentinel), counts recoverable records from partially-written DuckDB files (best effort via read-only open), generates a recovery manifest with `FinalizationReason = RecoveryAfterCrash` and a conservative `CaptureGap` spanning the full interval, writes the `_ready` sentinel, and dispatches an upload intent. Each orphan is processed independently; a failure on one does not prevent recovery of the others.

**Success conditions:**

1. `StartupRecoveryService.RecoverAsync(CancellationToken)` enumerates `Path.Combine(DataRoot, "intervals")`; for each subfolder whose name parses via `IntervalTimestamp.TryParse` and whose `IntervalDirectory.IsReady == false`, the folder is treated as an orphan; folders that are already ready or whose name does not parse are skipped without logging.
2. When `Path.Combine(DataRoot, "intervals")` does not exist, `RecoverAsync` creates the directory and returns without error; no manifest or sentinel is written.
3. For each orphan, recovery attempts to open `events.duckdb` via `DuckDbStorageReader.OpenAsync` in read-only mode and calls `CountEventsAsync(EventFilter.All, ct)`; if the file is absent or the open/count throws for any reason, `eventCount` defaults to `0` and recovery continues; `slow_state.duckdb` is handled identically for `slowStateCount`.
4. The `IntervalManifest` written for a recovered orphan has `FinalizationReason = ManifestFinalizationReason.RecoveryAfterCrash`, exactly one `CaptureGap` with `Reason = CaptureGapReason.UnrecoveredCrashGap` spanning the full interval (`StartUtc` = interval start, `EndUtc` = interval start + `IntervalDuration`), `DroppedRecordCount = 0`, and `SessionMarkers` as an empty list.
5. After recovery, `IntervalDirectory.IsReady == true` and `IntervalDirectory.HasManifest == true` for each successfully processed orphan; `UploadIntentDispatcher.DispatchAsync` is called exactly once per processed orphan.
6. An exception during finalization of one orphan (caught at the per-orphan level) is logged at `Warning` and does not abort recovery of remaining orphans; `RecoverAsync` does not re-throw per-orphan exceptions.
7. Multiple orphans are processed in ascending `IntervalTimestamp.Value` string order (chronological, because the format is lexicographically sortable).
8. `StartupRecoveryTests` test class (`Tracer.Tests.Unit/Agent/StartupRecoveryTests.cs`) exists with passing test methods:
   - `StartupRecovery_NoIntervalsDirectory_CreatesDirectoryAndReturns` — `DataRoot` where `intervals/` does not exist; after `RecoverAsync`, the directory exists; no exception; no manifest written.
   - `StartupRecovery_NoOrphans_LogsAndReturns` — all interval folders under `intervals/` have `_ready` files; `RecoverAsync` completes; no new manifests are written.
   - `StartupRecovery_OneOrphan_WritesManifestAndSentinel` — one interval folder without `_ready`; after `RecoverAsync`, `IsReady == true` and `HasManifest == true`.
   - `StartupRecovery_OneOrphan_ManifestHasRecoveryReason` — the written manifest's `FinalizationReason == RecoveryAfterCrash` and `CaptureGaps` has exactly one entry with `Reason == UnrecoveredCrashGap`.
   - `StartupRecovery_MultipleOrphans_AllFinalized` — three orphaned intervals; after `RecoverAsync`, all three have `IsReady == true`; they were processed in ascending timestamp order (verified by the order of logged interval names).
   - `StartupRecovery_CorruptEventsDb_CountsAsZeroAndContinues` — orphan with a missing or corrupt `events.duckdb`; `RecoverAsync` does not throw; written manifest has `EventCount == 0`.
9. All Phase 1 and Phase 2 integration tests pass.

**Dependencies:** TRC-P2-005, TRC-P2-001

## TRC-P2-007 — Upload & Retention

**Design:** [tracer_phase2_design.md §6 — Interval Lifecycle: The Heart of Phase 2](./tracer_phase2_design.md#6-interval-lifecycle-the-heart-of-phase-2) (§6.3 IntervalRotator upload dispatch, §6.6 AgentHostedService shutdown and retention loop)

Implements the three remaining agent lifecycle components: `UploadIntentDispatcher` hands a completed `IntervalDirectory` and its manifest to `ITelemetryUploadService.RequestUploadAsync` (fire-and-forget); `RetentionManager` enforces `KeepLastNIntervals` and `DiskWatermarkPercent` by evicting the oldest completed (ready) intervals; `ShutdownCoordinator` orchestrates orderly drain on SIGTERM or service-stop by cancelling the hosted service token and awaiting the rotation's final `GracefulShutdown` finalization. Together these three components close the loop from interval capture through upload hand-off to disk management.

**Success conditions:**

1. `UploadIntentDispatcher.DispatchAsync(IntervalDirectory, IntervalManifest, CancellationToken)` builds an `UploadRequest` from the interval directory's `EnumerateFiles()` and the manifest's `NodeId` and `IntervalTimestamp`; calls `ITelemetryUploadService.RequestUploadAsync` exactly once; logs the returned `UploadIntentId` at `Information` level; does not await upload completion (fire-and-forget semantics verified by test: mock upload service call is recorded even if it blocks, but `DispatchAsync` returns before mock confirms completion).
2. `RetentionManager.ApplyAsync(CancellationToken)` enumerates interval folders under `DataRoot/intervals/` that have `_ready` sentinels; keeps the most recent `KeepLastNIntervals` by `IntervalTimestamp.Value` (lexicographic = chronological) order; deletes all others via `Directory.Delete(path, recursive: true)`; logs each deletion at `Information` level with the interval timestamp.
3. `RetentionManager.ApplyAsync` does not delete any interval folder lacking `_ready` (orphans are left for recovery); does not delete the currently-open interval (determined by comparing folder name to the open interval's timestamp).
4. `RetentionManager.ApplyAsync` enforces the disk watermark: if available disk space falls below `DiskWatermarkPercent`% of total volume capacity (checked via `DriveInfo`), oldest ready intervals beyond the minimum of one are deleted until the watermark is cleared or no more evictable intervals remain; at least one completed interval is always preserved regardless of disk pressure.
5. `ShutdownCoordinator` cancels the `IHostApplicationLifetime.ApplicationStopping` token on demand (for tests) and ensures `AgentHostedService` completes its `ExecuteAsync` — verified by `TracerAgentFixture.StopAsync()` which calls the coordinator and awaits host stop within 5 seconds.
6. `RetentionManagerTests` test class (`Tracer.Tests.Unit/Agent/RetentionManagerTests.cs`) exists with passing test methods:
   - `RetentionManager_KeepLast3_WithFiveIntervals_DeletesOldestTwo` — creates five ready interval folders; `ApplyAsync`; two oldest folders deleted, three newest remain.
   - `RetentionManager_OrphanNotDeleted` — one ready interval and one orphan (no `_ready`); `ApplyAsync` with `KeepLastNIntervals = 1`; only the ready interval is kept; orphan remains.
   - `RetentionManager_NothingToEvict_NoException` — fewer intervals than `KeepLastNIntervals`; `ApplyAsync` completes without error.
7. `UploadIntentDispatcherTests` test class (`Tracer.Tests.Unit/Agent/UploadIntentDispatcherTests.cs`) exists with passing test methods:
   - `UploadIntentDispatcher_Dispatch_CallsUploadServiceOnce` — dispatches one interval; mock `ITelemetryUploadService` received exactly one `RequestUploadAsync` call.
   - `UploadIntentDispatcher_Dispatch_IncludesAllIntervalFiles` — dispatched `UploadRequest.Files` contains entries for `events.duckdb`, `slow_state.duckdb`, `manifest.json`, and `_ready`.
8. All Phase 1 and Phase 2 integration tests pass.

**Dependencies:** TRC-P2-005, TRC-P2-003

---

## TRC-P2-008 — Mock Transport & Upload

**Design:** [tracer_phase2_design.md §8 — Mock Adapters for Phase 2](./tracer_phase2_design.md#8-mock-adapters-for-phase-2) (§8.1 InProcessChannelTransport, §8.2 LocalFileSystemUploadService)

Completes the two Phase 2 mock adapter implementations with full behavior and unit test coverage. `InProcessChannelTransport` wraps a bounded `System.Threading.Channels` channel; the producer side is accessible to test code and to `FakeNodeOrchestrator`, and drops the oldest record when the channel is full. `LocalFileSystemUploadService` creates a ZIP archive per interval at `{fakeNasRoot}/{nodeId}/{intervalTimestamp}.zip` containing all files from `UploadRequest.Files`; it is fully synchronous and deterministic for test use. Both implementations were wired into DI by TRC-P2-003; this task adds the ZIP archive behavior, the `Complete()` shutdown API, drop-tracking, and the full unit test suites.

**Success conditions:**

1. `InProcessChannelTransport.WriteAsync(DiagnosticRecord, CancellationToken)` enqueues a record to the bounded channel; when the channel is full (capacity reached), the oldest unread record is dropped (`BoundedChannelFullMode.DropOldest`) and `TotalDropped` is incremented; `GetHealth().TotalDropped` reflects accumulated drops since construction.
2. `InProcessChannelTransport.Complete()` calls `_channel.Writer.TryComplete()`; subsequent `ReadAsync` iterates remaining buffered items then the async sequence completes without blocking; calling `Complete()` twice does not throw.
3. `LocalFileSystemUploadService.RequestUploadAsync(UploadRequest, CancellationToken)` creates `{fakeNasRoot}/{nodeId.Value}/` if absent; writes a ZIP archive to `{fakeNasRoot}/{nodeId.Value}/{interval.Value}.zip`; each file in `UploadRequest.Files` appears as a ZIP entry named by its base filename (fast-state Parquet entries are placed under a `fast_state/` prefix inside the archive); Parquet entries use `CompressionLevel.NoCompression`, all other entries use `CompressionLevel.Optimal`; the archive is readable by `System.IO.Compression.ZipFile.OpenRead` after `RequestUploadAsync` returns.
4. `LocalFileSystemUploadService.GetStatusAsync` returns `UploadStatus.Complete` for a successfully uploaded intent, `UploadStatus.Failed` for a failed one (e.g., I/O error during archive creation), and `UploadStatus.Unknown` for an unrecognised ID.
5. If the ZIP target already exists, the old file is deleted before the new archive is written (idempotent re-upload).
6. `InProcessChannelTransportTests` test class (`Tracer.Tests.Unit/Mock/InProcessChannelTransportTests.cs`) exists with passing test methods:
   - `InProcessChannelTransport_CapacityOne_SecondWriteDropsOldest` — capacity 1; writes two records; first is dropped; `ReadAsync` yields only the second.
   - `InProcessChannelTransport_Complete_ReadAsyncCompletes` — writes one record, calls `Complete()`; `ReadAllAsync` yields the record then terminates.
   - `InProcessChannelTransport_GetHealth_ReflectsDrops` — capacity 1, two writes; `GetHealth().TotalDropped == 1`.
7. `LocalFileSystemUploadServiceTests` test class (`Tracer.Tests.Unit/Mock/LocalFileSystemUploadServiceTests.cs`) exists with passing test methods:
   - `LocalFileSystemUploadService_Upload_CreatesZipAtExpectedPath` — uploads one interval; zip file exists at `{fakeNasRoot}/{nodeId}/{intervalTimestamp}.zip`.
   - `LocalFileSystemUploadService_Upload_ZipContainsAllFiles` — zip contains entries matching all filenames in `UploadRequest.Files`.
   - `LocalFileSystemUploadService_Upload_Idempotent` — uploading the same interval twice; second call overwrites; only one zip file exists.
   - `LocalFileSystemUploadService_GetStatus_UnknownId_ReturnsUnknown` — new service instance; `GetStatusAsync` for a random `UploadIntentId` returns `UploadStatus.Unknown`.
8. All Phase 1 and Phase 2 integration tests pass.

**Dependencies:** TRC-P2-003

---

## TRC-P2-009 — FakeNode

**Design:** [tracer_phase2_design.md §9 — FakeNode Application](./tracer_phase2_design.md#9-fakenode-application) (§9.1 Purpose, §9.2 Program.cs, §9.3 FakeNodeOrchestrator, §9.4 FakeNodeConfig, §9.5 Example fakenode.json)

Creates the `Tracer.FakeNode` assembly and `tracer-fakenode.exe` — a single-process development tool that composes mock data source, in-process transport, agent, and local upload service without any external dependencies. `FakeNodeOrchestrator` drives scenario records from `MockDataSource` into `InProcessChannelTransport`; when the scenario completes it calls `transport.Complete()` to signal the agent's ingestion pipeline to drain. The executable prints `LOG_FILE=` as its first stdout line, mirrors the agent's `Microsoft.Extensions.Hosting` pattern, and supports both console and Windows-service hosting.

**Success conditions:**

1. `Tracer.FakeNode.csproj` exists, targets `net8.0`, produces output executable `tracer-fakenode.exe`; references `Tracer.Core`, `Tracer.Agent`, `Tracer.Adapters.Mock`, `Microsoft.Extensions.Hosting`, and `Microsoft.Extensions.Hosting.WindowsServices`; does NOT reference xunit.
2. `FakeNodeConfig` in `Tracer.FakeNode.Configuration` is a `sealed record` with required properties `ScenarioName` (`string`), `ScenarioConfig` (`ScenarioConfig`), and `AgentConfig` (`AgentConfig`); `FakeNodeConfigLoader.Load(string[])` reads a JSON file from `--config <path>` (absolute path only — relative paths throw `ArgumentException`) and deserialises under the `"FakeNode"` key.
3. `FakeNodeOrchestrator` in `Tracer.FakeNode` inherits `BackgroundService`; its `ExecuteAsync` iterates `MockDataSource.ReadAsync` and calls `_transport.WriteAsync` for each record; when the async sequence completes (scenario done) it calls `_transport.Complete()` and logs scenario completion at `Information` level; `OperationCanceledException` from the stopping token is caught and handled without re-throw.
4. `Program.cs` writes `LOG_FILE=<path>` to stdout before any Serilog output; registers the full agent service set (same `AddAgentServices` helper pattern as `AgentHostBuilder`) plus `FakeNodeOrchestrator` as a second `IHostedService`; exits with code `0` on clean scenario completion and host shutdown; exits with code `1` on an unhandled exception.
5. `dotnet build Tracer.FakeNode --configuration Release` succeeds with zero warnings.
6. Running `tracer-fakenode.exe --config <valid-fakenode.json>` (exercised as an acceptance smoke-test via `dotnet run`) completes the configured scenario, writes at least one interval directory to `AgentConfig.DataRoot`, and exits with code `0`; the interval directory contains `events.duckdb`, `slow_state.duckdb`, `fast_state/`, `manifest.json`, and `_ready`.
7. The final interval produced in condition 6 has `FinalizationReason == GracefulShutdown` in its `manifest.json`, confirming that `transport.Complete()` → ingestion drain → agent shutdown flow is used rather than a forced host abort.
8. All Phase 1 and Phase 2 integration tests pass.

**Dependencies:** TRC-P2-006, TRC-P2-008, TRC-P1-008

---

## TRC-P2-010 — TestHarness Phase 2 Additions

**Design:** [tracer_phase2_design.md §10 — Test Plan for Phase 2](./tracer_phase2_design.md#10-test-plan-for-phase-2) (§10.4 Test Fixtures: `TracerAgentFixture`, `FakeNodeFixture`)

Extends `Tracer.TestHarness` with the three reusable fixtures needed by Phase 2 integration tests. `TracerAgentFixture` spins up an in-process agent host with mock transport, mock upload, a temp `DataRoot`, and an optional `SimulatedClock`; it exposes `PushAsync`, `ForceRotationAsync`, `AdvanceToNextBoundaryAsync`, and `StopAsync`. `FakeNodeFixture` runs a complete `FakeNode` host in-process for a named scenario and exposes the resulting interval paths and parsed manifests for assertion. `TestableIntervalScheduler` wraps `IntervalScheduler` and allows tests to override the next boundary time on demand, enabling rotation without real-time delays.

**Success conditions:**

1. `TracerAgentFixture` in `Tracer.TestHarness` is `sealed` and implements `IAsyncDisposable`; `TracerAgentFixture.CreateAsync(AgentFixtureOptions?, CancellationToken)` returns a running fixture with `IHost` started, `InProcessChannelTransport` wired as `IAgentTransport`, and a temp `DataRoot` created under `Path.GetTempPath()`.
2. `AgentFixtureOptions` in `Tracer.TestHarness` exposes `UseSimulatedClock = false` as default; when set to `true`, the fixture resolves `IClock` as `SimulatedClock` and exposes it via `fixture.SimulatedClock`; `AdvanceToNextBoundaryAsync` advances the simulated clock past the next interval boundary and waits until the rotation loop triggers a rotation.
3. `TracerAgentFixture.PushAsync(DiagnosticRecord, CancellationToken)` calls `Transport.WriteAsync` and returns; the record is processable by the agent on the next pipeline iteration.
4. `TracerAgentFixture.ForceRotationAsync(CancellationToken)` calls `Rotator.RotateAsync(ManifestFinalizationReason.ScheduledRotation, ct)` directly and awaits it; after the call the previous interval directory has `_ready` and `manifest.json` on disk.
5. `TracerAgentFixture.DisposeAsync` stops the host gracefully (triggering the final `GracefulShutdown` rotation), awaits host stop, and deletes the temp `DataRoot`; a second `DisposeAsync` call does not throw.
6. `FakeNodeFixture` in `Tracer.TestHarness` is `sealed` and implements `IAsyncDisposable`; `FakeNodeFixture.RunScenarioAsync(string scenarioName, ScenarioConfig, AgentConfig, CancellationToken)` builds and runs a full `FakeNode` host in-process until scenario completion; `FakeNodeFixture.Manifests` is a non-empty list of `IntervalManifest` objects deserialized from the `manifest.json` files in `AgentConfig.DataRoot/intervals/`; `FakeNodeFixture.IntervalZipPaths` lists the zip paths produced under `AgentConfig.UploadService.LocalFileSystemRoot`.
7. `TestableIntervalScheduler` in `Tracer.TestHarness.ClockControl` wraps `IntervalScheduler`; its `OverrideNextBoundary(WallclockTime)` method causes `NextIntervalBoundary()` to return the overridden value until consumed by a rotation or explicitly reset; after reset it delegates to the real `IntervalScheduler` calculation.
8. `Tracer.TestHarness.csproj` now also references `Tracer.Agent` and `Tracer.FakeNode`; `dotnet build Tracer.TestHarness --configuration Release` succeeds with zero warnings.
9. All Phase 1 and Phase 2 integration tests pass.

**Dependencies:** TRC-P2-009, TRC-P2-008

---

## TRC-P2-011 — Agent Unit Tests

**Design:** [tracer_phase2_design.md §10.1 — Unit Tests](./tracer_phase2_design.md#101-unit-tests)

Populates `Tracer.Tests.Unit/Agent/` and extends `Tracer.Tests.Unit/Storage/` with focused single-behavior tests for every Phase 2 agent component. Tests use isolated units with fakes or stubs rather than full hosted services; `SimulatedClock` replaces real time; temporary directories replace real `DataRoot` where file I/O is unavoidable. Each test class covers exactly one component; no test may depend on another test's side effects; the full unit suite must complete in under 15 seconds on the CI machine.

**Success conditions:**

1. `IntervalSchedulerTests` class (`Tracer.Tests.Unit/Agent/IntervalSchedulerTests.cs`) exists with passing test methods:
   - `CurrentIntervalStart_ReturnsAlignedBoundary` — `SimulatedClock` set to 14:22:00Z, 1-hour duration; `CurrentIntervalStart()` returns `"20260101T140000Z"`.
   - `NextIntervalBoundary_IsCurrentPlusDuration` — result equals aligned start plus duration.
   - `TimeUntilNextBoundary_DecreasesAsClockAdvances` — two consecutive calls with clock advanced between them; second result is smaller.
   - `IntervalDuration_11Minutes_ConstructorThrows` — `ArgumentException` thrown on construction.
   - `IntervalDuration_24Hours_DoesNotThrow` — no exception.
   - `IntervalDuration_LessThan1Minute_Throws` — `ArgumentOutOfRangeException` thrown.
2. `IntervalRotatorTests` class (`Tracer.Tests.Unit/Agent/IntervalRotatorTests.cs`) exists with passing test methods:
   - `OpenCurrentAsync_CreatesIntervalDirectory` — after call, `CurrentDirectory.Exists == true`.
   - `OpenCurrentAsync_CalledTwice_Throws` — `InvalidOperationException` on second call.
   - `RotateAsync_WritesManifestAndSentinel` — rotate on a pre-opened interval; manifest and `_ready` exist on disk afterward.
   - `RotateAsync_DispatchesUpload` — mock `UploadIntentDispatcher` receives exactly one `DispatchAsync` call per rotation.
   - `NotifyRecordWritten_EventRecord_IncrementsEventCount` — manifest written after rotation contains correct `EventCount`.
   - `NotifyCaptureGap_AccumulatesInManifest` — one `NotifyCaptureGap` call; manifest after rotation has exactly one gap entry.
   - `DisposeAsync_TriggersGracefulShutdownRotation` — disposing the rotator with an open interval writes a manifest with `FinalizationReason == GracefulShutdown`.
3. `RecordRouterTests` class (`Tracer.Tests.Unit/Agent/RecordRouterTests.cs`) exists with passing test methods:
   - `RecordRouter_EventRecord_CallsAppendEventAsync` — routes an `EventRecord`; mock writer's `AppendEventAsync` called once.
   - `RecordRouter_SlowState_CallsAppendStateAsync` — routes a slow-rate `StateSampleRecord`; `AppendStateAsync` called once.
   - `RecordRouter_FastState_CallsAppendFastStateAsync` — routes a fast-rate `StateSampleRecord`; `AppendFastStateAsync` called once.
   - `RecordRouter_AfterWrite_NotifiesRotator` — after routing any record, `IntervalRotator.NotifyRecordWritten` called with that record.
4. `DropPolicyTests` class (`Tracer.Tests.Unit/Agent/DropPolicyTests.cs`) exists with passing test methods:
   - `DropPolicy_Healthy_NothingDropped` — all record types return `false` at `BackpressureLevel.Healthy`.
   - `DropPolicy_FastStateAtRisk_DropsFastState` — fast-state `StateSampleRecord` returns `true` with `reason == BackpressureFastStateDropped`.
   - `DropPolicy_FastStateAtRisk_AcceptsEvents` — `EventRecord` returns `false` at `FastStateAtRisk`.
   - `DropPolicy_SlowStateAtRisk_DropsSlowState` — slow-state `StateSampleRecord` returns `true` with `reason == BackpressureSlowStateDropped`.
   - `DropPolicy_Saturated_DropsEvents` — `EventRecord` returns `true` with `reason == BackpressureEventsDropped`.
5. `ManifestWriterTests` class (`Tracer.Tests.Unit/Agent/ManifestWriterTests.cs`) exists with passing test methods:
   - `ManifestWriter_WriteAndRead_RoundTrips` — writes a fully-populated `IntervalManifest` then reads it back; all fields are equal.
   - `ManifestWriter_WallclockTimes_SerializeAsIso8601` — raw JSON contains a `finalized_at` field that is a valid ISO 8601 string with sub-second precision.
   - `ManifestWriter_EmptyGapsAndMarkers_SerializesEmptyArrays` — manifest with empty `CaptureGaps` and `SessionMarkers`; JSON contains `[]` for both arrays.
6. `StartupRecoveryTests` class (`Tracer.Tests.Unit/Agent/StartupRecoveryTests.cs`) exists with all passing test methods listed in TRC-P2-006 success condition 8.
7. `FastStateParquetWriterTests` class (`Tracer.Tests.Unit/Storage/FastStateParquetWriterTests.cs`) exists with all passing test methods listed in TRC-P2-002 success condition 9.
8. `RetentionManagerTests` class (`Tracer.Tests.Unit/Agent/RetentionManagerTests.cs`) exists with all passing test methods listed in TRC-P2-007 success condition 6.
9. `dotnet test Tracer.Tests.Unit --configuration Release` exits with code 0; total elapsed time is under 15 seconds on the CI machine.
10. All Phase 1 and Phase 2 integration tests pass.

**Dependencies:** TRC-P2-007, TRC-P2-008, TRC-P2-006, TRC-P2-002

---

## TRC-P2-012 — Agent Integration Tests

**Design:** [tracer_phase2_design.md §10.2 — Integration Tests](./tracer_phase2_design.md#102-integration-tests)

Implements the four integration test classes that validate the agent as a whole system. Tests use `TracerAgentFixture` and `FakeNodeFixture` from TRC-P2-010 and run against real DuckDB files, real Parquet files, and real file system layout in temporary directories. Each test method exercises a multi-step scenario — interval lifecycle, crash recovery, backpressure, or end-to-end FakeNode — and asserts on observable outputs: files on disk, manifest contents, upload service calls, and record counts.

**Success conditions:**

1. `AgentIntervalLifecycleTests` class (`Tracer.Tests.Integration/AgentIntervalLifecycleTests.cs`) exists with passing test methods:
   - `AgentIntervalLifecycleTests_ThreeIntervals_ThreeReadyDirectories` — pushes records across three forced rotations via `TracerAgentFixture.ForceRotationAsync`; three interval directories each have `_ready` and `manifest.json` on disk.
   - `AgentIntervalLifecycleTests_RecordCounts_MatchPushed` — sends 200 events in one interval, rotates, reads `events.duckdb` via `DuckDbStorageReader`; `CountEventsAsync == 200`.
   - `AgentIntervalLifecycleTests_UploadServiceReceivesEachInterval` — three rotations; mock upload service received exactly three `RequestUploadAsync` calls.
   - `AgentIntervalLifecycleTests_NoDataLoss_HealthyConditions` — 500 events pushed; one rotation; total events across completed and current interval equals 500.
2. `AgentRecoveryTests` class (`Tracer.Tests.Integration/AgentRecoveryTests.cs`) exists with passing test methods:
   - `AgentRecoveryTests_OrphanedInterval_FinalizedOnRestart` — creates an orphaned interval directory (no `_ready`) in a temp `DataRoot`; starts a fresh `TracerAgentFixture` against the same `DataRoot`; after startup, the orphan has `_ready` and `manifest.json`.
   - `AgentRecoveryTests_RecoveredManifest_HasCrashReason` — the manifest for the recovered orphan has `FinalizationReason == RecoveryAfterCrash` and at least one `CaptureGap` with `Reason == UnrecoveredCrashGap`.
   - `AgentRecoveryTests_AfterRecovery_NewIntervalAcceptsRecords` — after recovery, pushing 50 events and forcing rotation produces a new interval with `EventCount == 50`.
3. `AgentBackpressureTests` class (`Tracer.Tests.Integration/AgentBackpressureTests.cs`) exists with passing test methods:
   - `AgentBackpressureTests_FastStateDropsFirst` — transport capacity 100, fast-state threshold at 50 pending; floods with mixed fast-state and event records; after rotation `CaptureGaps` contains entries with `Reason == BackpressureFastStateDropped` and none with `Reason == BackpressureEventsDropped`.
   - `AgentBackpressureTests_SaturationDropsEvents` — extreme flood past all thresholds; manifest after rotation contains at least one `CaptureGap` with `Reason == BackpressureEventsDropped`.
   - `AgentBackpressureTests_DropsReportedInManifest` — sum of `DroppedRecordCount` across all `CaptureGaps` equals total records sent minus total records stored in DuckDB.
4. `FakeNodeEndToEndTests` class (`Tracer.Tests.Integration/FakeNodeEndToEndTests.cs`) exists with passing test methods:
   - `FakeNodeEndToEndTests_CalmScenario_ProducesIntervals` — runs `FakeNodeFixture` with `CalmScenario` and 15-minute intervals; fixture returns at least one `IntervalManifest`; no manifest has `FinalizationReason == RecoveryAfterCrash`.
   - `FakeNodeEndToEndTests_AllIntervalsUploaded` — count of `IntervalZipPaths` equals count of ready interval directories under `DataRoot`.
   - `FakeNodeEndToEndTests_TotalEventCount_MatchesScenario` — sum of `EventCount` across all manifests equals the number of events the `CalmScenario` is configured to produce (verified against a parallel `MockDataSource` enumeration).
   - `FakeNodeEndToEndTests_GracefulShutdown_LastInterval_HasGracefulReason` — the final manifest has `FinalizationReason == GracefulShutdown`.
5. `dotnet test Tracer.Tests.Integration --configuration Release` exits with code 0; total elapsed time is under 60 seconds on the CI machine.
6. All Phase 1 and Phase 2 integration tests pass.

**Dependencies:** TRC-P2-010, TRC-P2-011

<!-- PHASE 2 TASKS END -->

<!-- PHASE 3 TASKS BEGIN -->

## TRC-P3-001 — `Tracer.Observer` Assembly

**Design:** [tracer_phase3_design.md §2 — Project Layout Additions](./tracer_phase3_design.md#2-project-layout-additions); [§3 — The TracerObserver Process](./tracer_phase3_design.md#3-the-tracerobserver-process) (§3.1–§3.11)
**Architecture:** [tracer_architecture_v1.md §12 — TracerObserver (Live Mode)](./tracer_architecture_v1.md#12-tracerobserver-live-mode); [§18 — Build Sequence](./tracer_architecture_v1.md#18-build-sequence)

Creates the `Tracer.Observer` project and all its constituent types: `Program.cs` (LOG_FILE convention, graceful exit codes), `ObserverHostBuilder` (WebApplicationBuilder wiring for Kestrel, DI, Serilog, CORS, NSwag, Windows-service support), `ObserverConfig`/`DataSourcesConfig`/`LiveStreamingConfig` (all fields per §3.5), `ObserverIngestionPipeline` (multi-source concurrent drain via `Task.WhenAll`, per-record routing to `IntervalRotator.CurrentWriter`, event broadcast, failure isolation per §3.7), `ObserverStateReporter` with `RollingCounter` (sliding-window bucketed counter per §3.10), `ReadOnlyConnectionPool` (rotation-aware, FIFO, 8-slot, `PooledConnection` disposes-not-returns after interval switch per §3.9), `NamedDataSource`/`DataSourceComposition` (Mock-only in Phase 3 per §3.8), and `ObserverHostedService` (recovery → interval open → pool init → ingestion + retention + rotation loops → graceful shutdown rotation per §3.11). Adds `ObserverFixture` and `WebApiFixture` to `Tracer.TestHarness`.

**Success conditions:**

1. `Tracer.Observer.csproj` exists at `src/Tracer.Observer/`, references `Tracer.Core`, `Tracer.Storage.DuckDB`, `Tracer.Adapters.Mock`, and `Tracer.WebApi`; `dotnet build --configuration Release` exits with code 0 and zero warnings.
2. `ObserverHostBuilder.Build(string[])` returns a `WebApplication` with Kestrel bound to `ObserverConfig.HttpPort`, `ObserverHostedService` registered as a hosted service, all query services registered as singletons, and `LiveEventBroadcaster` registered as both singleton and hosted service.
3. `DataSourceComposition.Build` with `Kind = "Mock"` and one `MockSourceEntry` returns one `NamedDataSource`; `Kind = "UnknownKind"` throws `InvalidOperationException`; `Kind = "Mock"` with zero `Sources` throws `InvalidOperationException`.
4. `ReadOnlyConnectionPool.InitializeAsync` opens `poolSize` (8) read-only DuckDB connections against the target path; `AcquireAsync` returns a connection immediately; `PooledConnection.DisposeAsync` returns it to the pool (pool count is restored to 8 after return).
5. After `ReadOnlyConnectionPool.OnIntervalRotatedAsync` with a new path, connections borrowed before the call are disposed-on-return (not re-pooled), verified by asserting pool remains at full capacity after those connections return.
6. `ObserverIngestionPipeline.RunAsync` with a single source producing 50 `EventRecord` values routes each to `IntervalRotator.CurrentWriter.AppendEventAsync` and calls `LiveEventBroadcaster.Publish` for each; `OperationCanceledException` on the cancellation token stops all sources cleanly.
7. A write failure on one record in `ObserverIngestionPipeline` increments `ObserverStateReporter.DroppedTotal` by 1 and the pipeline continues processing the next record without throwing.
8. `ObserverStateReporter.IncrementIngested` updates `IngestedTotal`, `IngestedLastMinute`, and `LastEventUtc`; `IncrementDropped` updates `DroppedTotal` only; `RollingCounter.Count` returns 0 after the configured window has elapsed with no increments.
9. `ObserverHostedService`: on start, recovery runs before `OpenCurrentAsync`; `ReadOnlyConnectionPool.InitializeAsync` is called after the first interval opens; on a forced rotation, `pool.OnIntervalRotatedAsync` is called with the new active DB path; on graceful shutdown the final rotation carries `ManifestFinalizationReason.GracefulShutdown`.
10. `ObserverIngestionTests` class (`Tracer.Tests.Unit/Observer/ObserverIngestionTests.cs`) exists with passing test methods: `Records_WrittenToCurrentWriter`, `Events_PublishedToLiveBroadcaster`, `SlowState_WrittenButNotBroadcast`, `FastState_WrittenViaAppendFastStateAsync`, `Cancellation_PropagatesCleanly`, `WriteFailure_IncrementsDropCounter_PipelineContinues`.
11. `ObserverStateReporterTests` class (`Tracer.Tests.Unit/Observer/ObserverStateReporterTests.cs`) exists with passing test methods: `IncrementIngested_UpdatesAllCounters`, `IncrementDropped_UpdatesDroppedOnly`, `Snapshot_ReflectsCurrentState`, `RollingCounter_ReturnsZeroAfterWindowElapsed`, `RollingCounter_SumsMultipleBucketsWithinWindow`.
12. `ReadOnlyConnectionPoolTests` class (`Tracer.Tests.Unit/Observer/ReadOnlyConnectionPoolTests.cs`) exists with passing test methods: `InitializeAsync_OpensConfiguredPoolSize`, `AcquireAsync_ReturnsConnection`, `PooledConnection_DisposeAsync_ReturnsToPool`, `OnIntervalRotated_BorrowedConnectionDisposesOnReturn`, `DisposeAsync_ClosesAllConnections`, `AcquireAsync_AfterDispose_ThrowsObjectDisposedException`.
13. `ObserverHostedServiceTests` class (`Tracer.Tests.Unit/Observer/ObserverHostedServiceTests.cs`) exists with passing test methods: `OnStart_RecoveryRunsBeforeIntervalOpen`, `OnStart_PoolInitializedAfterIntervalOpen`, `OnRotation_PoolRefreshedToNewDbPath`, `OnGracefulShutdown_FinalRotationHasGracefulReason`, `PoolRefreshFailure_Logged_HostNotCrashed`.
14. `ObserverFakeNodeEndToEndTests` class (`Tracer.Tests.Integration/ObserverFakeNodeEndToEndTests.cs`) exists with passing test methods: `GetSessions_ReturnsActiveSession`, `GetScenarioNotables_ReturnsNotablesFromScenario`, `GetScenarioPhases_ReturnsActivePhaseName`.
15. `ObserverRotationIntegrationTests` class (`Tracer.Tests.Integration/ObserverRotationIntegrationTests.cs`) exists with passing test methods: `FirstInterval_FinalizedWithReady_AfterRotation`, `SecondInterval_QueriesReturnCurrentIntervalEvents`, `Queries_DuringRotation_SucceedAfterBriefBlock`.
16. All Phase 1, 2, and 3 integration tests pass.

**Dependencies:** TRC-P2-012

---

## TRC-P3-002 — `Tracer.WebApi` Project Setup and Cross-Cutting Middleware

**Design:** [tracer_phase3_design.md §2 — Project Layout Additions](./tracer_phase3_design.md#2-project-layout-additions); [§4.1 — Phase 3 Endpoint Set](./tracer_phase3_design.md#41-phase-3-endpoint-set); [§4.5 — Error Handling](./tracer_phase3_design.md#45-error-handling)
**Architecture:** [tracer_architecture_v1.md §14 — Web API Surface](./tracer_architecture_v1.md#14-web-api-surface)

Creates the `Tracer.WebApi` project with all shared infrastructure that every endpoint relies on: `Tracer.WebApi.csproj` (referencing `Tracer.Core`, `Tracer.Storage.DuckDB`, `Microsoft.AspNetCore.OpenApi`, `NSwag.AspNetCore`, `NSwag.MSBuild`), `ApiExceptionMiddleware` (writes RFC 7807 problem-details JSON, no stack traces in response body), `ProblemDetailsFactory` (maps `ArgumentException` → 400, `TracerStorageException` → 500, all others → 500 with sanitized message), `HealthEndpoints` (`GET /api/health`), and `OpenApiConfiguration`. Also declares the MSBuild NSwag target that regenerates `tracer-viewer/src/api/tracerApiClient.ts` on Debug builds, and registers CORS (`AllowAnyOrigin`/`AllowAnyMethod`/`AllowAnyHeader`) and the static-file SPA fallback in the middleware pipeline.

**Success conditions:**

1. `Tracer.WebApi.csproj` exists at `src/Tracer.WebApi/`, references `Tracer.Core` and `Tracer.Storage.DuckDB`; `dotnet build --configuration Release` exits with code 0 and zero warnings.
2. The middleware pipeline (configured via `ObserverHostBuilder.ConfigureMiddleware`) registers CORS before endpoint routing, the exception handler before CORS, and NSwag Swagger UI only when `app.Environment.IsDevelopment()` is true.
3. `ApiExceptionMiddleware.HandleAsync` with an `ArgumentException` writes HTTP 400, `Content-Type: application/problem+json`, and a `detail` field containing the exception message; the response body contains no stack-trace text.
4. `ApiExceptionMiddleware.HandleAsync` with any other exception writes HTTP 500 with `detail` equal to `"An unexpected error occurred"` and no stack-trace text in the body.
5. `ProblemDetailsFactory.From(new ArgumentException("x"))` returns `ProblemDetails` with `Status = 400`; `From(new TracerStorageException("y"))` returns `Status = 500`; `From(null)` returns `Status = 500`.
6. `GET /api/health` on a `WebApplicationFactory`-hosted test server returns `200 OK` with a JSON body containing `"status": "ok"`, without requiring a running DuckDB file.
7. The `GenerateTypeScriptClient` MSBuild target is declared in `Tracer.WebApi.csproj` with `Condition="'$(Configuration)' == 'Debug'"` and specifies an output path resolving to `tracer-viewer/src/api/tracerApiClient.ts` relative to the project root.
8. `dotnet build Tracer.WebApi.csproj --configuration Release` produces no NSwag generation output (target is skipped in Release) and exits with code 0.
9. All Phase 1, 2, and 3 integration tests pass.

**Dependencies:** TRC-P3-001

---

## TRC-P3-003 — Session and Topology Endpoints

**Design:** [tracer_phase3_design.md §4.2 — Endpoint Implementations](./tracer_phase3_design.md#42-endpoint-implementations); [§4.3 — DTOs](./tracer_phase3_design.md#43-dtos); [§4.4 — Query Services](./tracer_phase3_design.md#44-query-services)
**Architecture:** [tracer_architecture_v1.md §14.1 — Discovery and Session Listing](./tracer_architecture_v1.md#141-discovery-and-session-listing)

Implements `SessionEndpoints` (`GET /api/sessions`, `GET /api/sessions/{sessionId}`), `TopologyEndpoints` (`GET /api/topology`), and their query services. `SessionQueryService.ListAsync` pairs `system.session_start` / `system.session_end` events via DuckDB SQL with `JSON_EXTRACT_STRING` on payload, returns sessions ordered descending by start time, and enriches each with a participating-node list and event count via a second aggregate query (per the two-step approach in §4.4). `TopologyQueryService.GetCurrentAsync` groups `events` by `publisher_node` for `firstSeenUtc`, `lastSeenUtc`, and `eventsPublished`. All queries acquire a connection from `ReadOnlyConnectionPool`. DTOs introduced: `SessionDto`, `TopologyDto`, `NodeInfoDto`. `DtoMappers` introduced with session/topology mapping methods.

**Success conditions:**

1. `GET /api/sessions` against a DB with two `system.session_start` events (no matching `system.session_end`) returns `200 OK` with an array of two `SessionDto` values, both with `status = "Active"`, ordered most-recent first.
2. `GET /api/sessions` with one paired `system.session_start` / `system.session_end` returns one `SessionDto` with `status = "Completed"` and `endUtc` populated.
3. `GET /api/sessions?from={iso}&to={iso}` excludes sessions whose `system.session_start` event falls outside the specified range.
4. `GET /api/sessions/{knownId}` returns `200 OK` with the matching `SessionDto`; `GET /api/sessions/{unknownId}` returns `404`.
5. `SessionDto.participatingNodes` contains the distinct `publisher_node` values from events within the session's time range; `eventCount` is the total event count for those events.
6. `GET /api/topology` returns `200 OK` with a `TopologyDto` whose `nodes` array contains one `NodeInfoDto` per distinct `publisher_node` in the active interval, each with correct `firstSeenUtc`, `lastSeenUtc`, and `eventsPublished`.
7. `SessionEndpointTests` class (`Tracer.Tests.Unit/WebApi/SessionEndpointTests.cs`) exists with passing test methods: `ListSessions_EmptyDb_ReturnsEmptyArray`, `ListSessions_OrderedByStartTimeDesc`, `ActiveSession_HasStatusActive`, `CompletedSession_HasStatusCompletedAndEndUtcSet`, `TimeRangeFilter_ExcludesOutOfRangeSessions`, `GetSession_UnknownId_Returns404`, `EventCountAndNodes_ReflectSessionTimeRange`.
8. `DtoMappingTests` class (`Tracer.Tests.Unit/WebApi/DtoMappingTests.cs`) exists with passing test methods: `SessionDto_AllFieldsMapped`, `TopologyDto_AllFieldsMapped`, `TraceId_FormattedAs16CharUppercaseHex`, `EventId_FormattedAs16CharUppercaseHex`, `NullableFields_SerializeAsMissingKeysNotNullLiterals`, `DateTimeOffset_RoundTripsThroughIso8601`.
9. `WebApiQueryRoundTripTests` class (`Tracer.Tests.Integration/WebApiQueryRoundTripTests.cs`) exists with a passing test method `GetSessions_AfterIngestion_ReturnsCorrectSessions` — pushes known events to the observer fixture and verifies the API returns matching DTOs with correct field values.
10. All Phase 1, 2, and 3 integration tests pass.

**Dependencies:** TRC-P3-002

---

## TRC-P3-004 — Scenario and Event Endpoints

**Design:** [tracer_phase3_design.md §4.2 — Endpoint Implementations](./tracer_phase3_design.md#42-endpoint-implementations); [§4.3 — DTOs](./tracer_phase3_design.md#43-dtos); [§4.4 — Query Services](./tracer_phase3_design.md#44-query-services)
**Architecture:** [tracer_architecture_v1.md §14.2 — Event Queries](./tracer_architecture_v1.md#142-event-queries); [§14.5 — Scenario Queries](./tracer_architecture_v1.md#145-scenario-queries)

Implements `ScenarioEndpoints` (`GET /api/scenario/phases`, `GET /api/scenario/notables`, `GET /api/scenario/state`) and `EventEndpoints` (`GET /api/events/{eventId}`), plus `ScenarioQueryService` and `EventLookupService`. `GetNotablesAsync` queries events where `notable_label IS NOT NULL` within the session's time range, ordered descending, with `before`-cursor pagination and `limit` validation (1–500). `GetPhasesAsync` pairs `scenario.phase_started` / `scenario.phase_ended` events by phase name. `GetCurrentStateAsync` returns an aggregated `ScenarioStateDto`. `EventLookupService.GetAsync` does a single-row `SELECT ... WHERE event_id = ?` lookup. DTOs introduced: `NotableEventDto`, `ScenarioPhaseDto`, `ScenarioStateDto`, `EventDto`. `DtoMappers` extended with scenario and event mapping methods.

**Success conditions:**

1. `GET /api/scenario/notables?sessionId={id}` returns events with non-null `notable_label` only, ordered by `occurredAtUtc` descending; events without a label are excluded.
2. `GET /api/scenario/notables?sessionId={id}&limit=100&before={iso}` returns at most 100 results all occurring strictly before the `before` timestamp.
3. `GET /api/scenario/notables?sessionId={id}&limit=0` returns `400 Bad Request` with a `ProblemDetails` body; `?limit=600` also returns `400`.
4. `GET /api/scenario/phases?sessionId={id}` pairs `scenario.phase_started` and `scenario.phase_ended` events; an unpaired phase-start has `status = "Active"` and null `endedAtUtc`; a paired phase has `status = "Completed"` with `endedAtUtc` set.
5. `GET /api/scenario/state?sessionId={id}` returns a `ScenarioStateDto` with `currentPhase` equal to the phase name of the latest unpaired `scenario.phase_started` event (or null if none), and correct `totalEvents`, `totalNotables`, and `participatingNodes`.
6. `GET /api/events/{validHexId}` with a known event returns `200 OK` with an `EventDto` where `eventId` and `traceId` are 16-character uppercase hex strings and all other fields are correctly mapped.
7. `GET /api/events/{unknownHexId}` (valid 16-char hex, no matching row) returns `404`.
8. `GET /api/events/ZZZZZZZZZZZZZZZZ` (non-hex) and `GET /api/events/ABCD` (too short) both return `400 Bad Request`.
9. `ScenarioEndpointTests` class (`Tracer.Tests.Unit/WebApi/ScenarioEndpointTests.cs`) exists with passing test methods: `GetNotables_ReturnsOnlyNotableEvents`, `GetNotables_PaginationWithBeforeCursor`, `GetNotables_LimitOutOfRange_Returns400`, `GetPhases_PairsStartAndEndEvents`, `GetPhases_UnpairedStart_StatusActive`, `GetState_ReflectsCurrentPhaseAndAggregates`.
10. `EventEndpointTests` class (`Tracer.Tests.Unit/WebApi/EventEndpointTests.cs`) exists with passing test methods: `GetEvent_ValidHexId_Returns200WithEventDto`, `GetEvent_UnknownId_Returns404`, `GetEvent_NonHexId_Returns400`, `GetEvent_WrongLengthHexId_Returns400`.
11. `DtoMappingTests` (`Tracer.Tests.Unit/WebApi/DtoMappingTests.cs`) is extended with passing test methods: `EventRecord_ToEventDto_AllFieldsMapped`, `EventRecord_ToNotableEventDto_ExcludesSubscriberAndSequenceNumber`, `Severity_SerializesAsTitleCaseString`.
12. All Phase 1, 2, and 3 integration tests pass.

**Dependencies:** TRC-P3-003

---

## TRC-P3-005 — SSE Live Streaming

**Design:** [tracer_phase3_design.md §5 — Live Streaming via SSE](./tracer_phase3_design.md#5-live-streaming-via-sse) (§5.1–§5.5)
**Architecture:** [tracer_architecture_v1.md §14.8 — Live Mode Streaming](./tracer_architecture_v1.md#148-live-mode-streaming)

Implements the full SSE subsystem: `LiveEventBroadcaster` (background `IHostedService`, unbounded `Channel<EventRecord>` inbox with `SingleReader = true`, fans out to all registered `SseConnection`s via `SseConnectionManager` per §5.1), `SseConnectionManager` (concurrent dictionary, enforces `MaxConcurrentSseClients` cap, `BroadcastAsync` fanout per §5.2), `SseConnection` (bounded-channel per client with `DropOldest` on full, drop counter, `ReadAsync` as `IAsyncEnumerable` per §5.2), `SseFilter` (`NotablesOnly` and `SessionId` fields per §5.2), `SseEndpoints` (`GET /api/live/notables` — SSE stream with heartbeat task and deregister-on-disconnect; `GET /api/live/status` — `LiveStatusDto` per §5.3), and `LiveStatusDto`. Confirms `ObserverIngestionPipeline.ProcessOneAsync` calls `_broadcaster.Publish(ev)` for every `EventRecord` (events only; state samples not broadcast per §3.7).

**Success conditions:**

1. `GET /api/live/notables?sessionId={id}` responds with `Content-Type: text/event-stream`, `Cache-Control: no-cache`, and `X-Accel-Buffering: no` headers.
2. On an otherwise-idle SSE connection, the client receives a `: keepalive\n\n` comment line within the configured `HeartbeatInterval` (default 15 s).
3. An `EventRecord` with a non-null `NotableLabel` published via `LiveEventBroadcaster.Publish` appears on the SSE stream as a `data: {…}\n\n` JSON line within 100 ms.
4. An `EventRecord` with a null `NotableLabel` published via `LiveEventBroadcaster.Publish` does NOT appear on a `NotablesOnly` SSE connection.
5. When `MaxConcurrentSseClients` is at capacity (e.g., set to 1 in test config and 1 client already connected), a second connection to `/api/live/notables` returns `503 Service Unavailable`.
6. When the test client disconnects (`RequestAborted`), `SseConnectionManager.Deregister` is called and `ActiveCount` decrements by 1.
7. A slow SSE client whose per-client channel is full triggers `DropOldest`; the connection's drop counter increments; the stream remains alive and subsequently delivers new events.
8. `GET /api/live/status` returns `200 OK` with a `LiveStatusDto`; `ingestionHealthy = true` when `ObserverStateReporter.Snapshot().LastEventUtc` is within 60 seconds of now; `ingestionHealthy = false` when `LastEventUtc` is null or older than 60 seconds; `activeSseClients` equals `SseConnectionManager.ActiveCount`.
9. `SseEndpointTests` class (`Tracer.Tests.Unit/WebApi/SseEndpointTests.cs`) exists with passing test methods: `SseEndpoint_Returns200_WithEventStreamContentType`, `Heartbeat_SentWithinConfiguredInterval`, `NotableEvent_AppearsOnStream`, `NonNotableEvent_NotSentOnNotablesOnlyStream`, `AtCapacity_Returns503`, `ClientDisconnect_DeregistersConnection`, `SlowClient_DropOldest_StreamStaysAlive`.
10. `LiveStatusTests` class (`Tracer.Tests.Unit/WebApi/LiveStatusTests.cs`) exists with passing test methods: `LiveStatus_ReflectsStateReporterCounters`, `IngestionHealthy_TrueWhenLastEventWithin60s`, `IngestionHealthy_FalseWhenNoEventsOrStale`, `ActiveSseClients_MatchesConnectionManagerCount`.
11. `LiveStreamingTests` class (`Tracer.Tests.Integration/LiveStreamingTests.cs`) exists with passing test methods: `PushNotableEvents_AppearOnStreamInOrder`, `ClientReconnect_ReceivesNewEventsAfterReconnect`, `SlowClient_DropsCountedButStreamRemainsAlive`.
12. All Phase 1, 2, and 3 integration tests pass.

**Dependencies:** TRC-P3-001, TRC-P3-002

---

## TRC-P3-006 — Vue SPA Scaffold

**Design:** [tracer_phase3_design.md §6.1 — Project Setup](./tracer_phase3_design.md#61-project-setup); [§6.2 — vite.config.ts](./tracer_phase3_design.md#62-viteconfigts); [§6.3 — App Shell and Routing](./tracer_phase3_design.md#63-app-shell-and-routing); [§6.4 — Generated API Client](./tracer_phase3_design.md#64-generated-api-client); [§6.5 — Stores (Pinia)](./tracer_phase3_design.md#65-stores-pinia); [§6.12 — Color Tokens](./tracer_phase3_design.md#612-color-tokens)
**Architecture:** [tracer_architecture_v1.md §15 — Viewer Architecture](./tracer_architecture_v1.md#15-viewer-architecture)

Scaffolds the `tracer-viewer/` frontend project: `package.json` (Vue 3.4+, Vite 5+, TypeScript 5.3+, Pinia, Vue Router 4, `@microsoft/fetch-event-source`, Vitest, `@vue/test-utils`, Playwright, ESLint + `eslint-plugin-vue`, Prettier, Sass), `vite.config.ts` (dev server port 5173, `/api` proxy to `localhost:5300`, production build to `../src/Tracer.Observer/wwwroot`), `tsconfig.json`/`tsconfig.app.json`/`tsconfig.node.json`, `App.vue` (RouterView + fade transition), `src/router/index.ts` (three routes: `/` redirect, `/sessions`, `/scenario/:sessionId`), Pinia stores (`sessionStore.ts`, `liveStore.ts`, `topologyStore.ts`), shared components `AppHeader.vue`/`AppShell.vue`/`LoadingSpinner.vue`/`ErrorMessage.vue`, design-token SCSS (`styles/tokens.scss`, `styles/base.scss`), Vitest configuration (`vitest.config.ts` or inline in `vite.config.ts`), and Playwright configuration (`playwright.config.ts`). The NSwag-generated `tracerApiClient.ts` (or a hand-authored stub with the same exported types and `TracerApiClient` class) must be present so the stores compile.

**Success conditions:**

1. `tracer-viewer/package.json` declares all runtime and dev dependencies listed in §6.1; `pnpm install` (or `npm install`) completes without errors.
2. `pnpm run build` executes `vue-tsc -b && vite build` and exits with code 0; build artifacts appear under `src/Tracer.Observer/wwwroot/`.
3. `vite.config.ts` sets `server.port = 5173` and proxies `/api/*` to `http://localhost:5300` with `changeOrigin: true`; `build.outDir` resolves to `src/Tracer.Observer/wwwroot`.
4. `src/router/index.ts` declares exactly three routes: `"/"` (redirects to `"/sessions"`), `"/sessions"` (lazy-loads `SessionBrowserView.vue`), `"/scenario/:sessionId"` (lazy-loads `ScenarioView.vue`, `props: true`).
5. `sessionStore` has state fields `current`, `state`, `loading`, `error`; `load(sessionId)` calls `api.getSession` and `api.getScenarioState`, setting `loading` to `true` before and `false` after; `refreshState()` updates `state` for the current session; `clear()` resets all fields to initial values.
6. `liveStore` has connection state `{ connected: boolean, lastEventAt: Date | null, reconnectAttempts: number }`; `setConnected(true)` sets `connected = true` and resets `reconnectAttempts` to 0; `onEvent()` updates `lastEventAt`; `onReconnect()` increments `reconnectAttempts`.
7. `styles/tokens.scss` defines CSS custom properties `--c-bg`, `--c-bg-surface`, `--c-bg-subtle`, `--c-text`, `--c-text-muted`, `--c-accent`, `--c-success`, `--c-warning`, `--c-danger`, `--font-sans`, `--font-mono` with values matching §6.12.
8. `ErrorMessage.vue` accepts a `message` prop and emits a `retry` event when its retry trigger is activated.
9. `pnpm run test:unit` (Vitest) exits with code 0 on the scaffolded source (at minimum a smoke test importing `App.vue` without error).
10. `pnpm run lint` exits with code 0 on all scaffolded source files.
11. All Phase 1, 2, and 3 integration tests pass.

**Dependencies:** TRC-P3-003, TRC-P3-004, TRC-P3-005

---

## TRC-P3-007 — Session Browser View

**Design:** [tracer_phase3_design.md §6.6 — useLiveSse Composable](./tracer_phase3_design.md#66-uselivesse-composable); [§6.7 — Session Browser View](./tracer_phase3_design.md#67-session-browser-view)
**Architecture:** [tracer_architecture_v1.md §15 — Viewer Architecture](./tracer_architecture_v1.md#15-viewer-architecture)

Implements the entry-point view and the live-connection composable. `useLiveNotables(sessionId)` uses `fetchEventSource` to subscribe to `/api/live/notables?sessionId=...`; on `onopen` (200 response) sets `liveStore.connected = true`; on `onmessage` parses the `NotableEventDto` JSON and prepends to the events list (capped at 200, deduped); on `onclose`/`onerror` updates `liveStore` and lets `fetchEventSource` handle backoff; unmounts the controller via `onUnmounted`. `SessionBrowserView.vue` loads sessions via `api.listSessions()` on mount, renders a grid of `SessionCard` components, shows `LoadingSpinner` while loading, `ErrorMessage` with retry on error, and an empty-state message when the list is empty; clicking a card navigates to `/scenario/{sessionId}`. `SessionCard.vue` renders `scenarioId`, `label`, formatted `startUtc`, `status` badge, `eventCount`, and node count. `LiveIndicator.vue` reads `liveStore.connection` and shows pulsing green ("Live"), static yellow ("Quiet", > 30 s since last event), or red ("Disconnected") per §6.11. `NotableEventsList.vue` merges initial REST-fetched notables with live SSE arrivals, deduplicates by `eventId` (live first), and renders `NotableEventCard` items in a `TransitionGroup`.

**Success conditions:**

1. `SessionBrowserView.vue` renders one `SessionCard` per entry in `sessions.value`; while `loading = true` only `LoadingSpinner` is visible; when `sessions.length === 0` and not loading, the text "No sessions yet. Start FakeNode and refresh." is rendered; when `error` is non-null, `ErrorMessage` is rendered with a retry button.
2. Clicking a `SessionCard` calls `router.push({ name: 'scenario', params: { sessionId: s.sessionId } })`.
3. When `ErrorMessage` emits `retry`, `SessionBrowserView` re-calls `load()`; the `loading` state transitions to `true` during the reload.
4. `SessionCard.vue` renders `session.scenarioId`, a formatted `session.startUtc`, `session.status` as a badge, `session.eventCount`, and the node count (`session.participatingNodes.length`).
5. `useLiveNotables`: calling the mock `onopen` with a 200 response sets `liveStore.connection.connected = true`; calling `onmessage` with a valid `NotableEventDto` JSON string prepends the event to `events.value`; after 201 such calls `events.value.length === 200` (cap enforced); calling `onclose` sets `connected = false`; calling `onerror` calls `liveStore.onReconnect()`.
6. `LiveIndicator.vue` with `connected = true` and `lastEventAt` within 30 seconds renders CSS class `live-indicator--live` and text "Live"; with `lastEventAt` older than 30 seconds renders `live-indicator--stale` and "Quiet"; with `connected = false` renders `live-indicator--disconnected` and "Disconnected".
7. `NotableEventsList.vue` with `initialEvents = [A, B]` and `liveEvents = [C, A]` (A duplicate by `eventId`): `allEvents` contains exactly `[C, A, B]` — live events first, duplicates removed, preserving order.
8. `useLiveSse.spec.ts` (`tracer-viewer/tests/unit/useLiveSse.spec.ts`) exists with passing test methods: `Connect_SetsLiveStoreConnected`, `Message_PrependsEventToList`, `Message_CapsListAt200Events`, `Close_SetsDisconnected`, `Error_IncrementsReconnectAttempts`.
9. `NotableEventsList.spec.ts` (`tracer-viewer/tests/unit/NotableEventsList.spec.ts`) exists with passing test methods: `MergesInitialAndLiveEvents_LiveFirst`, `DeduplicatesEventsByEventId`, `ShowsEmptyState_WhenNoEvents`.
10. Playwright E2E test file `session-browser.spec.ts` (`tracer-viewer/tests/e2e/session-browser.spec.ts`) exists with at least one test `loads_and_shows_session_card` that navigates to `http://localhost:5300/sessions` and asserts a `.session-card` element is visible within 10 seconds (runs against a live FakeNode + Observer instance).
11. All Phase 1, 2, and 3 integration tests pass.

**Dependencies:** TRC-P3-005, TRC-P3-006

---

## TRC-P3-008 — Scenario View

**Design:** [tracer_phase3_design.md §6.8 — Scenario View](./tracer_phase3_design.md#68-scenario-view--the-first-user-facing-view); [§6.9 — ScenarioStatePanel Component](./tracer_phase3_design.md#69-scenariostatepanel-component); [§6.10 — NotableEventsList Component](./tracer_phase3_design.md#610-notableeventslist-component)

Implements `ScenarioView.vue` and its dedicated child components: `ScenarioStatePanel.vue` (status, current phase, elapsed time, totals, and node-list at-a-glance panel per §6.9), `ScenarioPhaseBanner.vue` (renders one row per `ScenarioPhaseDto` from `/api/scenario/phases`, distinguishing active and completed phases), and `NotableEventsFeed.vue` (live-updating events feed that loads an initial page of notables via `api.getScenarioNotables` then merges arriving `liveEvents` prop entries, deduplicating by `eventId` with live events taking precedence, displayed in a `TransitionGroup`). `ScenarioView` accepts `sessionId` as a route prop, calls `sessionStore.load(sessionId)` on mount and on prop change via `watch`, runs a 5-second `setInterval` refreshing `sessionStore.refreshState()` (cleared on `onUnmounted`), and wires `useLiveNotables(sessionId)` from TRC-P3-007 as the live events source.

**Success conditions:**

1. `ScenarioView.vue` with `sessionStore.loading = true` and no current session renders only `LoadingSpinner` and hides the layout grid; with a loaded session it renders `ScenarioStatePanel`, `ScenarioPhaseBanner`, `NotableEventsFeed`, and `LiveIndicator` inside the two-column grid.
2. On mount with `sessionId = 'abc'`, `sessionStore.load` is called once with `'abc'`; when the `sessionId` prop changes to `'def'`, `load` is called again with `'def'`.
3. A Vitest fake timer advanced by 5000 ms triggers at least one call to `sessionStore.refreshState()`; after `onUnmounted`, further timer advancement produces no additional calls.
4. `ScenarioStatePanel.vue` with `state.currentPhase = 'engagement'` renders the text `engagement` in the phase display element; with `state = null` renders `—` in both the current-phase and elapsed-time positions.
5. `ScenarioStatePanel.vue` with `session.status = 'Active'` applies CSS class `scenario-state-panel__value--active` to the status value element; `status = 'Completed'` applies `scenario-state-panel__value--completed`.
6. `ScenarioPhaseBanner.vue` given an array of two `ScenarioPhaseDto` items renders exactly two phase rows; the row with `status = 'Active'` contains no end-time text; the row with `status = 'Completed'` renders a formatted `endedAtUtc` timestamp.
7. `NotableEventsFeed.vue` with `liveEvents = [X]` and initial REST response `[Y, X]` (X a duplicate by `eventId`) produces `allEvents = [X, Y]` — live events first, duplicate removed, order preserved.
8. `NotableEventsFeed.vue` while `loading = true` and `allEvents` is empty renders a loading placeholder; when `allEvents.length === 0` and not loading renders the text `No notable events yet.`
9. `ScenarioView.spec.ts` (`tracer-viewer/tests/unit/ScenarioView.spec.ts`) exists with passing test methods: `Load_CalledWithSessionId_OnMount`, `Load_CalledAgain_OnSessionIdChange`, `RefreshTimer_InvokesRefreshState_Every5s`, `RefreshTimer_ClearedOnUnmount`, `ShowsSpinner_WhileLoadingNoSession`, `ShowsGrid_WhenSessionIsLoaded`.
10. `ScenarioStatePanel.spec.ts` (`tracer-viewer/tests/unit/ScenarioStatePanel.spec.ts`) exists with passing test methods: `ShowsCurrentPhase`, `ShowsElapsedTime`, `NullState_ShowsDashes`, `StatusActive_AppliesActiveClass`, `StatusCompleted_AppliesCompletedClass`, `RendersAllParticipatingNodes`.
11. `ScenarioPhaseBanner.spec.ts` (`tracer-viewer/tests/unit/ScenarioPhaseBanner.spec.ts`) exists with passing test methods: `RendersOneRowPerPhase`, `ActivePhase_OmitsEndTime`, `CompletedPhase_ShowsFormattedEndTime`.
12. All Phase 1, 2, and 3 integration tests pass.

**Dependencies:** TRC-P3-007

---

## TRC-P3-009 — Observer+FakeNode Integration Tests

**Design:** [tracer_phase3_design.md §8.2 — Backend Integration Tests](./tracer_phase3_design.md#82-backend-integration-tests)

Writes the two backend integration test classes declared as success criteria in TRC-P3-001. `ObserverFakeNodeEndToEndTests` starts a single-process `ObserverFixture` backed by `MockDataSource` running the `CombatEngagement` scenario, advances simulated time to produce session-start, phase, and notable events, and verifies they are queryable through the Web API. `ObserverRotationIntegrationTests` drives `IntervalRotator` through two intervals using `SimulatedClock` (1-minute duration), confirms the first interval is finalized with status `_ready`, and asserts that the read-only connection pool is refreshed so queries target the current interval. Also adds a multi-node ingestion test validating §1.3 success criterion #6.

**Success conditions:**

1. `ObserverFakeNodeEndToEndTests` class (`Tracer.Tests.Integration/ObserverFakeNodeEndToEndTests.cs`) contains a shared `ObserverFixture` (initialized once per class) that starts the observer with a `MockDataSource` producing `CombatEngagement` events; the fixture exposes the base URL for HTTP assertions.
2. `GetSessions_ReturnsActiveSession` — after the scenario emits `system.session_start`, `GET /api/sessions` returns exactly one `SessionDto` with `status = "Active"` and a non-null `sessionId`.
3. `GetScenarioNotables_ReturnsNotablesFromScenario` — after at least one notable event is ingested, `GET /api/scenario/notables?sessionId={id}` returns a non-empty array where every element has a non-null `notableLabel`.
4. `GetScenarioPhases_ReturnsActivePhaseName` — while the first phase is active (unmatched `scenario.phase_started`), `GET /api/scenario/phases?sessionId={id}` contains a `ScenarioPhaseDto` with `status = "Active"` and `phaseName` matching the scenario's first declared phase name.
5. `ObserverRotationIntegrationTests` class (`Tracer.Tests.Integration/ObserverRotationIntegrationTests.cs`) uses `SimulatedClock` with interval duration 1 minute and an `ObserverFixture` that exposes `PushEventsAsync(IEnumerable<EventRecord>)`.
6. `FirstInterval_FinalizedWithReady_AfterRotation` — 100 events pushed into interval 1; clock advances past the boundary; the closed interval's DuckDB manifest file has status `_ready`.
7. `SecondInterval_QueriesReturnCurrentIntervalEvents` — 100 more events pushed into interval 2; a query filtered to interval 2's time range returns exactly 100 results and none from interval 1.
8. `Queries_DuringRotation_SucceedAfterBriefBlock` — a query issued concurrently with the rotation moment completes within 2000 ms with a valid (possibly empty) response and does not throw.
9. `MultipleNodes_EventsFromAllNodesIngested` — two `NamedDataSource` instances with distinct `AgentId` values each produce 50 events; after ingestion `GET /api/topology` returns a `TopologyDto` with exactly two `NodeInfoDto` entries each reporting `eventsPublished = 50`.
10. All Phase 1, 2, and 3 integration tests pass.

**Dependencies:** TRC-P3-001, TRC-P3-004

---

## TRC-P3-010 — Web API Query Round-Trip Integration Tests

**Design:** [tracer_phase3_design.md §8.2 — Backend Integration Tests](./tracer_phase3_design.md#82-backend-integration-tests)

Writes the full `WebApiQueryRoundTripTests` class (stub method declared in TRC-P3-003). Each test pushes known `EventRecord` instances directly through the observer's writer via `ObserverFixture.PushAsync`, issues HTTP requests against the `WebApplicationFactory`-hosted API, and asserts that response DTO field values exactly match the ingested records. Coverage spans session listing and lookup (ordering, time-range filter, status derivation), scenario notables (label filter, pagination cursor), scenario phases (pairing logic), event lookup (hit, miss, bad-format inputs), and topology aggregation (per-node counters).

**Success conditions:**

1. `WebApiQueryRoundTripTests` class (`Tracer.Tests.Integration/WebApiQueryRoundTripTests.cs`) uses a shared `ObserverFixture` via `IClassFixture<ObserverFixture>` and exposes `PushAsync(IEnumerable<EventRecord>)` to inject records without touching the SSE broadcast path.
2. `GetSessions_AfterIngestion_ReturnsCorrectSessions` — two `system.session_start` events with known `sessionId` and `startUtc` pushed; `GET /api/sessions` returns both DTOs ordered descending by `startUtc`, each with `status = "Active"`, `sessionId`, and `startUtc` matching the push.
3. `GetSession_ById_ReturnsMatchingDto` — `GET /api/sessions/{id}` for a known `sessionId` returns `200 OK` with `sessionId`, `startUtc`, and `participatingNodes` all matching the ingested records.
4. `GetScenarioNotables_ReturnsOnlyNotableEvents_WithCorrectFields` — events with and without `NotableLabel` pushed; `GET /api/scenario/notables?sessionId={id}` returns only labeled events, each with `notableLabel`, `occurredAtUtc`, `topic`, and `severity` matching the push.
5. `GetScenarioNotables_BeforeCursor_ReturnsSubset` — ten notables spanning a time range pushed; `GET /api/scenario/notables?sessionId={id}&limit=3&before={midpoint}` returns exactly 3 results all with `occurredAtUtc` strictly before `midpoint`.
6. `GetScenarioPhases_PairsStartAndEnd` — a paired `scenario.phase_started`/`scenario.phase_ended` and an unpaired `scenario.phase_started` pushed; `GET /api/scenario/phases?sessionId={id}` returns one entry with `status = "Completed"` and `endedAtUtc` set, and one with `status = "Active"` and null `endedAtUtc`.
7. `GetEvent_ById_ReturnsCorrectEventDto` — a known `EventRecord` pushed; `GET /api/events/{eventId}` returns an `EventDto` with `eventId` as 16-char uppercase hex, and `traceId`, `topic`, `severity`, `occurredAtUtc` all matching the push.
8. `GetEvent_UnknownId_Returns404` — `GET /api/events/{unknownHexId}` (valid 16-char hex, no matching row) returns `404`.
9. `GetTopology_AfterIngestion_ReturnsNodeInfo` — events from two distinct `publisher_node` values pushed; `GET /api/topology` returns a `TopologyDto` with `nodes.Count = 2`, each `NodeInfoDto` having a correct `firstSeenUtc` and `eventsPublished`.
10. All Phase 1, 2, and 3 integration tests pass.

**Dependencies:** TRC-P3-003, TRC-P3-004, TRC-P3-009

---

## TRC-P3-011 — Live Streaming Integration Tests

**Design:** [tracer_phase3_design.md §8.2 — Backend Integration Tests](./tracer_phase3_design.md#82-backend-integration-tests); [§5 — Live Streaming via SSE](./tracer_phase3_design.md#5-live-streaming-via-sse)

Expands `LiveStreamingTests` (stub class declared in TRC-P3-005) with all integration-level assertions from §8.2 plus multi-node and session-filter scenarios. Tests connect an `HttpClient` stream to `/api/live/notables` against a `WebApplicationFactory` host and push events through `ObserverIngestionPipeline` so the full broadcast path — ingestion → `LiveEventBroadcaster` → `SseConnectionManager` → per-client channel — is exercised end-to-end.

**Success conditions:**

1. `LiveStreamingTests` class (`Tracer.Tests.Integration/LiveStreamingTests.cs`) connects a streaming `HttpClient` to `GET /api/live/notables?sessionId={id}` and reads SSE `data:` lines via a background `Task` using `StreamReader`.
2. `PushNotableEvents_AppearOnStreamInOrder` — five notable events with sequential `occurredAtUtc` values pushed; all five `data:` lines appear on the stream in that order within 500 ms of the final push.
3. `ClientReconnect_ReceivesNewEventsAfterReconnect` — a client connects, receives one event, disconnects, reconnects to the same endpoint, and receives a subsequent new event; the server does not throw or hang between connections.
4. `SlowClient_DropsCountedButStreamRemainsAlive` — a client connected with an artificially blocked read loop; 50 events exceeding the per-client channel capacity pushed; `SseConnection.DropCount` for that client is greater than zero; after releasing the blockade the stream delivers the next pushed event.
5. `MultipleNodes_AllEventsAppearInUnifiedStream` — two `NamedDataSource` instances with distinct `AgentId` values each produce 10 notable events concurrently via `ObserverIngestionPipeline`; a single SSE subscription receives all 20 distinct events (verified by `eventId`) within 1000 ms.
6. `SessionFilter_ExcludesEventsFromOtherSession` — notable events for two different `sessionId` payload values pushed; the SSE stream filtered to `sessionId=A` receives only events whose payload `sessionId` matches A and none from session B.
7. `Heartbeat_ReceivedWithinConfiguredInterval` — with `HeartbeatInterval` set to 1 s in the test host config, a connected idle client receives a `: keepalive` SSE comment line within 1500 ms.
8. All Phase 1, 2, and 3 integration tests pass.

**Dependencies:** TRC-P3-005, TRC-P3-009

---

## TRC-P3-012 — Frontend Component Tests (Vitest)

**Design:** [tracer_phase3_design.md §8.3 — Frontend Unit Tests (Vitest)](./tracer_phase3_design.md#83-frontend-unit-tests-vitest)

Writes the remaining Vitest unit test files not created as part of earlier feature tasks. Introduces the `useScenarioQuery(sessionId)` composable (wraps `api.getScenarioNotables`, `api.getScenarioPhases`, and `api.getScenarioState` into reactive refs with `loading` and `error` states, runs all three calls concurrently via `Promise.all`) and adds `useScenarioQuery.spec.ts`, `SessionCard.spec.ts`, and `NotableEventsFeed.spec.ts` (covering the API-fetch and error-handling behavior of `NotableEventsFeed.vue` not addressed by TRC-P3-007's merging-logic tests). All component tests use `@vue/test-utils` with a Pinia test context; API calls are intercepted via `vi.mock`.

**Success conditions:**

1. `useScenarioQuery` composable exists at `tracer-viewer/src/composables/useScenarioQuery.ts`; it exposes `notables`, `phases`, `state`, `loading`, and `error` as reactive refs; calling `load()` sets `loading = true`, awaits `Promise.all` over the three API calls, then sets `loading = false`.
2. `useScenarioQuery.spec.ts` (`tracer-viewer/tests/unit/useScenarioQuery.spec.ts`) exists with passing test methods: `Load_SetsLoadingTrueThenFalse`, `Load_PopulatesNotablesPhasesAndState`, `Load_OnApiError_SetsErrorRefAndClearsLoading`, `ReactiveSessionId_ReloadsOnChange`.
3. `SessionCard.spec.ts` (`tracer-viewer/tests/unit/SessionCard.spec.ts`) exists with passing test methods: `RendersScenarioId`, `RendersFormattedStartUtc`, `RendersStatusBadge`, `RendersEventCount`, `RendersNodeCount`.
4. `NotableEventsFeed.spec.ts` (`tracer-viewer/tests/unit/NotableEventsFeed.spec.ts`) exists with passing test methods: `OnMount_CallsGetScenarioNotables_ViaApi`, `ApiError_LoadingSetFalse_ListRemainsEmpty`, `InitialLoad_PopulatesInitialEvents`, `LiveAndInitial_MergedInCorrectOrder`.
5. `pnpm run test:unit` exits with code 0 with all new spec files included in the Vitest run; coverage from TRC-P3-007 and TRC-P3-008 spec files remains unbroken.
6. `pnpm run lint` exits with code 0 over all new spec files.
7. All Phase 1, 2, and 3 integration tests pass.

**Dependencies:** TRC-P3-007, TRC-P3-008

---

## TRC-P3-013 — Playwright E2E Smoke Tests

**Design:** [tracer_phase3_design.md §8.4 — E2E Tests (Playwright)](./tracer_phase3_design.md#84-e2e-tests-playwright)

Writes `scenario-view.spec.ts` — the Playwright smoke suite that validates the full Phase 3 demo path end-to-end against a live FakeNode+Observer process on `localhost:5300`. Tests assert navigation, session card visibility, Scenario View load, live indicator state, SSE event arrival, and cold-cache page-load performance, all within the targets specified in §1.3.

**Success conditions:**

1. `scenario-view.spec.ts` (`tracer-viewer/tests/e2e/scenario-view.spec.ts`) is registered in `playwright.config.ts`; `pnpm run test:e2e` discovers and executes it.
2. `NavigatesToSessionBrowser_OnRootLoad` — navigating to `http://localhost:5300/` produces a URL matching `/sessions` within 3000 ms.
3. `SessionCard_Visible_Within10s` — at least one `.session-card` element is visible within 10 000 ms of the initial navigation (FakeNode must emit `system.session_start` within that window).
4. `ClickSessionCard_OpensScenarioView` — clicking the first `.session-card` navigates to a URL matching `/scenario/` and `.scenario-state-panel` is visible within 3000 ms.
5. `LiveIndicator_TurnsGreen_Within5s` — after Scenario View loads, `.live-indicator--live` is visible within 5000 ms, confirming the SSE connection is established and the first event received.
6. `NotableEvents_AppearWithin500ms_OfLiveIndicator` — within 500 ms of `.live-indicator--live` becoming visible, at least one `.notable-event-card` element is present in the DOM.
7. `PageLoad_Cold_Under2s` — `performance.timing.domContentLoadedEventEnd - performance.timing.navigationStart` is less than 2000 ms on the first load with the browser cache cleared.
8. `playwright.config.ts` declares a `webServer` block (or equivalent `globalSetup`) that polls `http://localhost:5300/api/health` and waits for `200 OK` before test execution begins.
9. All Phase 1, 2, and 3 integration tests pass.

**Dependencies:** TRC-P3-009, TRC-P3-012

<!-- PHASE 3 TASKS END -->

<!-- PHASE 4 TASKS BEGIN -->

## TRC-P4-001 — Bundle Format

**Design:** [tracer_phase4_design.md §3 — The Bundle Format](./tracer_phase4_design.md#3-the-bundle-format)

Implements the `Tracer.Bundle` assembly's format layer — the pure data-model and naming types with no file I/O. `BundleManifest` is the root record mirroring the §3.2 JSON schema (with nested types for writer metadata, session context, statistics, and per-file entries), all serializable via `System.Text.Json` with camelCase naming. `BundleLayout` defines path-constant strings for every file in the bundle directory. `BundleSchemaV1` holds the recognized version set and the `IsRecognized` predicate. `BundleNaming.SafeFileName` replaces filesystem-hostile characters and appends a 4-character hex hash suffix to prevent collisions between distinct inputs that share the same base form.

**Success conditions:**

1. `Tracer.Bundle.csproj` exists under `src/Tracer.Bundle/` and references only `Tracer.Core`, `System.Text.Json`, and `Ulid`; no infrastructure or DuckDB packages appear.
2. `BundleManifest` is a `record` with all top-level fields from §3.2 (`BundleId`, `SchemaVersion`, `CreatedAtUtc`, `TracerVersion`, `Writer`, `TimeRange`, `SessionContext`, `ParticipatingNodes`, `FastStateScope`, `FastStateEntities`, `Statistics`, `Files`) and serializes to JSON with camelCase property names via `JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase`.
3. `BundleSchemaV1.CurrentVersion` equals `1`; `BundleSchemaV1.IsRecognized(1)` returns `true`; `IsRecognized(0)` and `IsRecognized(99)` both return `false`.
4. `BundleLayout.ManifestFile` equals `"manifest.json"`, `BundleLayout.ScenarioFile` equals `"scenario.json"`, `BundleLayout.ChecksumsFile` equals `"checksums.txt"`, and all other path constants from §3.1 are defined as non-null, non-empty `static string` fields.
5. `BundleNaming.SafeFileName("vehicle:blue:17")` returns a string in which every character is in `[a-zA-Z0-9._-]` and the last 5 characters are `_` followed by a 4-character lowercase hex suffix.
6. `BundleNaming.SafeFileName` returns distinct outputs for two different inputs that produce the same character-replaced base string (collision prevention via hash suffix is verified).
7. `BundleManifestTests` test class (`Tracer.Tests.Unit/Bundle/BundleManifestTests.cs`) exists with the following passing test methods:
   - `BundleManifest_RoundTripsViaJsonSerializer` — a fully-populated `BundleManifest` serialized to JSON and deserialized yields a record equal to the original.
   - `BundleManifest_CamelCaseJson_ContainsBundleIdKey` — the serialized JSON string contains `"bundleId"` (not `"BundleId"`).
   - `BundleSchemaV1_CurrentVersionIsOne` — `BundleSchemaV1.CurrentVersion == 1`.
   - `BundleSchemaV1_IsRecognized_TrueForOne_FalseForNinetyNine` — `IsRecognized(1)` is `true`; `IsRecognized(99)` is `false`.
   - `BundleNaming_SafeFileName_ReplacesColons` — `SafeFileName("a:b")` contains no colon character.
   - `BundleNaming_SafeFileName_DistinctInputs_ProduceDifferentOutputs` — `SafeFileName("x:y")` and `SafeFileName("x_y")` are not equal.
   - `BundleLayout_AllPathConstants_AreNonEmpty` — every `public static string` field on `BundleLayout` is non-null and non-empty.
8. All Phase 1–4 integration tests pass.

**Dependencies:** TRC-P1-002, TRC-P1-003

---

## TRC-P4-002 — Bundle Packaging

**Design:** [tracer_phase4_design.md §3 — The Bundle Format](./tracer_phase4_design.md#3-the-bundle-format) (§3.1 On-Disk Layout, §3.6 checksums.txt Format)

Adds the I/O layer to `Tracer.Bundle`: `BundleDirectoryWriter` finalizes a staging directory into a valid bundle by writing `manifest.json`, computing SHA-256 for every file in `manifest.Files`, producing `checksums.txt` in `sha256sum`-compatible format, and creating `annotations/.keep`. `BundleZipWriter` wraps `BundleDirectoryWriter` and then compresses the result using `System.IO.Compression.ZipFile`. `BundleReader` opens a bundle that is either a directory or a `.zip` file and returns the deserialized `BundleManifest`. `BundleExtractor` unzips a `.tracerbundle.zip` to a caller-supplied target directory.

**Success conditions:**

1. `BundleDirectoryWriter.WriteAsync(stagingPath, manifest, ct)` creates `manifest.json` and `checksums.txt` in `stagingPath` and creates `annotations/.keep`; all paths listed in `manifest.Files` must already exist in `stagingPath` before calling (the writer does not move data files).
2. `checksums.txt` produced by `BundleDirectoryWriter` contains exactly one line per entry in `manifest.Files`, each formatted as `<64-hex-chars>  <relative-path>` (two spaces between hash and path, matching `sha256sum -c` expectations).
3. The SHA-256 values written to `checksums.txt` match the SHA-256 of the actual file contents at write time.
4. `BundleZipWriter.WriteAsync(stagingPath, manifest, outputZipPath, ct)` produces a file readable by `System.IO.Compression.ZipArchive` with `ZipArchiveMode.Read`.
5. The ZIP archive produced by `BundleZipWriter` contains an entry at the path `manifest.json` at its root (not nested under an extra intermediate directory level).
6. `BundleReader.ReadManifestAsync(directoryPath, ct)` returns a `BundleManifest` whose fields match those written by `BundleDirectoryWriter`.
7. `BundleReader.ReadManifestAsync(zipPath, ct)` (where `zipPath` is a `.zip` file) reads the manifest without leaving extracted files on disk after the call completes.
8. `BundleExtractor.ExtractAsync(zipPath, targetDirectory, ct)` extracts all ZIP entries; `manifest.json` exists at `Path.Combine(targetDirectory, "manifest.json")` after completion.
9. `BundleDirectoryWriterTests` test class (`Tracer.Tests.Unit/Bundle/BundleDirectoryWriterTests.cs`) exists with the following passing test methods:
   - `WriteAsync_CreatesManifestJson` — after write, `manifest.json` exists in the output directory.
   - `WriteAsync_CreatesChecksumsFileWithOneLinePerManifestFile` — `checksums.txt` has the expected line count.
   - `WriteAsync_ChecksumsMatchActualFileHashes` — each hash in `checksums.txt` matches the SHA-256 of the corresponding file.
   - `WriteAsync_CreatesAnnotationsKeep` — `annotations/.keep` exists.
   - `BundleZipWriter_ProducesReadableZip` — produced file opens with `ZipArchive` without exception.
   - `BundleZipWriter_ZipContainsManifestAtRoot` — a `manifest.json` entry exists at the archive root.
   - `BundleReader_Directory_ReturnsMatchingManifest` — read-back manifest equals the one written.
   - `BundleReader_Zip_ReturnsMatchingManifest` — same for a zip path.
   - `BundleExtractor_ExtractsManifestToTargetDirectory` — `manifest.json` exists in the target directory after extraction.
10. All Phase 1–4 integration tests pass.

**Dependencies:** TRC-P4-001

---

## TRC-P4-003 — Bundle Validation

**Design:** [tracer_phase4_design.md §3 — The Bundle Format](./tracer_phase4_design.md#3-the-bundle-format) (§3.6 checksums.txt Format, §3.7 Schema Versioning); [§6 — The CLI: tracer-aggregate.exe](./tracer_phase4_design.md#6-the-cli-tracer-aggregateexe) (§6.3 validate Command)

Implements `BundleValidator`, `ValidationResult`, and `ValidationError` in `Tracer.Bundle.Validation`. The validator runs sequentially: manifest exists and deserializes cleanly; `schemaVersion` is recognized by `BundleSchemaV1`; every file listed in `manifest.Files` exists on disk; each file's `sizeBytes` matches; in strict mode, the SHA-256 of each file matches the manifest entry; `checksums.txt` entries agree with `manifest.Files`; and the auxiliary JSON files (`scenario.json`, `topology.json`, `source_intervals.json`) are well-formed JSON. All discovered errors are collected into `ValidationResult.Errors` rather than short-circuiting on the first failure.

**Success conditions:**

1. `BundleValidator.ValidateAsync(bundleDirectory, manifest, strict: false, ct)` returns `ValidationResult` with `IsValid = true` and `Errors.Count = 0` for a bundle written by `BundleDirectoryWriter`.
2. Deleting a file listed in `manifest.Files` before validation produces `IsValid = false` and a `ValidationError` whose `Message` identifies the missing filename.
3. A manifest with `SchemaVersion = 99` causes a `ValidationError` referencing schema version recognition failure.
4. In non-strict mode, overwriting a file's bytes with different content (preserving size) does not produce a checksum `ValidationError`; `IsValid` remains `true`.
5. In strict mode, the same content change produces a `ValidationError` indicating a SHA-256 mismatch for that file.
6. A file whose actual byte count differs from `manifest.Files[].SizeBytes` produces a size-mismatch `ValidationError` in both strict and non-strict modes.
7. A bundle with two independently missing files returns exactly two `ValidationError` entries (all errors collected, not short-circuited).
8. `BundleValidatorTests` test class (`Tracer.Tests.Unit/Bundle/BundleValidatorTests.cs`) exists with the following passing test methods:
   - `ValidBundle_PassesValidation` — a correctly built bundle is `IsValid`.
   - `MissingFile_FailsWithFileNotFoundError` — deleting a listed file yields `!IsValid` with a relevant message.
   - `UnrecognizedSchemaVersion_FailsValidation` — schema version 99 yields a version error.
   - `CorruptedContent_NonStrictMode_Passes` — size unchanged, content changed, non-strict → `IsValid`.
   - `CorruptedContent_StrictMode_FailsWithChecksumError` — same corruption, strict mode → `!IsValid`.
   - `SizeMismatch_FailsInBothModes` — file truncated → `!IsValid` regardless of strict flag.
   - `MultipleErrors_AllReported` — two missing files → two `ValidationError` entries.
9. All Phase 1–4 integration tests pass.

**Dependencies:** TRC-P4-001, TRC-P4-002

---

## TRC-P4-004 — MultiIntervalReader

**Design:** [tracer_phase4_design.md §4 — The MultiInterval Reader](./tracer_phase4_design.md#4-the-multiinterval-reader)

Implements the `Tracer.Storage.DuckDB.MultiInterval` assembly. `IntervalDbFile` is a value record carrying a file path and alias hint. `AttachedDatabaseManager` attaches and detaches read-only DuckDB files to a single connection: it generates unique SQL-safe aliases (prefix `db_`, normalized hint characters, 6-character random hex suffix), tracks live attachments, and detaches all on `DisposeAsync`. `MultiIntervalReader` is created via `CreateAsync`, opens an in-memory primary DuckDB connection, attaches all supplied `IntervalDbFile` inputs, and provides `BuildEventsUnionSql` which constructs a `UNION ALL` query across all attached databases' `events` tables.

**Success conditions:**

1. `AttachedDatabaseManager.AttachAsync` returns an alias matching the pattern `db_[a-z0-9_]+_[0-9a-f]{6}` (prefix, normalized hint body, 6-hex suffix).
2. Attaching two files with the same alias hint produces two distinct aliases (no collision).
3. `AttachedDatabaseManager.DetachAsync(alias, ct)` removes the alias from `Attachments`; a subsequent SQL `SELECT * FROM {alias}.events` on the same connection throws, confirming the database is detached.
4. `AttachedDatabaseManager.DisposeAsync` detaches all live attachments without throwing, including when called on a manager whose connection is already closed.
5. `MultiIntervalReader.CreateAsync` returns a reader with `Attachments.Count` equal to the number of `IntervalDbFile` inputs provided.
6. `MultiIntervalReader.BuildEventsUnionSql` with two attachments returns a SQL string containing exactly one `"UNION ALL"` substring and two occurrences of `.events`.
7. `MultiIntervalReader.BuildEventsUnionSql` with zero attachments returns the sentinel string `"SELECT NULL WHERE FALSE"`.
8. The SQL produced by `BuildEventsUnionSql` for two attachments each backed by a real DuckDB file with an `events` table executes successfully and returns rows from both databases.
9. `MultiIntervalReader.CreateAsync` completes successfully when provided 100 `IntervalDbFile` inputs, each backed by a distinct DuckDB file written to a temp directory.
10. `MultiIntervalReader.DisposeAsync` disposes the underlying `AttachedDatabaseManager` and connection without throwing.
11. `MultiIntervalReaderTests` test class (`Tracer.Tests.Unit/MultiInterval/MultiIntervalReaderTests.cs`) and `AttachedDatabaseManagerTests` (`Tracer.Tests.Unit/MultiInterval/AttachedDatabaseManagerTests.cs`) exist with all the above behaviors covered by passing test methods.
12. All Phase 1–4 integration tests pass.

**Dependencies:** TRC-P1-005, TRC-P1-006

---

## TRC-P4-005 — Aggregation Core

**Design:** [tracer_phase4_design.md §5 — The TracerAggregator](./tracer_phase4_design.md#5-the-traceraggregator) (§5.1–§5.4)

Implements the `Tracer.Aggregator` assembly's orchestration and discovery layer. `AggregationOrchestrator` is the single public entry point: it resolves the time range (via `SessionResolver` when a session ID is supplied), discovers overlapping intervals via `IntervalDiscovery`, and sequences the nine aggregation steps while reporting each `AggregationStage` to the optional `IAggregationProgressReporter`. `AggregationRequest` and `AggregationResult` are the input/output records. `FastStateScope` (`None`, `SelectedEntities`, `All`) controls fast-state inclusion. `IntervalDiscovery` queries `ITelemetryStorageReader` to find intervals whose `[StartUtc, EndUtc)` overlaps the requested `TimeRange`, filtered by optional node list. `SessionResolver` scans per-interval manifests to derive a `TimeRange` from session-start/session-end markers.

**Success conditions:**

1. `AggregationRequest` requires `OutputPath` and accepts either `TimeRange` or `SessionId` (not both); `FastStateScope` defaults to `FastStateScope.None` when not specified.
2. `AggregationOrchestrator.RunAsync` throws `ArgumentException` when `AggregationRequest` specifies neither `TimeRange` nor `SessionId`.
3. `AggregationOrchestrator.RunAsync` throws `InvalidOperationException` containing the phrase "No intervals found" when `IntervalDiscovery` returns zero intervals for the resolved time range.
4. `IntervalDiscovery.FindOverlappingAsync` includes only intervals where the interval's `[StartUtc, EndUtc)` overlaps the requested `TimeRange`; an interval entirely before or entirely after the range is excluded.
5. `IntervalDiscovery.FindOverlappingAsync` with a non-null `nodeFilter` list returns intervals only for nodes whose IDs appear in that list (case-insensitive comparison).
6. `IntervalDiscovery.FindOverlappingAsync` with `nodeFilter = null` returns intervals for all nodes returned by `ITelemetryStorageReader.ListNodesAsync`.
7. `SessionResolver.ResolveAsync` returns a `TimeRange` spanning the earliest session-start marker to the latest session-end marker found across all interval manifests for the given session ID.
8. `SessionResolver.ResolveAsync` returns `null` when no interval manifest contains the requested session ID.
9. `SessionResolver.ResolveAsync` uses `DateTimeOffset.UtcNow` as the end of the returned `TimeRange` when a session-start marker is present but no session-end marker exists.
10. `IAggregationProgressReporter.Report` is called with `AggregationStage.Started` as the first call and `AggregationStage.Completed` as the last call during a successful full aggregation run against minimal fake data.
11. `IntervalDiscoveryTests` (`Tracer.Tests.Unit/Aggregator/IntervalDiscoveryTests.cs`) and `SessionResolverTests` (`Tracer.Tests.Unit/Aggregator/SessionResolverTests.cs`) exist with all the above behaviors covered by passing test methods.
12. All Phase 1–4 integration tests pass.

**Dependencies:** TRC-P4-001, TRC-P4-002, TRC-P4-004, TRC-P2-008

---

## TRC-P4-006 — Aggregation Consolidators

**Design:** [tracer_phase4_design.md §5 — The TracerAggregator](./tracer_phase4_design.md#5-the-traceraggregator) (§5.5–§5.7)

Implements the data-processing layer called by `AggregationOrchestrator`. `EventsConsolidator` attaches per-node `events.duckdb` source files in sequence, inserts rows within the time range into a fresh output DB, builds Phase 1 indexes, and calls `CHECKPOINT` to flush the WAL. `SlowStateConsolidator` applies the same pattern for `slow_state.duckdb`. `FastStateCopier` copies per-entity Parquet samples filtered by `FastStateScope`, merging across source intervals via DuckDB's `read_parquet` + atomic file replace. `ScenarioMetadataCollector` queries the consolidated events DB to produce `scenario.json`. `TopologyExtractor` derives `topology.json` from extracted interval metadata. `ManifestBuilder` computes SHA-256 checksums for all output files and assembles the final `BundleManifest`. `StagingDirectory` owns a temp workspace and deletes it on `DisposeAsync`. `IAggregationProgressReporter` and `AggregationStage` provide the progress contract.

**Success conditions:**

1. `EventsConsolidator.ConsolidateAsync` with two source intervals each containing N events within the time range produces an output `events.duckdb` with exactly 2N rows; events outside the time range are absent.
2. The output `events.duckdb` from `EventsConsolidator` has the indexes defined in Phase 1 §4.2 (confirmed by querying the DuckDB information schema or `PRAGMA table_info`).
3. `EventsConsolidator` issues `CHECKPOINT` before returning, so no WAL file exists alongside the output `.duckdb` file.
4. `FastStateCopier` with `FastStateScope.None` produces no `fast_state/` directory in the staging path.
5. `FastStateCopier` with `FastStateScope.All` copies rows for every distinct `instance_key` found across all source Parquet files.
6. `FastStateCopier` with `FastStateScope.SelectedEntities` copies rows only for entity IDs present in the `entityFilter` list; other entities are absent from the output.
7. `FastStateCopier` correctly merges samples from two source intervals for the same entity into a single `samples.parquet` containing rows from both intervals (verified by row count equality with the sum of per-source counts within the time range).
8. `ManifestBuilder.BuildAsync` produces a `BundleManifest` whose `Files[].Sha256` values each match the actual SHA-256 of the corresponding file at build time.
9. `StagingDirectory.DisposeAsync` deletes the staging temp directory; `Directory.Exists` returns `false` immediately after disposal.
10. `EventsConsolidatorTests` (`Tracer.Tests.Unit/Aggregator/EventsConsolidatorTests.cs`), `FastStateCopierTests` (`Tracer.Tests.Unit/Aggregator/FastStateCopierTests.cs`), and `TopologyExtractorTests` (`Tracer.Tests.Unit/Aggregator/TopologyExtractorTests.cs`) exist with all the above behaviors covered by passing test methods.
11. All Phase 1–4 integration tests pass.

**Dependencies:** TRC-P4-001, TRC-P4-002, TRC-P4-005

---

## TRC-P4-007 — `tracer-aggregate.exe` CLI

**Design:** [tracer_phase4_design.md §6 — The CLI: tracer-aggregate.exe](./tracer_phase4_design.md#6-the-cli-tracer-aggregateexe)

Implements the `Tracer.Aggregator.Cli` assembly using `System.CommandLine`. `Program.cs` wires a root command with global `--nas-root` and `--log-level` options and three subcommands. `BuildCommand` parses `--session-id`/`--time-range`, `--output`, `--nodes`, `--fast-state`, `--fast-state-entities`, `--label`, and `--force`, then calls `AggregationOrchestrator`. `ValidateCommand` parses a bundle path and `--strict`, calls `BundleValidator`, and exits with code 0 on success or 1 on failure. `InspectCommand` reads the manifest via `BundleReader` and prints a human-readable summary to stdout. `CliConsoleLogger` writes `LOG_FILE=<path>` to stdout on startup and routes all progress lines to stderr.

**Success conditions:**

1. `tracer-aggregate build --nas-root <path> --session-id <id> --output <outPath>` (invoked via `Program.Main`) exits with code 0 and produces a bundle directory at `<outPath>` containing `manifest.json`, `events.duckdb`, `slow_state.duckdb`, and `checksums.txt` when the mock NAS contains at least one interval for that session.
2. `tracer-aggregate build` with neither `--session-id` nor `--time-range` exits with a non-zero code without producing any bundle file.
3. `tracer-aggregate build --output <existingPath>` without `--force` exits with a non-zero code; the same command with `--force` exits with code 0 and overwrites the output.
4. `tracer-aggregate validate <bundlePath>` exits with code 0 for a bundle produced in condition 1.
5. `tracer-aggregate validate <bundlePath>` with a manually corrupted `manifest.json` exits with code 1 and writes a validation error description to stderr.
6. `tracer-aggregate validate <bundlePath> --strict` exits with code 1 when a file's content has been altered but its size left unchanged (strict SHA-256 check fails).
7. `tracer-aggregate inspect <bundlePath>` exits with code 0 and writes lines containing the bundle ID, time range, event count, and participating node list to stdout.
8. On any successful `build` invocation, the first line written to stdout is `LOG_FILE=<absolute-path>` and all subsequent progress output goes to stderr (verified by capturing stdout and stderr separately in tests).
9. `AggregatorEndToEndTests` integration test class (`Tracer.Tests.Integration/AggregatorEndToEndTests.cs`) exists with the following passing test methods:
   - `BuildCommand_ProducesValidBundle` — `Program.Main` called with `build` args; exit code is 0 and `BundleValidator.ValidateAsync` on the output returns `IsValid`.
   - `ValidateCommand_ValidBundle_ExitsZero` — `Program.Main` called with `validate` args against the bundle from the prior step; exit code is 0.
   - `ValidateCommand_CorruptedManifest_ExitsOne` — manifest JSON replaced with invalid bytes; exit code is 1.
   - `InspectCommand_OutputContainsBundleId` — stdout captured from `inspect`; contains the ULID-format bundle ID string.
10. All Phase 1–4 integration tests pass.

**Dependencies:** TRC-P4-003, TRC-P4-005, TRC-P4-006

---

## TRC-P4-008 — OfflineViewer

**Design:** [tracer_phase4_design.md §8 — The Offline Viewer](./tracer_phase4_design.md#8-the-offline-viewer)

Implements the `Tracer.OfflineViewer` assembly: a standalone `tracer-viewer.exe` that hosts the same Vue SPA and Web API surface against a bundle file rather than a live observer. `OfflineViewerHostBuilder` wires Kestrel (localhost-only), `BundleOpenManager`, the Phase 3 query services (`SessionQueryService`, `ScenarioQueryService`, `TopologyQueryService`, `EventLookupService`), and an `InertObserverStateReporter`. `OfflineHostedService` opens any bundle path supplied on the command line; `BrowserLauncher` then opens the default browser at the configured port. Three new endpoints — `POST /api/bundle/open`, `POST /api/bundle/close`, `GET /api/bundle/current` — drive the Vue SPA's `useBundleMode` composable and `BundleOpenView`.

**Success conditions:**
1. `OfflineViewerHostBuilder.Build(null)` returns a `WebApplication` that starts without exception and binds to localhost only; the server addresses collection contains no non-loopback address.
2. `BundleOpenManager.OpenAsync` with a valid bundle directory path reads `manifest.json`, validates the bundle (non-strict), and calls `ReadOnlyConnectionPool.InitializeAsync` against `events.duckdb`; `BundleOpenManager.Current` is non-null and `Current.Manifest.BundleId` is non-empty afterwards.
3. `BundleOpenManager.OpenAsync` with a `.tracerbundle.zip` path extracts to a temp directory, opens the extracted bundle successfully, and deletes the temp directory on `BundleOpenManager.CloseAsync`.
4. `BundleOpenManager.OpenAsync` with a bundle whose `manifest.json` is malformed throws `InvalidOperationException`; `Current` remains null after the failed open.
5. `POST /api/bundle/open` with a valid bundle path returns `200 OK` containing the expected `bundleId`; a subsequent `GET /api/bundle/current` returns the same `bundleId`.
6. `POST /api/bundle/close` returns `204 No Content`; a subsequent `GET /api/bundle/current` body deserializes to `null`.
7. `GET /api/sessions` issued to a running offline viewer returns the same session list as the live Observer that produced the bundle (verified by `BundleRoundTripTests`).
8. `InertObserverStateReporter` is registered as the `ObserverStateReporter` implementation in the offline viewer's DI container and returns a static bundle-mode state for all properties without throwing.
9. `OfflineViewerSmokeTests` integration test class (`Tracer.Tests.Integration/OfflineViewerSmokeTests.cs`) exists with the following passing test methods:
   - `OfflineViewer_StartsAndServesBundle` — spawns `tracer-viewer.exe` with a pre-built bundle path argument; polls `GET /api/bundle/current` until the expected bundle ID is returned within 10 seconds; asserts `GET /api/sessions` returns a non-empty list.
   - `OfflineViewer_ExitsCleanlyOnSigint` — sends cancellation to the hosted service; process exits with code 0 within 5 seconds.
10. `useBundleMode.spec.ts` Vitest test file (`tracer-viewer/tests/unit/useBundleMode.spec.ts`) exists with passing tests for `live`, `bundle`, and `no-bundle` mode detection per §10.3.
11. All Phase 1–4 integration tests pass.

**Dependencies:** TRC-P4-003, TRC-P4-004

---

## TRC-P4-009 — Web API Bundle Mode

**Design:** [tracer_phase4_design.md §7 — Web API Additions](./tracer_phase4_design.md#7-web-api-additions)

Adds bundle build and management endpoints to the Observer's Web API. `BundleEndpoints` maps six routes (`POST /api/bundles/build`, `GET /api/bundles`, `GET /api/bundles/{bundleId}`, `GET /api/bundles/{bundleId}/status`, `GET /api/bundles/{bundleId}/download`, `DELETE /api/bundles/{bundleId}`) to `BundleBuildService` and `BundleCatalog`. `BundleBuildService` accepts build requests, runs `AggregationOrchestrator` in a background task with at most one concurrent build enforced by a `SemaphoreSlim`, and tracks build status by bundle ID. `ObserverHostBuilder` is extended with `BundleCatalog`, `ITelemetryStorageReader` (backed by `LocalFileSystemStorageReader`), `AggregationOrchestrator`, and `BundleBuildService` singletons; `ObserverConfig` gains `BundlesRoot` and `NasMockRoot` fields.

**Success conditions:**
1. `POST /api/bundles/build` with a valid `BundleBuildRequestDto` containing `sessionId` returns `202 Accepted` with a `BundleBuildAcceptedDto` whose `bundleId` is a non-empty 26-character ULID string.
2. `POST /api/bundles/build` with neither `sessionId` nor `timeRange` populated returns `400 Bad Request`.
3. `GET /api/bundles/{id}/status` for the bundle ID from condition 1 transitions through `Queued` → `InProgress` → `Completed`; `CompletedAtUtc` is non-null and `OutputPath` points to an existing file or directory on completion.
4. Two simultaneous `POST /api/bundles/build` requests result in at most one build running at a time; the second remains in `Queued` state while the first is `InProgress` (`BundleEndpointTests.TwoConcurrentBuilds_OnlyOneRunsAtATime`).
5. `GET /api/bundles/{id}/download` for a completed build returns `200 OK` with `Content-Type: application/zip` and a non-empty body that is a valid ZIP containing at least `manifest.json` and `events.duckdb`.
6. `GET /api/bundles/{id}/download` for an unknown bundle ID returns `404 Not Found`.
7. `DELETE /api/bundles/{id}` for a completed build returns `204 No Content`; subsequent `GET /api/bundles/{id}` returns `404 Not Found`; the bundle directory or zip file no longer exists on disk.
8. `GET /api/bundles` returns `200 OK` with a list including all registered bundles; each entry carries `bundleId`, `createdAtUtc`, `timeRange`, and `sizeBytes`.
9. `BundleEndpointTests` test class (`Tracer.Tests.Unit/WebApi/BundleEndpointTests.cs`) exists with passing test methods covering conditions 1–8 above using an in-memory `BundleBuildService` backed by a mock `AggregationOrchestrator`.
10. `ObserverBundleBuildTests` integration test class (`Tracer.Tests.Integration/ObserverBundleBuildTests.cs`) exists with the following passing test methods:
    - `PostBundleBuild_ReturnsAcceptedWithBundleId` — full Observer+FakeNode stack; POST returns 202 with a valid bundle ID.
    - `GetStatus_ProgressesToCompleted` — polls status until `Completed`; asserts bundle file exists at `OutputPath`.
    - `GetDownload_ReturnsValidZip` — downloads bundle; asserts ZIP contains `manifest.json`.
    - `DeleteBundle_RemovesFromDisk` — deletes; asserts GET returns 404 and file is gone.
11. All Phase 1–4 integration tests pass.

**Dependencies:** TRC-P4-005, TRC-P4-006, TRC-P4-007

---

## TRC-P4-010 — Self-Contained Distribution

**Design:** [tracer_phase4_design.md §9 — Self-Contained Distribution](./tracer_phase4_design.md#9-self-contained-distribution)

Configures `Tracer.OfflineViewer.csproj` for self-contained `win-x64` single-file publishing and provides `build-viewer-distribution.ps1` at repo root. The script builds the Vue SPA with `pnpm run build`, publishes the .NET project as a self-contained single-file executable, copies the Vue `dist/` output into `wwwroot/`, generates `README.txt`, and zips the result into a portable `TracerViewer.zip`. The distribution folder requires no .NET installation on the target machine and contains `tracer-viewer.exe`, the DuckDB native library, `wwwroot/`, and `README.txt`.

**Success conditions:**
1. `Tracer.OfflineViewer.csproj` contains `<RuntimeIdentifier>win-x64</RuntimeIdentifier>`, `<SelfContained>true</SelfContained>`, `<PublishSingleFile>true</PublishSingleFile>`, `<PublishTrimmed>false</PublishTrimmed>`, and `<InvariantGlobalization>true</InvariantGlobalization>` exactly as specified in §9.2.
2. `dotnet publish src/Tracer.OfflineViewer -c Release -r win-x64 --self-contained -p:PublishSingleFile=true` completes with exit code 0 and produces `tracer-viewer.exe` in the output directory.
3. `build-viewer-distribution.ps1` executes with exit code 0 and produces a `dist/TracerViewer/` folder containing at minimum `tracer-viewer.exe`, `wwwroot/index.html`, and `README.txt`.
4. `build-viewer-distribution.ps1` also produces `dist/TracerViewer.zip`; the ZIP extracts to a folder whose layout matches condition 3.
5. `build-viewer-distribution.ps1` exits with a non-zero code and prints a descriptive error when any file in the `$expected` check list is absent from the output folder.
6. `dist/TracerViewer/README.txt` contains the phrases "Double-click tracer-viewer.exe" and "No installation required" as per §9.4.
7. Running `tracer-viewer.exe` from the distribution output folder (with `wwwroot/` present) starts successfully; `GET http://localhost:<port>/api/bundle/current` returns `200 OK` within 10 seconds; the root URL serves `index.html`.
8. `DistributionSmokeTests` integration test class (`Tracer.Tests.Integration/DistributionSmokeTests.cs`) or an equivalent section of `OfflineViewerSmokeTests` verifies condition 7 by launching the published executable and issuing HTTP probes.
9. All Phase 1–4 integration tests pass.

**Dependencies:** TRC-P4-008

---

## TRC-P4-011 — TestHarness Phase 4 Additions

**Design:** [tracer_phase4_design.md §10.2 — Backend Integration Tests](./tracer_phase4_design.md#102-backend-integration-tests)

Extends `Tracer.TestHarness` with three fixtures shared across Phase 4 integration tests. `AggregationFixture` creates a temp mock-NAS root, populates it via a `FakeNodeFixture` run, and exposes an `AggregationOrchestrator` ready to build bundles against that data. `BundleFixture` wraps `AggregationFixture` to produce a fully validated bundle directory and exposes its path and manifest. `RoundTripAssertions` provides named helper methods that compare live Observer query results against the same queries issued to an OfflineViewer instance, covering session lists and notables, with descriptive failure messages when results diverge.

**Success conditions:**
1. `AggregationFixture` creates a temp NAS root, runs a `FakeNodeFixture` session to populate it with at least one interval, and exposes `OrchestratorForNas` as a ready `AggregationOrchestrator`; the NAS root is deleted on `DisposeAsync`.
2. `AggregationFixture.RunDefaultBuildAsync(outputPath, ct)` calls `AggregationOrchestrator.RunAsync` with the fixture session's ID and returns an `AggregationResult` with `TotalEvents > 0`; the bundle exists at `outputPath` on return.
3. `BundleFixture` calls `AggregationFixture.RunDefaultBuildAsync` in `InitializeAsync` and exposes the resulting `BundlePath` (a valid `.tracerbundle` directory) and `Manifest` with a non-null `BundleId`.
4. `BundleFixture` disposes its inner `AggregationFixture`; after `DisposeAsync` the bundle directory no longer exists on disk.
5. `RoundTripAssertions.AssertSessionListsMatchAsync(liveClient, bundleClient)` fetches `GET /api/sessions` from both clients and asserts that session IDs and event counts are equal; it throws `XunitException` with a descriptive message if they differ.
6. `RoundTripAssertions.AssertNotablesMatchAsync(liveClient, bundleClient, sessionId)` fetches `GET /api/scenario/notables?sessionId={id}` from both clients and asserts that notable count, IDs, severities, and publish timestamps are equal.
7. `TestHarnessPhase4Tests` test class (`Tracer.Tests.Unit/TestHarness/TestHarnessPhase4Tests.cs`) exists with the following passing test methods:
   - `BundleFixture_ProducesValidBundle` — creates `BundleFixture`; `BundleValidator.ValidateAsync` returns `IsValid = true`.
   - `BundleFixture_CleansUpOnDispose` — disposes `BundleFixture`; asserts `Directory.Exists(fixture.BundlePath)` is `false`.
   - `AggregationFixture_RunsAndProducesBundle` — creates `AggregationFixture`, calls `RunDefaultBuildAsync`; asserts `TotalEvents > 0` and output path exists.
8. All Phase 1–4 integration tests pass.

**Dependencies:** TRC-P4-005, TRC-P4-006, TRC-P4-007

---

## TRC-P4-012 — Bundle & Aggregator Unit Tests

**Design:** [tracer_phase4_design.md §10.1 — Backend Unit Tests](./tracer_phase4_design.md#101-backend-unit-tests)

Implements the Phase 4 backend unit test suite covering the bundle format, validation, multi-interval reader, and aggregator internals. Each test class exercises its target type in isolation using in-memory DuckDB connections or temp files; no external processes or large data files are required. Test class names and their behavioral coverage are specified in §10.1.

**Success conditions:**
1. `BundleManifestTests` (`Tracer.Tests.Unit/Bundle/BundleManifestTests.cs`) exists with passing test methods:
   - `RoundTrip_SerializeDeserialize_Equals` — a `BundleManifest` survives JSON round-trip and compares equal.
   - `Deserialize_UnknownFields_Ignored` — manifest JSON with an extra unknown field deserializes without error.
   - `Deserialize_MissingRequiredField_Throws` — manifest JSON missing `bundleId` throws a descriptive error.
   - `BundleId_IsValidUlid` — a freshly constructed manifest's `BundleId` is a 26-character string matching the ULID alphabet.
2. `BundleDirectoryWriterTests` (`Tracer.Tests.Unit/Bundle/BundleDirectoryWriterTests.cs`) exists with passing test methods:
   - `Write_ProducesExpectedLayout` — a minimal bundle written to a temp directory contains `manifest.json`, `scenario.json`, `topology.json`, `source_intervals.json`, and `checksums.txt`.
   - `Checksums_MatchManifestFiles` — every SHA-256 in `checksums.txt` matches the corresponding `manifest.files[].sha256` entry.
   - `Dispose_BeforeFinalize_CleansUpDirectory` — disposing the writer before `FinalizeAsync` removes the staging directory.
3. `BundleValidatorTests` (`Tracer.Tests.Unit/Bundle/BundleValidatorTests.cs`) exists with passing test methods:
   - `ValidBundle_ReturnsIsValid` — a well-formed bundle returns `IsValid = true` with no errors.
   - `WrongFileSize_ReturnsError` — a file whose on-disk size differs from `manifest.files[].sizeBytes` triggers a validation error containing the file path.
   - `WrongChecksum_StrictMode_ReturnsError` — a file whose content is altered (size unchanged) returns an error when `strict: true`.
   - `MissingFile_ReturnsError` — a file listed in the manifest but absent from the directory returns an error.
   - `UnknownSchemaVersion_ReturnsError` — a manifest with `schemaVersion: 99` returns an error.
4. `MultiIntervalReaderTests` (`Tracer.Tests.Unit/MultiInterval/MultiIntervalReaderTests.cs`) exists with passing test methods:
   - `CreateWithZeroFiles_BuildEventsUnionSql_ReturnsEmptySentinel` — `BuildEventsUnionSql` with zero attachments returns the sentinel empty-query string.
   - `CreateWithOneFile_SqlReferencesAlias` — SQL from `BuildEventsUnionSql` references the single attached alias.
   - `CreateWithNFiles_AllAliasesPresent` — SQL references all N aliases for N ≥ 2.
   - `SourceAliasColumn_PresentInResults` — a query against a real in-memory test DuckDB includes `__source_alias` in the result set.
   - `Dispose_DetachesAllDatabases` — after `DisposeAsync`, the `Attachments` dictionary is empty.
5. `AttachedDatabaseManagerTests` (`Tracer.Tests.Unit/MultiInterval/AttachedDatabaseManagerTests.cs`) exists with passing test methods:
   - `AttachSamePath_Twice_Throws` — second attach with the same alias hint on the same connection throws `InvalidOperationException`.
   - `Detach_RemovesFromAttachments` — `DetachAsync` removes the alias from `Attachments`.
   - `Dispose_DetachesAll` — after `DisposeAsync`, `Attachments` is empty.
   - `AliasGeneration_NeverCollides` — attaching 20 files with the same hint produces 20 distinct aliases.
   - `AliasGeneration_ProducesValidSqlIdentifier` — alias generated for a hint containing colons and slashes matches `[a-zA-Z_][a-zA-Z0-9_]*`.
6. `IntervalDiscoveryTests` (`Tracer.Tests.Unit/Aggregator/IntervalDiscoveryTests.cs`) exists with passing test methods:
   - `FindOverlapping_NoFilter_ReturnsAllMatchingIntervals`.
   - `FindOverlapping_WithNodeFilter_ReturnsOnlySpecifiedNodes`.
   - `FindOverlapping_EmptyResult_WhenNoOverlap`.
   - `BoundaryCase_IntervalStartEqualsRangeEnd_Excluded`.
   - `BoundaryCase_IntervalEndEqualsRangeStart_Excluded`.
7. `SessionResolverTests` (`Tracer.Tests.Unit/Aggregator/SessionResolverTests.cs`) exists with passing test methods:
   - `Resolve_SessionWithStartAndEnd_ReturnsCorrectRange`.
   - `Resolve_SessionWithOnlyStart_UsesNowAsEnd`.
   - `Resolve_NonExistentSession_ReturnsNull`.
   - `Resolve_MultipleIntervalsWithMarkers_EarliestStartLatestEnd`.
8. `EventsConsolidatorTests` (`Tracer.Tests.Unit/Aggregator/EventsConsolidatorTests.cs`), `FastStateCopierTests` (`Tracer.Tests.Unit/Aggregator/FastStateCopierTests.cs`), and `TopologyExtractorTests` (`Tracer.Tests.Unit/Aggregator/TopologyExtractorTests.cs`) exist with all behaviors from §10.1 covered by passing test methods.
9. `BundleEndpointTests` (`Tracer.Tests.Unit/WebApi/BundleEndpointTests.cs`) exists with passing test methods covering the behaviors listed in §10.1 under "WebApi/BundleEndpointTests.cs".
10. All Phase 1–4 integration tests pass.

**Dependencies:** TRC-P4-001, TRC-P4-002, TRC-P4-003, TRC-P4-004, TRC-P4-005, TRC-P4-006, TRC-P4-009

---

## TRC-P4-013 — Bundle Round-Trip Integration Tests

**Design:** [tracer_phase4_design.md §10.2 — Backend Integration Tests](./tracer_phase4_design.md#102-backend-integration-tests); [§1.3 — Success Criteria](./tracer_phase4_design.md#13-success-criteria)

Implements the full Phase 4 integration test suite. `AggregatorEndToEndTests` exercises `AggregationOrchestrator` directly against a mock NAS. `BundleRoundTripTests` captures a live FakeNode+Observer session, builds a bundle via `POST /api/bundles/build`, opens it in a separate OfflineViewer process, and asserts that query results are bitwise identical between live and bundle mode using `RoundTripAssertions`. Together these tests enforce success criteria §1.3 items 3, 4, 5, and 8.

**Success conditions:**
1. `AggregatorEndToEndTests` (`Tracer.Tests.Integration/AggregatorEndToEndTests.cs`) exists with the following passing test methods:
   - `BuildCommand_ProducesValidBundle` — `AggregationOrchestrator.RunAsync` against a 2-node × 3-interval mock NAS; `BundleValidator.ValidateAsync` returns `IsValid = true` with `strict: true`.
   - `Build_SessionIdVariant_UsesCorrectTimeRange` — aggregator resolves `sessionId` from session-start/end markers; bundle `timeRange` matches those markers exactly.
   - `Build_EventCount_MatchesSumOfSources` — bundle `events.duckdb` row count equals the sum of source rows within the time range across all nodes.
   - `Build_ProgressEvents_InOrder` — progress reporter receives stages in the order `Started` … `Completed` with no intervening `Failed` stage.
2. `BundleRoundTripTests` (`Tracer.Tests.Integration/BundleRoundTripTests.cs`) exists with the following passing test methods:
   - `RoundTrip_SessionList_IsIdentical` — runs FakeNode+Observer fixture; captures `GET /api/sessions`; builds bundle via `POST /api/bundles/build` and waits for `Completed`; opens bundle in OfflineViewer; asserts `RoundTripAssertions.AssertSessionListsMatchAsync` passes.
   - `RoundTrip_Notables_AreIdentical` — same fixture; asserts `RoundTripAssertions.AssertNotablesMatchAsync` passes for the session's notables.
   - `RoundTrip_CrossIntervalQuery_ReturnsAllEvents` — bundle spans 2+ intervals; event count returned by `GET /api/sessions` against the viewer matches the live observer total.
3. `ObserverBundleBuildTests` (`Tracer.Tests.Integration/ObserverBundleBuildTests.cs`) exists with passing test methods:
   - `PostBundleBuild_ReturnsAcceptedWithBundleId`.
   - `GetStatus_ProgressesToCompleted`.
   - `GetDownload_ReturnsValidZip`.
   - `DeleteBundle_RemovesFromDisk`.
4. `BundleRoundTripTests.RoundTrip_SessionList_IsIdentical` and `RoundTrip_Notables_AreIdentical` each complete within 60 seconds, enforced by `[Timeout(60_000)]` or the xUnit test collection timeout.
5. All Phase 1–4 integration tests pass.

**Dependencies:** TRC-P4-008, TRC-P4-009, TRC-P4-010, TRC-P4-011

<!-- PHASE 4 TASKS END -->

<!-- PHASE 5 TASKS BEGIN -->

## TRC-P5-001 — LiveMultiIntervalReader & IntervalSetTracker

**Design:** [tracer_phase5_design.md §3](./tracer_phase5_design.md#3-live-multi-interval-querying)
**Architecture:** [tracer_architecture_v1.md §17](./tracer_architecture_v1.md#17-performance-targets) *(query latency targets require pool pre-warming)*

`IntervalSetTracker` maintains the authoritative set of intervals eligible for live querying — the active interval plus the N most-recent completed ones — and notifies subscribers on rotation and retention-eviction events. `LiveMultiIntervalReader` extends Phase 4's multi-interval pool to be reactive: it subscribes to `IntervalSetTracker.SetChanged` and rebuilds its pre-attached connection pool whenever the set changes. Both types are wired into `ObserverHostedService` so the multi-interval reader is ready before the first HTTP request arrives. All Phase 3 query services (`SessionQueryService`, `ScenarioQueryService`, `TopologyQueryService`, `EventLookupService`) are migrated to use `LiveMultiIntervalReader`; `ReadOnlyConnectionPool` is removed from the Observer's DI as specified in §3.5.

**Success conditions:**

1. `IntervalSetTrackerTests` (`Tracer.Tests.Unit/MultiInterval/IntervalSetTrackerTests.cs`) exists with the following passing test methods:
   - `InitializeAsync_NoCompletedIntervals_SnapshotContainsOnlyActive` — snapshot after `InitializeAsync` with zero completed intervals has exactly one entry with `Role == IntervalRole.Active`.
   - `InitializeAsync_FiveCompleted_CapThree_SnapshotContainsThreeNewestPlusActive` — with cap=3 and 5 completed intervals, `CurrentSnapshot().Intervals` has 4 entries (3 completed + 1 active) with the 2 oldest excluded.
   - `OnIntervalRotatedAsync_PreviousActiveBecomesCompleted` — after rotation, the previously-active interval appears in the snapshot with `Role == IntervalRole.Completed` and a new active interval is present.
   - `OnIntervalEvictedAsync_RemovesEvictedIntervalFromSnapshot` — `CurrentSnapshot()` after eviction does not contain the evicted directory's timestamp.
   - `SetChanged_FiredAfterInitialize` — `SetChanged` event fires exactly once during `InitializeAsync`.
   - `SetChanged_FiredAfterRotation` — `SetChanged` fires on `OnIntervalRotatedAsync`.
   - `SetChanged_NotFiredIfEvictionTargetNotInSet` — evicting an interval not currently tracked does not fire `SetChanged`.
2. `LiveMultiIntervalReaderTests` (`Tracer.Tests.Unit/MultiInterval/LiveMultiIntervalReaderTests.cs`) exists with the following passing test methods:
   - `InitializeAsync_BuildsPoolSizedConnections` — after `InitializeAsync`, `_poolSize` connections are available.
   - `AcquireAsync_ReturnsConnection_WithCurrentIntervalsAttached` — acquired connection's `Intervals` list matches the tracker's current snapshot.
   - `AfterRotation_NewConnectionsHaveNewSet` — after the tracker fires `SetChanged` with a new active interval, the next acquired connection's `Intervals` includes the new active entry.
   - `ConnectionFromOldPool_DisposesRatherThanReturns` — a connection issued before a rebuild is disposed on `DisposeAsync()`, verified by confirming the pool's available count is unchanged.
   - `ConcurrentAcquireAndRebuild_NoCrashOrHandleLeak` — 8 concurrent acquires during a simulated `OnSetChangedAsync` complete without throwing and with no dangling open handles.
3. `ObserverHostedService` calls `_tracker.InitializeAsync` then `_multiReader.InitializeAsync` before entering the rotation loop; verified by an integration test `LiveMultiIntervalQueryTests.Observer_StartsWithMultiReaderReady` that issues a query immediately after the hosted service signals readiness.
4. `ReadOnlyConnectionPool` has zero DI registrations in `ObserverHostBuilder`; all four Phase 3 query services resolve `LiveMultiIntervalReader` as their dependency; verified by `ObserverDiTests.QueryServices_UseLiveMultiIntervalReader_NotSinglePool`.
5. `LiveMultiIntervalQueryTests` (`Tracer.Tests.Integration/LiveMultiIntervalQueryTests.cs`) exists with the following passing test methods:
   - `Query_SpanningThreeIntervals_ReturnsEventsFromAll` — events injected into 3 sequential intervals via a mock writer; `GET /api/events?sessionId=X` for the full session range returns events from all 3 with correct ordering.
   - `Query_AfterRotation_IncludesNewInterval` — push events, rotate, push more; query returns events from both intervals.
   - `Query_AfterEviction_ExcludesEvictedInterval` — configure cap=1; push events into 3 intervals; after the oldest is evicted, query returns only events from the 2 most recent plus active.
6. Retention manager calls `_tracker.OnIntervalEvictedAsync` before deleting the directory; the fixed 30-second delay before deletion (§3.3) is present; verified by `RetentionCoordinationTests.Retention_WaitsBeforeDeletion`.
7. All Phase 1–5 integration tests pass.

**Dependencies:** TRC-P4-005, TRC-P4-006, TRC-P3-006

---

## TRC-P5-002 — `/api/events` List & Aggregate Endpoints

**Design:** [tracer_phase5_design.md §4](./tracer_phase5_design.md#4-the-event-query-api)
**Architecture:** [tracer_architecture_v1.md §17](./tracer_architecture_v1.md#17-performance-targets) *(list/aggregate < 300 ms p95; 100M-event aggregate < 1 s)*

`EventQueryService` implements the list query with full filter composition and a two-pass count+rows strategy using `LiveMultiIntervalReader`'s UNION ALL SQL. `EventAggregationService` implements DuckDB `time_bucket` aggregation with the eight supported bucket durations and four group-by modes. `EventEndpoints` is extended with `GET /api/events` (list) and `GET /api/events/aggregate` following the contracts in §4.1–§4.3. The three DTO types (`EventListDto`, `EventAggregateBucketDto`, `EventFilterDto`) and their JSON-source-generated serializers are added under `Contracts/Dto/`. Filter composition follows the AND-across-chips, OR-within-chip rule from §4.4, enforced in `QueryPredicateBuilder`.

**Success conditions:**

1. `EventQueryServiceTests` (`Tracer.Tests.Unit/WebApi/EventQueryServiceTests.cs`) exists with the following passing test methods:
   - `ListAsync_NoFilter_ReturnsAllEventsInTimeOrder` — an in-memory fixture with 10 events returns all 10 in ascending `publish_wallclock` order.
   - `ListAsync_TimeRange_ExcludesEventsOutsideRange` — events strictly outside `[from, to)` are absent; boundary events follow inclusive/exclusive semantics.
   - `ListAsync_TopicFilter_ReturnsOnlyMatchingTopics` — single-topic filter returns only that topic's events.
   - `ListAsync_MultiTopicFilter_OrsWithinFilter` — two topics ORd; events on either topic present; events on a third topic absent.
   - `ListAsync_MultipleFilterTypes_AndsAcrossFilters` — `topic=A` plus `severity=error` returns only events matching both; events with topic A but not error severity absent.
   - `ListAsync_TraceIdFilter_ReturnsOnlyThatTrace` — only events with the specified `TraceId` returned.
   - `ListAsync_Limit_TruncatesAndSetsTruncatedFlag` — result with `limit=3` over 10 events: `Returned=3`, `TotalMatching=10`, `Truncated=true`.
   - `ListAsync_OrderDescending_ReturnsByNewestFirst` — verify first event in result has the latest `PublishWallclock`.
   - `ListAsync_EmptyResult_ReturnsTotalMatchingZero` — filter matching nothing: `TotalMatching=0`, `Events` empty, `Truncated=false`.
   - `ListAsync_NotablesOnly_ExcludesNonNotables` — events without a `NotableLabel` excluded.
2. `EventAggregationServiceTests` (`Tracer.Tests.Unit/WebApi/EventAggregationServiceTests.cs`) exists with the following passing test methods:
   - `AggregateAsync_OneHourAt5sBuckets_ReturnsExpectedBucketCount` — 1-hour range with `bucketDuration=5s` produces at most 720 buckets.
   - `AggregateAsync_EmptyRange_ReturnsEmptyBuckets`.
   - `AggregateAsync_GroupByNone_EachBucketHasSingleGroupWithNullKey`.
   - `AggregateAsync_GroupByNode_GroupsArePublisherNodes`.
   - `AggregateAsync_FilterAppliedBeforeAggregation_ExcludesNonMatchingEvents`.
   - `AggregateAsync_BucketTotalsEqualSumOfGroupCounts` — for every bucket, `total == groups.Sum(g => g.Count)`.
   - `AggregateAsync_InvalidBucketDuration_ThrowsArgumentException`.
3. `EventEndpointsListTests` (`Tracer.Tests.Unit/WebApi/EventEndpointsListTests.cs`) exists with the following passing test methods:
   - `HandleListAsync_NoFilter_Returns200WithEventList`.
   - `HandleListAsync_LimitZero_Returns400ProblemDetails`.
   - `HandleListAsync_LimitOverMax_Returns400ProblemDetails`.
   - `HandleListAsync_UnknownSessionId_Returns404ProblemDetails`.
   - `HandleListAsync_MultipleTopicParams_PassedAsListToService`.
4. `EventEndpointsAggregateTests` (`Tracer.Tests.Unit/WebApi/EventEndpointsAggregateTests.cs`) exists with the following passing test methods:
   - `HandleAggregateAsync_ValidRequest_Returns200WithAggregateDto`.
   - `HandleAggregateAsync_InvalidBucketDuration_Returns400ProblemDetails`.
   - `HandleAggregateAsync_MissingSessionId_Returns400`.
5. `GET /api/events` and `GET /api/events/aggregate` appear in the OpenAPI document; the TypeScript client (`tracerApiClient.ts`) regenerates with `listEvents` and `aggregateEvents` methods.
6. `EventListDto`, `EventAggregateBucketDto`, and `EventFilterDto` serializers are source-generated (`[JsonSerializable]`); reflection-based serialization of these types is absent from the hot path.
7. p95 latency of `GET /api/events` on a 1M-event session with no filter is < 300 ms, measured by `PerformanceTests.EventList_1MEventSession_Under300ms`.
8. All Phase 1–5 integration tests pass.

**Dependencies:** TRC-P5-001

---

## TRC-P5-003 — Extended SSE for Filtered Events

**Design:** [tracer_phase5_design.md §4.7](./tracer_phase5_design.md#47-sse-for-live-events-extended-from-phase-3), [§8.4](./tracer_phase5_design.md#84-live-sse-endpoint)
**Architecture:** [tracer_architecture_v1.md §17](./tracer_architecture_v1.md#17-performance-targets) *(SSE event → marker visible < 100 ms)*

`SseFilter` is extended from Phase 3's notables-only filter to evaluate the full filter expression — topic, node, traceId, entityId, playerId, severity, notablesOnly — using O(1) `HashSet` lookups per field. `LiveEventBroadcaster.Publish` is unchanged (it fans out to all registered connections); each connection now carries a richer `SseFilter` that gates delivery. A new `GET /api/live/events` endpoint is added to `LiveEventStreamEndpoints` following the handler shape in §8.4; it accepts the same filter parameters as `/api/events` and registers a connection with the full filter. The Phase 3 `/api/live/notables` endpoint is retained unchanged for backward compatibility.

**Success conditions:**

1. `SseFilterTests` (`Tracer.Tests.Unit/WebApi/SseFilterTests.cs`) exists with the following passing test methods:
   - `Matches_NotablesOnly_ExcludesEventsWithoutLabel` — an event with `NotableLabel = null` does not match a `NotablesOnly = true` filter.
   - `Matches_TopicFilter_ExcludesNonMatchingTopic`.
   - `Matches_MultipleTopics_MatchesAnyListed` — filter with two topics; event on either topic matches.
   - `Matches_NodeFilter_ExcludesNonMatchingNode`.
   - `Matches_TraceIdFilter_ExcludesNonMatchingTrace`.
   - `Matches_EntityIdFilter_ExcludesNonMatchingEntityId`.
   - `Matches_PlayerIdFilter_ExcludesNonMatchingPlayerId`.
   - `Matches_SeverityFilter_ExcludesNonMatchingSeverity`.
   - `Matches_MultipleFilterTypesCompose_RequiresAllToMatch` — event matches topic filter but not severity filter; `Matches` returns false.
   - `Matches_EmptyFilter_AllEventsMatch` — filter with all null/empty collections; every event matches.
2. `LiveEventBroadcasterTests` (`Tracer.Tests.Unit/WebApi/LiveEventBroadcasterTests.cs`) is extended with:
   - `Publish_ConnectionWithTopicFilter_OnlyDeliverMatchingEvents` — two SSE connections, one topic-filtered; confirm only matching events enqueued to the filtered connection.
   - `Publish_TenClientsAtThousandEventsPerSecond_NoDropsOrCrashes` — synthetic load test: 10 clients × 1000 events/sec for 1 second; all connections receive expected counts; no exceptions.
3. `LiveEventStreamEndpointsTests` (`Tracer.Tests.Unit/WebApi/LiveEventStreamEndpointsTests.cs`) exists with:
   - `GetLiveEvents_ContentTypeIsTextEventStream` — `Content-Type: text/event-stream` header present.
   - `GetLiveEvents_WithTopicFilter_OnlyMatchingEventsDelivered` — inject events matching and not matching the filter; only matching appear in the SSE stream.
   - `GetLiveEvents_XAccelBufferingNoCache_HeadersPresent`.
4. `/api/live/events` appears in the OpenAPI document; `useApi` composable gains `subscribeToLiveEvents(filter)` method.
5. `/api/live/notables` (Phase 3) continues to pass its existing tests unchanged.
6. End-to-end SSE latency test `LiveStreamLatencyTests.SseEvent_ArrivesAtClientWithinBudget` measures the wall-clock delta between `LiveEventBroadcaster.Publish` and the client receiving the SSE frame; median < 50 ms, p99 < 100 ms.
7. All Phase 1–5 integration tests pass.

**Dependencies:** TRC-P5-001, TRC-P3-009

---

## TRC-P5-004 — Timeline Canvas Renderer

**Design:** [tracer_phase5_design.md §5.6](./tracer_phase5_design.md#56-timelinerenderer--the-canvas-drawing-module), [§5.7](./tracer_phase5_design.md#57-hitindex), [§6](./tracer_phase5_design.md#6-pan-zoom-hover-click)
**Architecture:** [tracer_architecture_v1.md §17](./tracer_architecture_v1.md#17-performance-targets) *(pan/zoom response < 100 ms; render 5000 markers < 50 ms)*

`timelineRenderer.ts` implements the pure Canvas2D draw logic: swimlane backgrounds, gridlines, raw-event markers (circles for standard, squares for notables), aggregate bars per (bucket, node), and selection highlight. `timelineLayout.ts` provides coordinate math and `chooseBucketDuration` bucketing thresholds. `timelineHitTest.ts` implements the 64×16-cell uniform-grid `HitIndex` built during render and used for O(1) pointer lookup. `timelineAggregator.ts` provides client-side bucket merging for incremental live-mode updates in aggregate mode. `colorScheme.ts` is extended with per-node deterministic palette assignment and per-severity colors. All modules are pure TypeScript (no Vue reactivity, no DOM) and are testable in Vitest with a canvas mock.

**Success conditions:**

1. `timelineRenderer.spec.ts` (`tracer-viewer/tests/unit/timelineRenderer.spec.ts`) exists with the following passing test methods:
   - `drawsOneMarkerPerEventInListMode` — `render()` with 5 events calls `ctx.arc` exactly 5 times.
   - `drawsSquareForNotableEvents` — an event with a non-null `notableLabel` calls `ctx.fillRect` rather than `ctx.arc`.
   - `drawsBarPerBucketGroupInAggregateMode` — aggregate input with 3 buckets × 2 nodes calls `ctx.fillRect` at least 6 times.
   - `skipsEventsOutsideViewport` — events whose `publishWallclock` falls outside `[fromMs, toMs]` produce no draw calls.
   - `handlesEmptyEventsListWithoutError`.
   - `hitIndexHasEntryForEachDrawnMarker` — `result.hitIndex.findMarkerAt(x, y)` is non-null for each marker's expected canvas coordinate.
2. `timelineLayout.spec.ts` (`tracer-viewer/tests/unit/timelineLayout.spec.ts`) exists with the following passing test methods:
   - `chooseBucketDuration_SubOneMinute_ReturnsRaw`.
   - `chooseBucketDuration_FiveMinutes_Returns100ms`.
   - `chooseBucketDuration_ThirtyMinutes_Returns5s`.
   - `chooseBucketDuration_OneHour_Returns30s`.
   - `chooseBucketDuration_FourHoursOrMore_Returns5m`.
   - `chooseBucketDuration_BoundaryValues_CorrectThresholdBehavior` — spans at each threshold boundary (60000 ms, 300000 ms, 1800000 ms, 3600000 ms, 14400000 ms) return the expected bucket or 'raw'.
3. `timelineHitTest.spec.ts` (`tracer-viewer/tests/unit/timelineHitTest.spec.ts`) exists with the following passing test methods:
   - `findMarkerAt_ExactCoordinate_ReturnsMarker`.
   - `findMarkerAt_InsideRadius_ReturnsMarker`.
   - `findMarkerAt_OutsideAllMarkers_ReturnsNull`.
   - `findMarkerAt_TwoMarkersInSameCell_ReturnsCloserOne`.
   - `findBucketAt_PointInsideBucket_ReturnsBucket`.
   - `findBucketAt_PointOutsideBucket_ReturnsNull`.
   - `performanceWith1000Markers_FindTakesUnder1ms` — 1000 markers inserted; 100 random lookup calls each complete within the 1 ms budget measured with `performance.now()`.
4. `render()` measured via `timelineRenderer.spec.ts` perf test with 5000 markers completes in < 50 ms on the CI runner (guarded by `expect(elapsed).toBeLessThan(50)`).
5. `colorScheme.ts` extended unit test `colorScheme.spec.ts` asserts: the same node name always produces the same hex color across two independent calls (`isDeterministic`), and the three severity colors (`info`, `warning`, `error`) are distinct.
6. All Phase 1–5 integration tests pass.

**Dependencies:** TRC-P5-002

---

## TRC-P5-005 — TimelineView Vue Components

**Design:** [tracer_phase5_design.md §5.1](./tracer_phase5_design.md#51-overall-layout), [§5.2](./tracer_phase5_design.md#52-component-responsibilities), [§6](./tracer_phase5_design.md#6-pan-zoom-hover-click), [§8.3](./tracer_phase5_design.md#83-auto-follow-ux), [§9.1](./tracer_phase5_design.md#91-bundlesview-vue)
**Architecture:** [tracer_architecture_v1.md §17](./tracer_architecture_v1.md#17-performance-targets) *(open session → first render < 500 ms)*

`TimelineView.vue` is the top-level page component: it reads `sessionId` from the route, initialises the timeline store, and composes the CSS Grid layout shell hosting `FilterPanel`, `TimelineCanvas`, `TimelineToolbar`, `TimelineAxis`, `EventInspector`, and `DensityIndicator`. `TimelineCanvas.vue` mounts the `<canvas>` element, registers pointer event handlers for pan (§6.1), wheel zoom (§6.3), hover (§6.4), and click-to-select (§6.5), and delegates all drawing to `useCanvasRenderer`. `TimelineAxis.vue` renders an SVG tick row below the canvas. `TimelineToolbar.vue` contains zoom-preset buttons and the auto-follow toggle (§8.3). `Swimlane.vue` renders the per-node label and color key in the left-edge chrome. `DensityIndicator.vue` shows the "showing N of M events" or "buckets of Xs" badge. `BundlesView.vue` is added as a separate page at `/bundles` listing built bundles with download links and inline build-trigger per §9.1–§9.2.

**Success conditions:**

1. `timeline-view.spec.ts` (Playwright, `tracer-viewer/tests/e2e/timeline-view.spec.ts`) exists with the following passing test methods:
   - `timelineView_renders_canvasAfterSessionLoad` — navigates to `/v/timeline/{sessionId}`; `canvas.timeline-canvas` is visible; no console errors.
   - `timelineView_pan_updatesUrlFromTo` — simulates a horizontal drag on the canvas; after 200 ms, the URL contains updated `from=` and `to=` query parameters.
   - `timelineView_zoom_changesViewportSpan` — wheel event reduces the viewport span; URL `to - from` is smaller than before.
   - `timelineView_clickMarker_opensInspector` — clicks a marker position; `.event-inspector` becomes visible.
   - `timelineView_clickBucket_zoomsIn` — in aggregate mode, clicks a bucket; viewport span shrinks.
   - `timelineView_followToggle_enablesAutoFollow` — clicks the Follow toggle; button gains `toolbar__follow--active` class.
2. `TimelineCanvas.vue` uses `pointer-events` (`setPointerCapture` / `releasePointerCapture`) for drag; verified by `TimelineCanvas.spec.ts` unit test `panHandler_capturesPointerOnDown`.
3. `TimelineToolbar.vue` unit test `TimelineToolbar.spec.ts`:
   - `followToggle_disabledWhenSessionNotLive` — when `isLiveSession = false`, the Follow button is disabled.
   - `zoomPreset_5m_setsViewportTo5MinuteSpan`.
4. `BundlesView.vue` unit test `BundlesView.spec.ts`:
   - `bundlesView_listsAllBundlesFromApi` — mocked `api.listBundles()` returns 3 entries; 3 `<li class="bundles__item">` elements rendered.
   - `bundlesView_downloadLink_containsBundleId` — each item's anchor `href` includes the bundle ID.
   - `bundlesView_buildBundleButton_callsBuildApi` — clicking "Build bundle" invokes `api.buildBundle` with the correct `sessionId`.
5. `DensityIndicator.vue` renders "Showing N of M events" when `queryMode === 'list'` and "Buckets of Xs" when `queryMode === 'aggregate'`; verified by `DensityIndicator.spec.ts`.
6. The timeline view is reachable at `/v/timeline/:sessionId` in Vue Router; `BundlesView` at `/bundles`; both routes lazy-load their page component.
7. All Phase 1–5 integration tests pass.

**Dependencies:** TRC-P5-004, TRC-P5-003

---

## TRC-P5-006 — Timeline Composables & Store

**Design:** [tracer_phase5_design.md §5.3](./tracer_phase5_design.md#53-timelinestore-pinia), [§5.4](./tracer_phase5_design.md#54-usetimelinequery--the-data-fetching-driver), [§7](./tracer_phase5_design.md#7-url-state-and-sharing), [§8.1](./tracer_phase5_design.md#81-live-streaming-wiring), [§8.2](./tracer_phase5_design.md#82-appending-live-events), [§5.8](./tracer_phase5_design.md#58-usecanvasrenderer)
**Architecture:** [tracer_architecture_v1.md §17](./tracer_architecture_v1.md#17-performance-targets) *(filter apply < 300 ms; SSE event → marker < 100 ms)*

`timelineStore.ts` (Pinia) is the single source of truth for viewport, filter, query result, query mode, selection, and follow-live flag; its `panBy`, `zoomBy`, and `appendLiveEvent` actions encapsulate all mutations. `useTimelineQuery.ts` watches viewport+filter and issues `listEvents` or `aggregateEvents` with 100 ms debounce and `AbortController` cancellation; it also manages the 5-second aggregate-mode re-poll timer when in live follow mode. `useTimelineUrl.ts` implements bidirectional URL↔store binding: route query params restore store state on mount; store changes are reflected into the URL via `router.replace` with a 250 ms debounce. `useTimelineLiveStream.ts` opens `GET /api/live/events` via `@microsoft/fetch-event-source` and calls `store.appendLiveEvent` on each message. `useTimelineSelection.ts` manages `selectedEventId` and exposes `filterToTrace`, `showInScenario` navigation actions. `useCanvasRenderer.ts` uses `watchEffect` to re-render on any viewport/result/selection change with DPI-correct sizing. `useResizeObserver.ts` is a generic resize composable that triggers canvas re-render on container dimension changes.

**Success conditions:**

1. `useTimelineQuery.spec.ts` (`tracer-viewer/tests/unit/useTimelineQuery.spec.ts`) exists with the following passing test methods:
   - `viewportChange_triggersQuery` — changing store viewport triggers exactly one API call.
   - `rapidViewportChanges_onlyLastQueryFires` — 5 viewport changes within 50 ms result in exactly 1 API call (debounce + abort).
   - `spanThreshold_switchesListToAggregate` — viewport span > 4 hours causes `api.aggregateEvents` to be called instead of `api.listEvents`.
   - `queryError_setsStoreError`.
   - `abortError_doesNotSurfaceAsStoreError`.
   - `aggregateLiveMode_repolls_every5Seconds` — when `queryMode === 'aggregate'` and `followLive === true`, a timer fires `fetchDebounced` at 5-second intervals.
2. `useTimelineUrl.spec.ts` (`tracer-viewer/tests/unit/useTimelineUrl.spec.ts`) exists with the following passing test methods:
   - `urlParams_restoreStoreStateOnMount` — mounting with `?from=T1&to=T2&topic=foo` sets `store.viewport.from`, `store.viewport.to`, and `store.filter.topics` to the parsed values.
   - `storeChange_updatesUrl_debounced` — store viewport change updates `router.replace` params after the debounce settles.
   - `multipleTopicValues_encodedAsRepeatedParams` — `store.filter.topics = ['a', 'b']` produces `?topic=a&topic=b`.
   - `selectEvent_addsSelectParam`.
   - `followLive_addsFollowTrueParam`.
   - `routerReplace_notPush_preventsHistoryChurn` — pan gesture uses `router.replace`; browser history length unchanged.
3. `useTimelineLiveStream.spec.ts` (`tracer-viewer/tests/unit/useTimelineLiveStream.spec.ts`) exists with:
   - `onMessage_callsAppendLiveEvent` — a synthetic SSE message triggers `store.appendLiveEvent` with the parsed DTO.
   - `filterChange_reconnects` — changing store filter aborts the current SSE connection and opens a new one with updated URL parameters.
   - `unmount_abortsConnection`.
4. `timelineStore.spec.ts` (`tracer-viewer/tests/unit/timelineStore.spec.ts`) exists with:
   - `panBy_shiftsViewportByCorrectMs` — `panBy(30000)` shifts both `from` and `to` by 30 000 ms.
   - `panBy_disablesFollowLive`.
   - `zoomBy_halvesSpanAroundCenter` — `zoomBy(0.5, center)` produces a span half as wide centered on the given timestamp.
   - `appendLiveEvent_listMode_appendsToEvents` — in list mode, event count increases by 1.
   - `appendLiveEvent_followLive_slidesViewport` — if `followLive` is true and the event is after `viewport.to`, viewport advances.
   - `appendLiveEvent_aggregateMode_doesNotMutateQueryResult` — in aggregate mode, `appendLiveEvent` does not change `queryResult`.
5. `useResizeObserver.ts` unit test: `resizeObserver_triggerCallback_onDimensionChange` — a simulated `ResizeObserver` entry with new dimensions calls the registered callback once.
6. `useTimelineSelection.ts` unit test: `filterToTrace_addsTraceIdFilter` — calling `filterToTrace('AAAA...')` sets `store.filter.traceId` and `showInScenario` navigates to `/scenario/{sessionId}`.
7. All Phase 1–5 integration tests pass.

**Dependencies:** TRC-P5-005, TRC-P5-004

---

## TRC-P5-007 — FilterPanel, EventInspector & Filter Types

**Design:** [tracer_phase5_design.md §5.2](./tracer_phase5_design.md#52-component-responsibilities), [§6.5](./tracer_phase5_design.md#65-click--selection--inspector), [§7.3](./tracer_phase5_design.md#73-pivots-update-the-url), [§9](./tracer_phase5_design.md#9-bundle-library)
**Architecture:** [tracer_architecture_v1.md §17](./tracer_architecture_v1.md#17-performance-targets) *(click event → inspector populated < 100 ms)*

`FilterPanel.vue` renders the left-rail filter UI with expandable sections for topic, node, traceId, entityId, playerId, severity, and notables-only; each active filter renders as a `FilterChip.vue` pill with a remove button. `EventInspector.vue` fetches `GET /api/events/{id}` on `store.selectedEventId` change and displays the full payload as pretty-printed syntax-highlighted JSON; it includes pivot buttons — "Filter to this trace" (active), "Show in scenario" (active), "Show causal tree" (disabled, stub for Phase 6), "Show entity history" (disabled, stub for Phase 7), and "Copy event ID" (active). The `filter.ts` type module defines `TimelineFilter`, `FilterChipValue`, and all related interfaces consumed by the store, URL composable, and API client. Cross-view navigation hooks for Phase 6 targets are present as no-op stubs with `// TODO Phase 6` comments so Phase 6 can enable them without structural changes.

**Success conditions:**

1. `FilterPanel.spec.ts` (`tracer-viewer/tests/unit/FilterPanel.spec.ts`) exists with the following passing test methods:
   - `filterPanel_showsActiveFiltersAsChips` — store with `filter.topics = ['weapons.fire']` renders one `FilterChip` with text containing `weapons.fire`.
   - `filterPanel_removeChip_removesFilterFromStore` — clicking the remove button on a chip clears that value from `store.filter`.
   - `filterPanel_addTopic_updatesStore` — simulating user input and confirm in the topic section calls `store.applyFilter` with the new topic included.
   - `filterPanel_notablesToggle_setsNotablesOnly` — toggling the notables-only control sets `store.filter.notablesOnly = true`.
2. `FilterChip.spec.ts` exists with:
   - `filterChip_displaysLabelAndValue` — renders both the filter type label (e.g., "topic") and the value.
   - `filterChip_removeButton_emitsRemoveEvent`.
3. `EventInspector.spec.ts` (`tracer-viewer/tests/unit/EventInspector.spec.ts`) exists with the following passing test methods:
   - `eventInspector_fetchesEventOnSelectionChange` — `store.selectedEventId` changing triggers `api.getEvent(id)`.
   - `eventInspector_rendersPayloadJson_prettyPrinted` — mock event with `payloadJson = '{"a":1}'` renders `"a": 1` in the DOM.
   - `eventInspector_filterToTrace_addsTraceFilter` — clicking "Filter to this trace" calls `store.applyFilter` with the event's `traceId` set.
   - `eventInspector_showInScenario_navigatesToScenarioRoute` — clicking "Show in scenario" triggers router navigation to `/scenario/{sessionId}`.
   - `eventInspector_showCausalTree_isDisabled` — "Show causal tree" button has `disabled` attribute in Phase 5.
   - `eventInspector_showEntityHistory_isDisabled` — "Show entity history" button has `disabled` attribute in Phase 5.
   - `eventInspector_copyEventId_writesToClipboard` — clicking "Copy event ID" calls `navigator.clipboard.writeText` with the event ID.
   - `eventInspector_hiddenWhenNoEventSelected` — when `store.selectedEventId === null`, the inspector panel is not rendered or has `display: none`.
4. `filter.ts` exports: `TimelineFilter` interface (all filter fields typed), `FilterChipValue` type union covering all chip variants; `TimelineFilter` is used consistently in the store, `useTimelineUrl`, and API client.
5. The Phase 6 cross-view navigation stubs are present: `EventInspector.vue` contains `// TODO Phase 6: enable causal tree navigation` and `// TODO Phase 7: enable entity history navigation` comment markers adjacent to the disabled buttons; verified by `grep_search` or a test that checks for their presence.
6. End-to-end test `timeline-view.spec.ts` extended with:
   - `e2e_filterPanel_addAndRemoveFilter_updatesTimeline` — adds a topic filter via the FilterPanel; URL gains `topic=` param; removes it; URL loses the param.
   - `e2e_eventInspector_filterToTrace_pivots` — clicks a marker, clicks "Filter to this trace" in the inspector; URL gains `trace=` param; a filter chip appears.
7. All Phase 1–5 integration tests pass.

**Dependencies:** TRC-P5-006, TRC-P5-005

## TRC-P5-008 — Bundle Library UI

**Design:** [tracer_phase5_design.md §9](./tracer_phase5_design.md#9-bundle-library)

`BundlesView.vue` surfaces the bundle list from `GET /api/bundles`, letting engineers browse, inspect, and download built bundles from within the SPA. `bundleStore.ts` holds the list in Pinia and manages fetch/error state. `SessionCard.vue` gains a "Build bundle" action that calls `POST /api/bundles/build`, polls status, and exposes a download link on completion. The view adapts its affordances to mode: download link in live mode; a descriptive hint directing the user to the Open Bundle screen in offline-viewer mode.

**Success conditions:**
1. `BundlesView.vue` exists at `tracer-viewer/src/views/BundlesView.vue` and is registered as a route at `/bundles`; navigating to `/bundles` renders the view without runtime errors.
2. `bundleStore.ts` (`defineStore('bundles', ...)`) exposes `bundles: BundleListEntryDto[]`, `loading: boolean`, and `error: string | null`; calling the store's `load()` action fetches `GET /api/bundles` and populates `bundles`.
3. Vitest test `BundlesView.spec.ts::renders_bundle_list_from_store` — given a store pre-populated with two `BundleListEntryDto` entries, the rendered `BundlesView` contains two list items each showing the bundle label and a human-readable size.
4. Vitest test `BundlesView.spec.ts::shows_empty_state_when_no_bundles` — a store with an empty `bundles` array causes the view to render the text "No bundles built yet" and no list items.
5. Vitest test `BundlesView.spec.ts::shows_error_state_on_fetch_failure` — when `load()` rejects, the view renders an error message and no list items.
6. Vitest test `BundlesView.spec.ts::shows_offline_hint_in_bundle_mode` — when `useBundleMode().isLive` is `false`, the view renders the offline hint "To open a different bundle, return to the Open Bundle screen." and omits download links.
7. Download links use the pattern `/api/bundles/{bundleId}/download` per §9.1; a rendered bundle card for a known `bundleId` contains an `<a>` element whose `href` matches that pattern.
8. Vitest test `SessionCard.spec.ts::buildBundle_showsProgressThenDownloadLink` — clicking the "Build bundle" button calls `POST /api/bundles/build`; while the polled status is `InProgress` a progress indicator is visible; when status transitions to `Completed` a download link appears.
9. All Phase 1–5 integration tests pass.

**Dependencies:** TRC-P5-005, TRC-P5-006

---

## TRC-P5-009 — Shareable URLs & URL State

**Design:** [tracer_phase5_design.md §7](./tracer_phase5_design.md#7-url-state-and-sharing)

`useTimelineUrl.ts` maintains bidirectional binding between the timeline store and the Vue Router query string. URL changes immediately restore the store (watcher `immediate: true`); store changes update the URL via a 250 ms debounced `router.replace` — never `router.push` — so continuous pan/zoom gestures do not pollute browser history. Every meaningful timeline state is encoded and restored: viewport `from`/`to`, all filter types (topic, node, entity, player, severity, traceId, notablesOnly), selected event ID, and follow mode. The same URL opened on two machines with access to the same session or bundle reproduces identical viewport, filters, and selection.

**Success conditions:**
1. `useTimelineUrl.ts` exists at `tracer-viewer/src/composables/useTimelineUrl.ts` and is called in `TimelineView.vue` setup; it registers a watcher on `route.query` (immediate) and a debounced watcher on the combined store viewport, filter, and selection state.
2. Vitest test `useTimelineUrl.spec.ts::urlParams_AppliedToStoreOnMount` — mounting a component that calls `useTimelineUrl` with `route.query = { from: '2026-05-19T14:00:00Z', to: '2026-05-19T14:30:00Z', topic: ['weapons.fire'] }` results in `store.viewport.from`, `store.viewport.to`, and `store.filter.topics` set to the decoded values without any user action.
3. Vitest test `useTimelineUrl.spec.ts::storeChange_UpdatesUrlDebounced` — updating `store.viewport` does not call `router.replace` synchronously; `router.replace` is called once after the 250 ms debounce, with `from` and `to` in ISO 8601 format in the query object.
4. Vitest test `useTimelineUrl.spec.ts::multipleFilterValues_EncodedAsRepeatedParams` — setting `store.filter.topics = ['a', 'b']` produces `?topic=a&topic=b` in the router query; reading that URL back into a fresh store restores `store.filter.topics` as `['a', 'b']`.
5. Vitest test `useTimelineUrl.spec.ts::selectedEvent_RoundTripsViaUrl` — setting `store.selectedEventId = 'AABBCCDD11223344'` encodes `?select=AABBCCDD11223344` in the URL; restoring from that URL sets `store.selectedEventId` to the same string.
6. Vitest test `useTimelineUrl.spec.ts::panGesture_UsesReplaceNotPush` — repeated viewport changes all invoke `router.replace` (not `router.push`); `router.push` is never called by the composable.
7. E2E test `timeline-view.spec.ts::shareableUrl_SameViewOnReload` — navigate to the timeline, pan and apply a topic filter, capture the URL, reload the page with that URL, and assert that the `from`, `to`, and `topic` query params are present in the new URL and the corresponding filter chip is visible in `FilterPanel`.
8. All Phase 1–5 integration tests pass.

**Dependencies:** TRC-P5-005, TRC-P5-006

---

## TRC-P5-010 — Auto-Follow Live Mode

**Design:** [tracer_phase5_design.md §8](./tracer_phase5_design.md#8-live-mode-and-auto-follow)

`useTimelineLiveStream.ts` opens a Server-Sent Events connection to `/api/live/events` with the current filter and appends arriving events to the store via `appendLiveEvent`. In follow mode (`store.viewport.followLive = true`), each new event whose timestamp lies beyond `viewport.to` advances the viewport forward, preserving the existing span and adding 5 s of leading headroom. Any user-initiated viewport change clears `followLive`, halting automatic advance. The Follow toggle in `TimelineToolbar` re-enables follow and snaps the viewport to the live edge. In aggregate mode, live arrivals trigger a periodic 5 s refetch rather than individual appends.

**Success conditions:**
1. `useTimelineLiveStream.ts` exists at `tracer-viewer/src/composables/useTimelineLiveStream.ts`; it subscribes to `/api/live/events` on mount via `@microsoft/fetch-event-source` and unsubscribes on unmount by aborting the `AbortController`.
2. Vitest test `useTimelineLiveStream.spec.ts::receivedEvent_AppendedToStoreInListMode` — simulating an SSE `message` event causes `store.appendLiveEvent` to be called with the parsed `EventDto`; `store.queryResult.events.length` increases by one and `store.queryResult.totalMatching` increases by one.
3. Vitest test `useTimelineLiveStream.spec.ts::followMode_ViewportSlidesOnNewEvent` — with `store.viewport.followLive = true` and an event whose `publishWallclock` is beyond the current `viewport.to`, `appendLiveEvent` advances `store.viewport.from` and `store.viewport.to` forward such that the event is within the new viewport and the span is unchanged.
4. Vitest test `useTimelineLiveStream.spec.ts::panGesture_DisablesFollow` — `store.panBy(5000)` sets `store.viewport.followLive = false`; subsequent calls to `appendLiveEvent` with out-of-viewport events do not advance the viewport.
5. Vitest test `useTimelineLiveStream.spec.ts::filterChange_ReconnectsStream` — changing `store.filter` causes the existing `AbortController.abort()` to be called and a new SSE connection to open with updated query parameters; `abort` is called exactly once per filter change.
6. Vitest test `useTimelineLiveStream.spec.ts::aggregateMode_LiveEventsDoNotAppend` — when `store.queryMode === 'aggregate'`, an arriving SSE event does not call `appendLiveEvent`; a 5 s timer schedules a refetch instead.
7. Vitest test `TimelineToolbar.spec.ts::followToggle_EnablesFollowAndSnapsToLiveEdge` — clicking the "Follow live" button sets `store.viewport.followLive = true` and updates `store.viewport.to` to within 5 s of `Date.now()`; the button label changes to "Following live".
8. E2E test `timeline-view.spec.ts::autoFollow_KeepsLiveEdgeVisible` — with a mock SSE stream emitting events every 500 ms, the `to` query param in the URL advances over time while `follow=true` is in the URL; a canvas click removes `follow` from the URL and stops the advance.
9. All Phase 1–5 integration tests pass.

**Dependencies:** TRC-P5-003, TRC-P5-005, TRC-P5-006

---

## TRC-P5-011 — Backend Unit Tests

**Design:** [tracer_phase5_design.md §11.1](./tracer_phase5_design.md#111-backend-unit-tests)

Unit tests exercise the six new backend components introduced in Phase 5 — `IntervalSetTracker`, `LiveMultiIntervalReader`, `EventQueryService`, `EventAggregationService`, `EventEndpoints` (list), and `EventEndpoints` (aggregate) — against in-memory DuckDB fixtures with no network or production disk I/O. Each class is tested in isolation with xUnit and FluentAssertions. The suite provides a fast regression signal covering filter composition semantics, ordering guarantees, error branches, and pool lifecycle invariants.

**Success conditions:**
1. `IntervalSetTrackerTests.cs` (in `Tracer.Tests.Unit/MultiInterval/`) passes all of:
   - `InitializeAsync_NoCompletedIntervals_SnapshotContainsOnlyActiveInterval`
   - `InitializeAsync_FiveCompletedIntervals_CappedTo3InSnapshot`
   - `OnIntervalRotatedAsync_DemotesPreviousActiveToCompleted_AddsNewActive`
   - `OnIntervalEvictedAsync_RemovesEvictedIntervalFromSnapshot`
   - `SetChanged_FiresAfterInitialize`
   - `SetChanged_FiresAfterRotation`
   - `SetChanged_FiresAfterEviction`
   - `SetChanged_DoesNotFireWhenEvictedIntervalWasNotInCurrentSet`
2. `LiveMultiIntervalReaderTests.cs` (in `Tracer.Tests.Unit/MultiInterval/`) passes all of:
   - `InitializeAsync_BuildsPoolConnectionsEqualToConfiguredPoolSize`
   - `AcquireAsync_ReturnsConnectionWithCurrentIntervalsAttached`
   - `AfterSetChangedFires_NewAcquiredConnectionsHaveUpdatedIntervalSet`
   - `ConnectionIssuedFromOldPool_DisposesRatherThanReturnsToPool`
   - `ConcurrentAcquireAndRebuild_CompletesWithoutExceptionOrLeak`
3. `EventQueryServiceTests.cs` (in `Tracer.Tests.Unit/WebApi/`) passes all of:
   - `ListAsync_EmptyFilter_ReturnsEventsInPublishWallclockAscendingOrder`
   - `ListAsync_TimeRange_ReturnsOnlyEventsWithinRange`
   - `ListAsync_SingleTopicFilter_ReturnsOnlyMatchingTopic`
   - `ListAsync_MultipleTopics_OredWithinFilter`
   - `ListAsync_TopicAndSeverity_AndedAcrossFilterTypes`
   - `ListAsync_LimitHit_TotalMatchingReflectsTrueCount_TruncatedTrue`
   - `ListAsync_TraceIdFilter_ReturnsOnlyEventsForThatTrace`
   - `ListAsync_OrderDescending_ReturnsNewestFirst`
   - `ListAsync_EmptyResult_TotalMatchingIsZero_TruncatedFalse`
4. `EventAggregationServiceTests.cs` (in `Tracer.Tests.Unit/WebApi/`) passes all of:
   - `AggregateAsync_OneHourViewportAt5sBuckets_Returns720Buckets`
   - `AggregateAsync_EmptyTimeRange_ReturnsEmptyBucketList`
   - `AggregateAsync_GroupByNone_EachBucketHasOnlyOneGroupWithNullKey`
   - `AggregateAsync_GroupByNode_GroupsResultsByPublisherNode`
   - `AggregateAsync_FilterAppliedBeforeGrouping_OnlyMatchingEventsCounted`
   - `AggregateAsync_BucketTotalEqualsGroupCountSum`
   - `AggregateAsync_InvalidBucketDuration_ThrowsArgumentException`
5. `EventEndpointsListTests.cs` (in `Tracer.Tests.Unit/WebApi/`) passes all of:
   - `GetEvents_ValidRequest_Returns200WithEventListDto`
   - `GetEvents_LimitZero_Returns400ProblemDetails`
   - `GetEvents_LimitOver5000_Returns400ProblemDetails`
   - `GetEvents_UnknownSessionId_Returns404ProblemDetails`
   - `GetEvents_MultipleTopicQueryParams_PassedAsListToQueryService`
6. `EventEndpointsAggregateTests.cs` (in `Tracer.Tests.Unit/WebApi/`) passes all of:
   - `GetAggregate_ValidRequest_Returns200WithAggregateDto`
   - `GetAggregate_InvalidBucketDuration_Returns400ProblemDetails`
   - `GetAggregate_MissingFromOrTo_Returns400ProblemDetails`
7. All Phase 1–5 integration tests pass.

**Dependencies:** TRC-P5-001, TRC-P5-002, TRC-P5-003

---

## TRC-P5-012 — Backend Integration Tests

**Design:** [tracer_phase5_design.md §11.2](./tracer_phase5_design.md#112-backend-integration-tests)

Integration tests spin up the full Observer fixture (or the offline-viewer fixture for bundle mode), write events across multiple real DuckDB interval files, and assert correct end-to-end behavior of the live multi-interval query path and the live/bundle round-trip parity guarantee. Performance assertions are enforced inline with `Stopwatch`, gating on the latency targets from §1.3: open session (first response) under 500 ms on a 1 M-event session, and aggregate query under 1 s on a 100 M-event session.

**Success conditions:**
1. `LiveMultiIntervalQueryTests.cs` (in `Tracer.Tests.Integration/`) passes all of:
   - `LiveQuery_EventsSpanThreeIntervals_AllReturnedByListEndpoint` — events pushed into 3 sequential intervals are all returned by `GET /api/events` for the full session range; returned count equals the sum of all pushed events.
   - `LiveQuery_ResultsOrderedAcrossIntervalBoundaries` — response events are in strictly ascending `publishWallclock` order regardless of which interval they originate from.
   - `LiveQuery_AfterRotation_NewActiveIntervalQueriedImmediately` — events pushed into the new active interval after a simulated rotation are returned by `GET /api/events` without restarting the Observer.
   - `LiveQuery_AfterEviction_EvictedIntervalExcludedFromResults` — with `CompletedIntervalsToInclude=1` and 3 completed intervals, only events from the most recent completed interval and the active interval appear in results; events from the oldest completed interval do not appear.
2. `TimelineRoundTripTests.cs` (in `Tracer.Tests.Integration/`) passes all of:
   - `RoundTrip_ListQuery_LiveAndBundleReturnIdenticalEvents` — identical `GET /api/events` parameters against the live Observer and a bundle built from the same session return the same event IDs in the same order with the same field values.
   - `RoundTrip_AggregateQuery_LiveAndBundleReturnIdenticalBuckets` — identical `GET /api/events/aggregate` parameters against both modes return the same bucket start times, group keys, and counts.
3. Performance: `RoundTrip_OpenSession_1MEvents_FirstResponseUnder500ms` — a 1 M-event session returns the first `/api/events` response (measured from request dispatch to last byte received) in fewer than 500 ms; the test asserts this with `Stopwatch` and fails the run if the threshold is exceeded.
4. Performance: `RoundTrip_AggregateQuery_100MEvents_CompletesUnder1s` — an aggregate query over a 100 M-event synthetic session completes in fewer than 1 000 ms; the test is annotated `[Trait("Category", "Performance")]` to allow selective CI exclusion and explicit on-demand execution.
5. All Phase 1–5 integration tests pass.

**Dependencies:** TRC-P5-001, TRC-P5-002, TRC-P5-003, TRC-P5-011

---

## TRC-P5-013 — Frontend Tests

**Design:** [tracer_phase5_design.md §11.3](./tracer_phase5_design.md#113-frontend-unit-tests-vitest) and [§11.4](./tracer_phase5_design.md#114-e2e-tests-playwright)

Vitest unit tests cover the three pure rendering modules (`timelineRenderer`, `timelineLayout`, `timelineHitTest`) and the two query-orchestration composables (`useTimelineQuery`, `useTimelineUrl`); all run against jsdom with a canvas mock, requiring no browser. Playwright E2E tests validate the integrated timeline interaction loop — pan, zoom, filter application, and click-to-inspect — against the full running application, with interaction-to-visible-result latency gated at < 300 ms per §1.3.

**Success conditions:**
1. `timelineRenderer.spec.ts` (in `tracer-viewer/tests/unit/`) passes all of:
   - `render_ListMode_DrawsOneArcPerNonNotableEvent`
   - `render_ListMode_DrawsOneRectPerNotableEvent`
   - `render_AggregateMode_DrawsFillRectPerBucketGroup`
   - `render_EmptyEventList_NoArcOrRectCallsMade`
   - `render_EventOutsideViewportBounds_SkippedDefensively`
   - `render_ReturnsHitIndexWithEntryForEachDrawnMarker`
2. `timelineLayout.spec.ts` (in `tracer-viewer/tests/unit/`) passes all of:
   - `chooseBucketDuration_SpanUnder60s_ReturnsRaw`
   - `chooseBucketDuration_Span1mTo5m_Returns100ms`
   - `chooseBucketDuration_Span5mTo30m_Returns1s`
   - `chooseBucketDuration_Span30mTo1h_Returns5s`
   - `chooseBucketDuration_Span1hTo4h_Returns30s`
   - `chooseBucketDuration_SpanOver4h_Returns5m`
   - Boundary value at each threshold transition returns the bucket for the lower (narrower) bucket.
3. `timelineHitTest.spec.ts` (in `tracer-viewer/tests/unit/`) passes all of:
   - `findMarkerAt_ExactPosition_ReturnsMarker`
   - `findMarkerAt_WithinMarkerRadius_ReturnsMarker`
   - `findMarkerAt_BeyondMarkerRadius_ReturnsNull`
   - `findMarkerAt_TwoMarkersInSameCell_ReturnsCloserOne`
   - `findMarkerAt_1000Markers_CompletesUnder1ms`
   - `findBucketAt_PointInsideBucket_ReturnsBucket`
   - `findBucketAt_PointOutsideBucket_ReturnsNull`
4. `useTimelineQuery.spec.ts` (in `tracer-viewer/tests/unit/`) passes all of:
   - `viewportChange_TriggersNewQuery`
   - `rapidViewportChanges_Under100ms_OnlyLastQueryFires`
   - `spanBelowThreshold_RequestsRawListEndpoint`
   - `spanAboveThreshold_RequestsAggregateEndpoint`
   - `queryError_SetsStoreError`
   - `abortError_NotSurfacedAsStoreError`
5. `useTimelineUrl.spec.ts` (in `tracer-viewer/tests/unit/`) passes all six test cases named in TRC-P5-009 success conditions 2–6 (`urlParams_AppliedToStoreOnMount`, `storeChange_UpdatesUrlDebounced`, `multipleFilterValues_EncodedAsRepeatedParams`, `selectedEvent_RoundTripsViaUrl`, `panGesture_UsesReplaceNotPush`).
6. Playwright E2E test `timeline-view.spec.ts::pan_ZoomFilter_CompleteUnder300ms` asserts three interactions each complete within 300 ms: (a) horizontal drag causes the URL `from`/`to` params to update; (b) adding a filter via `FilterPanel` causes a new network request to `/api/events` to complete and the canvas to repaint; (c) clicking a marker causes `.event-inspector` to become visible; latency for each is measured with `performance.now()` instrumentation and logged.
7. All Phase 1–5 integration tests pass.

**Dependencies:** TRC-P5-004, TRC-P5-005, TRC-P5-006, TRC-P5-007, TRC-P5-008, TRC-P5-009, TRC-P5-010

<!-- PHASE 5 TASKS END -->

<!-- PHASE 6 TASKS BEGIN -->

## TRC-P6-001 — Schema extension: parent_event_id partial index

**Design:** [tracer_phase6_design.md §3](./tracer_phase6_design.md#3-schema-extension-parent_event_id-index)

Adds a partial index on `parent_event_id` in the events table by extending the `SchemaV1.CreateIndexes` constant in `Tracer.Storage.DuckDB`. The `WHERE parent_event_id != 0` clause excludes root events, halving index size with no impact on query semantics. Because all three write paths (Agent, Observer, Aggregator) consume the same constant, the addition propagates automatically to every new interval and bundle. Existing pre-Phase 6 intervals remain unindexed but queryable; retention evicts them within hours (Option A).

**Success conditions:**
1. `SchemaV1Tests.CreateIndexes_ContainsPartialIndexOnParentEventId` (in `Tracer.Tests.Unit/Storage/`) asserts the `CreateIndexes` string contains the literal clause `idx_events_parent_event_id ON events (parent_event_id) WHERE parent_event_id != 0`.
2. `SchemaAppliedTests.NewInterval_ParentEventIdIndexExists` (in `Tracer.Tests.Integration/`) creates a fresh DuckDB interval via the Agent writer, issues `PRAGMA index_list('events')`, and asserts an entry named `idx_events_parent_event_id` is present.
3. `SchemaAppliedTests.DescendantQuery_ExplainPlanReferencesParentEventIdIndex` runs `EXPLAIN SELECT * FROM events WHERE parent_event_id = 42` against a freshly created interval and asserts the explain output contains `idx_events_parent_event_id`.
4. All Phase 1–6 integration tests pass.

**Dependencies:** TRC-P5-001

---

## TRC-P6-002 — Trace walking backend

**Design:** [tracer_phase6_design.md §4](./tracer_phase6_design.md#4-trace-walking-backend)

Implements `TraceWalker` (static class with `WalkAncestorsAsync` and `WalkDescendantsAsync`) and `TraceQueryService` (singleton with `GetTraceTreeAsync`, `GetTraceTreeForEventAsync`, `GetAncestorTreeAsync`, and `GetDescendantTreeAsync`). Ancestor walks climb the parent pointer chain via primary-key lookups with a visited-set cycle guard; descendant walks use BFS with batched `IN`-clause children queries against the new `parent_event_id` index. When event count exceeds `maxEvents` (default 1 000, hard cap 5 000), the result is truncated and `TraceSummary.Truncated` is set to `true`.

**Success conditions:**
1. `TraceWalkerTests.WalkAncestors_ThreeGenerationChain_ReturnsChainFromStartToRoot` asserts a 3-deep parent chain is returned in leaf-first order ending at the root.
2. `TraceWalkerTests.WalkAncestors_MaxDepthReached_StopsAtLimitAndReturnsPartialChain` asserts that with `maxDepth=2` on a 5-deep chain exactly 2 events are returned and no exception is thrown.
3. `TraceWalkerTests.WalkAncestors_CycleInParentPointers_TerminatesViaCycleGuard` constructs a synthetic parent cycle and asserts the walk terminates without infinite recursion.
4. `TraceWalkerTests.WalkDescendants_BinaryFanout_ReturnsAllNodesInBfsOrder` inserts a root with 2 children and 4 grandchildren and asserts all 6 descendant nodes are returned in breadth-first order.
5. `TraceWalkerTests.WalkDescendants_MaxNodesReached_TruncatesWithoutException` inserts 200 descendants and asserts that with `maxNodes=10` exactly 10 nodes are returned.
6. `TraceQueryServiceTests.GetTraceTree_NormalTrace_ReturnsNodesEdgesAndSummary` pushes 10 events sharing one `trace_id` into a mock interval and asserts the `TraceTree` has 10 nodes, 9 edges, correct root and leaf counts, and `Truncated = false`.
7. `TraceQueryServiceTests.GetTraceTree_ExceedsMaxEvents_ReturnsTruncatedResultWithFlagSet` inserts 6 000 events on a single trace, calls `GetTraceTreeAsync(maxEvents: 5000)`, and asserts `Truncated = true` and `Nodes.Count == 5000`.
8. `TraceQueryServiceTests.GetTraceTreeForEvent_EventWithTraceId_ReturnsSameResultAsDirectTraceCall` asserts the tree returned via `GetTraceTreeForEventAsync` is equivalent to the tree returned by `GetTraceTreeAsync` with the event's `trace_id`.
9. `TraceQueryServiceTests.GetTraceTreeForEvent_EventWithZeroTraceId_ReturnsSingletonTree` asserts that an event with `trace_id=0` yields a 1-node tree, 0 edges, and `Truncated = false`.
10. All Phase 1–6 integration tests pass.

**Dependencies:** TRC-P6-001, TRC-P5-001, TRC-P5-002

---

## TRC-P6-003 — Trace DTOs

**Design:** [tracer_phase6_design.md §5](./tracer_phase6_design.md#53-dtos)

Defines `TraceTreeDto`, `TraceNodeDto`, `TraceEdgeDto`, and `TraceSummaryDto` in `Tracer.WebApi.Contracts.Dto`, and `TraceDtoMapper` that converts the internal `TraceTree`/`TraceNode`/`TraceEdge`/`TraceSummary` records to wire form. All event and trace IDs are serialized as 16-character uppercase hex strings. `TraceNodeDto` carries `PayloadJson` so the inspector can open without a follow-up fetch. `TraceSummaryDto.TotalEventsAvailable` is present only when `Truncated = true`.

**Success conditions:**
1. `TraceDtoMapperTests.MapTraceTree_AllNodesProjected_EventIdIsUppercaseHex16` asserts every `TraceNodeDto.EventId` in the output is a 16-character uppercase hex string equal to the source `EventId` value formatted with `X16`.
2. `TraceDtoMapperTests.MapTraceTree_RootNodes_HaveNullParentEventId` asserts that nodes whose `EventId` does not appear as a `ChildEventId` in any edge have `ParentEventId == null`.
3. `TraceDtoMapperTests.MapTraceEdge_LatencyMs_RoundTripsAsDouble` asserts `TraceEdgeDto.LatencyMs` equals the source `TraceEdge.LatencyMs` without rounding.
4. `TraceDtoMapperTests.MapTraceSummary_WhenTruncated_TotalEventsAvailableIsNonNull` asserts `TotalEventsAvailable` is non-null when the source `Truncated = true`.
5. `TraceDtoMapperTests.MapTraceSummary_WhenNotTruncated_TotalEventsAvailableIsNull` asserts `TotalEventsAvailable` is `null` when `Truncated = false`.
6. All Phase 1–6 integration tests pass.

**Dependencies:** TRC-P6-002

---

## TRC-P6-004 — Trace API endpoints

**Design:** [tracer_phase6_design.md §6](./tracer_phase6_design.md#5-the-trace-api-endpoints)

Registers `TraceEndpoints` (and its `TraceQueryService` singleton) in `ObserverHostBuilder` and the offline-viewer builder, mapping five routes: `GET /api/traces/{traceId}`, `GET /api/traces/{traceId}/tree`, `GET /api/events/{eventId}/trace`, `GET /api/events/{eventId}/ancestors`, and `GET /api/events/{eventId}/descendants`. All IDs are parsed as 16-character hex; invalid input returns a `400 ProblemDetails`. The `maxEvents` query parameter is clamped to `[1, 5000]`; `maxDepth` to `[1, 100]`; `maxNodes` to `[1, 5000]`.

**Success conditions:**
1. `TraceEndpointsTests.GetTraceTree_ValidHexTraceId_Returns200WithNodesAndEdges` sends `GET /api/traces/{id}/tree` against a seeded test host and asserts HTTP 200 and `nodes.length > 0`.
2. `TraceEndpointsTests.GetTraceTree_InvalidHexId_Returns400ProblemDetails` sends a non-hex `traceId` and asserts HTTP 400 with a `ProblemDetails` response body.
3. `TraceEndpointsTests.GetTraceTree_UnknownTraceId_Returns404` sends a valid hex ID matching no events and asserts HTTP 404.
4. `TraceEndpointsTests.GetTraceTree_MaxEventsExceeds5000_ClampedTo5000AndNoError` sends `?maxEvents=99999` and asserts the response succeeds and contains at most 5 000 nodes.
5. `TraceEndpointsTests.GetAncestors_ValidEventId_Returns200WithAncestorChain` asserts HTTP 200 and that `rootEventIds` contains the topmost ancestor.
6. `TraceEndpointsTests.GetDescendants_ValidEventId_Returns200WithDescendantTree` asserts HTTP 200 and that every `leafEventIds` entry has no outgoing edges in the response.
7. `TraceEndpointsTests.GetTraceTree_Under100Events_RespondsBefore300ms` seeds 50 events on one trace, times the full round-trip with `Stopwatch`, and fails if elapsed exceeds 300 ms.
8. `TraceEndpointsTests.GetAncestors_10DeepChain_WalkExpandsBefore200ms` seeds a 10-deep ancestor chain and asserts round-trip under 200 ms via `Stopwatch`.
9. All Phase 1–6 integration tests pass.

**Dependencies:** TRC-P6-002, TRC-P6-003

---

## TRC-P6-005 — DAG layout algorithm

**Design:** [tracer_phase6_design.md §7](./tracer_phase6_design.md#6-the-layout-algorithm)

Implements `causalTreeLayout.ts`, exporting `layout(tree, config): LayoutResult`. Layer assignment uses longest-path-from-roots so converging nodes sit below both parents; within-layer order uses median-of-parents x-position with publish-wallclock as tiebreaker; layer 0 nodes are sorted chronologically. Returns a `Map<string, LaidOutNode>` (each entry holds pixel `(x, y)`), an `LaidOutEdge[]` with pre-computed pixel endpoints, and the total canvas dimensions. Multi-root DAGs are handled natively; no node may appear in more than one position.

**Success conditions:**
1. `causalTreeLayout.spec.ts::layout_SingleRootLinearChain_LayersAreConsecutiveIntegers` creates a 5-node chain and asserts layers are exactly 0, 1, 2, 3, 4.
2. `causalTreeLayout.spec.ts::layout_MultiRootDag_EachNodeAssignedExactlyOnce` constructs a 3-root, 10-node DAG and asserts `result.nodes.size === 10`.
3. `causalTreeLayout.spec.ts::layout_ConvergentNode_LayerIsOnePastMaxParentLayer` constructs two roots (layers 0) each pointing to one shared child and asserts the child's layer is 1.
4. `causalTreeLayout.spec.ts::layout_NodesInSameLayer_HaveDistinctXCoordinates` asserts no two nodes sharing a layer value have the same `x`.
5. `causalTreeLayout.spec.ts::layout_EdgeEndpoints_FromXMatchesParentX_ToXMatchesChildX` asserts `edge.fromX === parent.x` and `edge.toX === child.x` for every laid-out edge.
6. `causalTreeLayout.spec.ts::layout_EmptyTree_ReturnsZeroSizedResult` asserts `nodes.size === 0`, `edges.length === 0`, `widthPx === 0`, and `heightPx === 0`.
7. `causalTreeLayout.spec.ts::layout_500NodeTree_CompletesUnder50ms` generates a synthetic 500-node tree and asserts `layout()` returns in fewer than 50 ms measured with `performance.now()`.
8. All Phase 1–6 integration tests pass.

**Dependencies:** TRC-P6-003

---

## TRC-P6-006 — Causal tree canvas renderer and hit test

**Design:** [tracer_phase6_design.md §8](./tracer_phase6_design.md#7-frontend-rendering)

Implements `causalTreeRenderer.ts` (exporting `renderTree(ctx, layout, input)`) and `causalTreeHitTest.ts` (exporting `findNodeAt(layout, x, y, radius)`). Edges are drawn as Bézier curves with a latency-label pill at the midpoint. Nodes are filled circles colored by `publisherNode` using the Phase 5 `buildNodeColorMap` palette; an inner severity dot marks warning/error nodes; a corner square marks notable nodes; a selection ring is drawn before the fill when the node is selected. `findNodeAt` performs a linear point-in-circle scan returning the nearest node within `radius`.

**Success conditions:**
1. `causalTreeRenderer.spec.ts::renderTree_SingleEdge_CallsBezierCurveToAndFillText` mocks a `CanvasRenderingContext2D` and asserts `bezierCurveTo` and `fillText` are each invoked at least once for a one-edge tree.
2. `causalTreeRenderer.spec.ts::renderTree_ErrorSeverityNode_InnerDotUsesErrorColor` inserts one node with `severity='error'` and asserts an `arc` call is made with `fillStyle === '#e85c5c'`.
3. `causalTreeRenderer.spec.ts::renderTree_NotableNode_FillRectCalledAtCornerOffset` inserts one node with `notableLabel='notable'` and asserts a `fillRect` call with x-offset `+8` and y-offset `-16` from the node center.
4. `causalTreeRenderer.spec.ts::renderTree_SelectedNode_OuterRingArcPrecedesFillArc` inserts one selected node and asserts `arc` is called twice — the first call with radius 18 (ring) before the second with radius 14 (fill).
5. `causalTreeRenderer.spec.ts::renderTree_PublisherNodeColor_MatchesBuildNodeColorMapOutput` creates two nodes with distinct `publisherNode` values and asserts each fill style equals the corresponding value from `buildNodeColorMap`.
6. `causalTreeRenderer.spec.ts::renderTree_500NodeTree_CompletesUnder200ms` renders a 500-node `LayoutResult` against an `OffscreenCanvas` and asserts completion in fewer than 200 ms via `performance.now()`.
7. `causalTreeHitTest.spec.ts::findNodeAt_QueryAtNodeCenter_ReturnsNode` asserts the correct node is returned when the query point equals the node's `(x, y)`.
8. `causalTreeHitTest.spec.ts::findNodeAt_QueryBeyondRadius_ReturnsNull` asserts `null` when the query point is at distance `radius + 1` from every node.
9. `causalTreeHitTest.spec.ts::findNodeAt_TwoNodesWithinRadius_ReturnsCloserNode` places two nodes both within radius and asserts the nearer one is returned.
10. All Phase 1–6 integration tests pass.

**Dependencies:** TRC-P6-004, TRC-P6-005

## TRC-P6-007 — CausalTreeView Vue component

**Design:** [tracer_phase6_design.md §9](./tracer_phase6_design.md#7-frontend-rendering)

Three-column SPA view (`CausalTreeView.vue`) composing `TraceSummaryPanel.vue` on the left, `CausalTreeCanvas.vue` in the center, and the Phase 5 `EventInspector` on the right when a node is selected. `CausalTreeCanvas.vue` owns pan/zoom pointer-event handling (drag to pan, wheel to zoom with cursor-fixed scaling), triggers re-layout when the tree prop changes, and emits a `select` event on node click. `TraceSummaryPanel.vue` renders trace ID, total span, root/leaf counts, a color-keyed participating-node list, and a truncation warning when `summary.truncated` is true. `TraceNodeTooltip.vue` appears on node hover showing topic, publisher node, and publish time. `TraceSearchInput.vue` in the header accepts a 16-char hex ID with an "Event"/"Trace" kind toggle and routes via `vue-router`. A loading spinner occupies the canvas area until the first tree arrives; a retry-capable error message is shown on fetch failure. The whole view loads in under 300 ms for traces under 100 events.

**Success conditions:**
1. `CausalTreeView.spec.ts::renders_LoadingSpinner_WhenStoreIsLoadingAndNoTree` mounts the view with `store.loading = true, store.tree = null` and asserts `.loading-spinner` is visible and `.causal-tree-canvas` is absent.
2. `CausalTreeView.spec.ts::renders_ErrorMessage_WithRetryButton_WhenStoreHasError` mounts with `store.error = 'timeout'` and asserts an element with `data-testid="error-message"` is visible and contains a "Retry" button that calls `store.retry` when clicked.
3. `CausalTreeView.spec.ts::renders_ThreeColumnGrid_WhenTreeLoadedAndNodeSelected` mounts with a seeded `store.tree` and `store.selectedEventId`, and asserts `.causal-tree-view__summary`, `.causal-tree-view__canvas`, and `.causal-tree-view__inspector` are all visible.
4. `CausalTreeView.spec.ts::renders_TwoColumnGrid_WhenTreeLoadedAndNoNodeSelected` mounts with a seeded `store.tree` and `store.selectedEventId = null`, and asserts `.causal-tree-view__inspector` is absent.
5. `CausalTreeView.spec.ts::renders_EmptyPrompt_WhenNoTreeAndNotLoading` mounts with `store.tree = null, store.loading = false, store.error = null` and asserts `.causal-tree-view__empty` is visible.
6. `TraceSummaryPanel.spec.ts::renders_TruncationNotice_WhenSummaryTruncatedIsTrue` mounts with `summary.truncated = true` and `summary.totalEventsAvailable = 6000` and asserts `.trace-summary__truncation-notice` is visible containing the number 6000.
7. `TraceSummaryPanel.spec.ts::renders_NodeList_WithBorderColorMatchingNodeColorMap` mounts with two participating nodes and asserts each `.trace-summary__node` element has an inline `borderColor` style matching the `buildNodeColorMap` output for that node name.
8. `TraceSearchInput.spec.ts::submit_WithValidEventHex_NavigatesToCausalByEventRoute` fills the input with a valid 16-char hex and kind "event", submits, and asserts `router.push` was called with `{ name: 'causal-by-event', params: { eventId: ... } }`.
9. `TraceSearchInput.spec.ts::submit_WithNonHexValue_DisplaysValidationError` fills the input with `'zzzzzzzzzzzzzzzz'` and submits, and asserts `.trace-search__error` is visible and no navigation occurs.
10. All Phase 1–6 integration tests pass.

**Dependencies:** TRC-P6-006, TRC-P6-008

---

## TRC-P6-008 — Causal tree composables and store

**Design:** [tracer_phase6_design.md §10](./tracer_phase6_design.md#8-stores-composables-and-url-binding)

Implements `causalTreeStore.ts` (Pinia store with state `request`, `tree`, `loading`, `error`, `selectedEventId`; actions `openTrace`, `openByEvent`, `openAncestors`, `openDescendants`, `selectEvent`, `setResult`, `setError`, `clear`, `retry`), `useCausalTreeQuery.ts` (watches `store.request` and drives API calls with per-request `AbortController` cancellation so a new request cancels the prior in-flight fetch), `useCausalTreeLayout.ts` (wraps the pure `layout()` function in a `watchEffect` so the reactive `LayoutResult` ref updates when `store.tree` changes), and `causalTree.ts` (TypeScript type definitions mirroring all four backend DTOs: `TraceTreeDto`, `TraceNodeDto`, `TraceEdgeDto`, `TraceSummaryDto`). `setResult` auto-selects a notable node or the first event if `selectedEventId` is null or no longer in the returned tree.

**Success conditions:**
1. `causalTreeStore.spec.ts::openTrace_SetsRequestKindTraceAndClearsTree` calls `openTrace('abc0123456789def')` and asserts `store.request.kind === 'trace'`, `store.request.id === 'abc0123456789def'`, and `store.tree === null`.
2. `causalTreeStore.spec.ts::setResult_WhenSelectedIdNotInTree_SelectsFirstNotableNode` calls `setResult(treeWithOneNotableNode)` with `store.selectedEventId = 'nonexistent'` and asserts `store.selectedEventId` equals the notable node's `eventId`.
3. `causalTreeStore.spec.ts::setResult_WhenNoNotableNodes_SelectsFirstNode` calls `setResult(treeWithNoNotables)` with `store.selectedEventId = null` and asserts `store.selectedEventId === tree.nodes[0].eventId`.
4. `causalTreeStore.spec.ts::retry_ReassignsRequest_TriggeringWatcher` calls `openTrace('abc')`, then `retry()`, and asserts `store.request` is a new object reference (watcher fires again).
5. `useCausalTreeQuery.spec.ts::requestKindTrace_CallsGetTraceTree` sets `store.request = { kind: 'trace', id: 'abc', maxEvents: 1000 }` and asserts `api.getTraceTree` is called with `('abc', 1000, ...)`.
6. `useCausalTreeQuery.spec.ts::requestKindAncestors_CallsGetEventAncestors` sets `store.request = { kind: 'ancestors', id: 'def', maxDepth: 50 }` and asserts `api.getEventAncestors` is called with `('def', 50, ...)`.
7. `useCausalTreeQuery.spec.ts::secondRequest_AbortsFirst_BeforeFirstResolves` starts a first request via a delayed mock and fires a second before it resolves, and asserts the first `AbortController.abort` was called.
8. `useCausalTreeQuery.spec.ts::abortError_DoesNotSetStoreError` resolves the in-flight fetch with an `AbortError` and asserts `store.error` remains `null`.
9. `useCausalTreeLayout.spec.ts::layoutUpdates_WhenTreePropChanges` sets `store.tree` to a 5-node tree and asserts `layoutResult.value.nodes.size === 5`, then sets a 10-node tree and asserts `layoutResult.value.nodes.size === 10`.
10. All Phase 1–6 integration tests pass.

**Dependencies:** TRC-P6-005, TRC-P6-003

---

## TRC-P6-009 — Cross-view navigation

**Design:** [tracer_phase6_design.md §11](./tracer_phase6_design.md#9-cross-view-navigation)

Enables two-way pivot wiring across Phase 3–6 views. In `CausalTreeView`, `EventInspector` receives `showCausalTreePivot = false` (prevents self-loop) and `sessionId` resolved from `store.tree.sessionId`; clicking "Show in timeline" pushes to `/v/timeline/{sessionId}?from=...&to=...&select={eventId}` with a ±2 s window around the event's publish time; clicking "Show in scenario" pushes to `/scenario/{sessionId}`. In Phase 5 `TimelineView`, the previously-disabled "Show causal tree" button in `EventInspector` is enabled, pushing to `/v/causal/{eventId}`. Events with `traceId = '0000000000000000'` unconditionally hide the "Show causal tree" button. The backend `TraceQueryService` resolves `SessionId` (the session whose time window contains the trace's first event) and includes it in `TraceTreeDto`; `TraceDtoMapper` projects it.

**Success conditions:**
1. `EventInspector.spec.ts::showCausalTreeButton_HiddenWhenTraceIdIsZero` mounts with `event.traceId = '0000000000000000'` and `showCausalTreePivot = true`, and asserts no "Show causal tree" button is rendered.
2. `EventInspector.spec.ts::showCausalTreeButton_VisibleAndNavigates_WhenTraceIdNonZero` mounts with a non-zero `traceId` and `showCausalTreePivot = true`, clicks the button, and asserts `router.push` was called with `{ name: 'causal-by-event', params: { eventId: event.eventId } }`.
3. `EventInspector.spec.ts::pivotToTimeline_PushesTimelineRouteWithSelectAndWindow` mounts in causal-tree context with a known `sessionId` and `publishWallclock`, clicks "Show in timeline", and asserts `router.push` was called with `name: 'timeline'`, the correct `sessionId` param, and `query.select` equal to the event's ID.
4. `EventInspector.spec.ts::pivotToScenario_PushesScenarioRouteWithSessionId` clicks "Show in scenario" and asserts `router.push` was called with `{ name: 'scenario', params: { sessionId: ... } }`.
5. `EventInspector.spec.ts::showTimelinePivotFalse_HidesTimelineButton` mounts with `showTimelinePivot = false` and asserts "Show in timeline" is absent.
6. `TraceQueryServiceTests.GetTraceTree_SessionIdResolved_MatchesSessionContainingFirstEvent` asserts the returned `TraceTree.SessionId` equals the ID of the session whose time window contains `Summary.FirstEventUtc`.
7. `TraceDtoMapperTests.MapTraceTree_SessionIdPresentInDto` asserts `TraceTreeDto.SessionId` is a non-empty string equal to the source `TraceTree.SessionId`.
8. All Phase 1–6 integration tests pass.

**Dependencies:** TRC-P6-007, TRC-P6-004

---

## TRC-P6-010 — Shareable URL for causal view

**Design:** [tracer_phase6_design.md §12](./tracer_phase6_design.md#83-url-patterns)

Registers two lazy-loaded Vue Router routes — `{ path: '/v/trace/:traceId', name: 'causal-by-trace' }` and `{ path: '/v/causal/:eventId', name: 'causal-by-event' }` — both resolving to `CausalTreeView.vue`. `useCausalTreeUrl.ts` reads route params on mount and on every subsequent route change, dispatching `openTrace` / `openByEvent` / `openAncestors` / `openDescendants` based on the param and the optional `?mode`, `?maxDepth`, `?maxNodes`, `?maxEvents` query params; invalid or absent numeric params fall back to store-action defaults. When the user selects a node the composable debounce-writes `?select={eventId}` into the URL via `router.replace` (not `push`) so the browser back-button stack is not polluted. Navigating to the URL on any machine with access to the data restores the same view state.

**Success conditions:**
1. `useCausalTreeUrl.spec.ts::causalByEvent_NoMode_CallsOpenByEvent` simulates route `{ name: 'causal-by-event', params: { eventId: 'aabbccddeeff0011' } }` and asserts `store.openByEvent` is called with `'aabbccddeeff0011'`.
2. `useCausalTreeUrl.spec.ts::causalByEvent_ModeAncestors_CallsOpenAncestorsWithMaxDepth` simulates the route with `query.mode = 'ancestors'` and `query.maxDepth = '20'`, and asserts `store.openAncestors('aabbccddeeff0011', 20)` is called.
3. `useCausalTreeUrl.spec.ts::causalByEvent_ModeDescendants_CallsOpenDescendantsWithParsedParams` simulates `query.mode = 'descendants'`, `query.maxDepth = '15'`, `query.maxNodes = '300'`, and asserts `store.openDescendants(eventId, 15, 300)`.
4. `useCausalTreeUrl.spec.ts::causalByTrace_CallsOpenTrace` simulates `{ name: 'causal-by-trace', params: { traceId: '1122334455667788' } }` and asserts `store.openTrace('1122334455667788')`.
5. `useCausalTreeUrl.spec.ts::causalByTrace_WithSelectParam_SetsSelectedEventId` simulates the trace route with `query.select = 'ffff000011112222'` and asserts `store.selectedEventId === 'ffff000011112222'` after the watcher runs.
6. `useCausalTreeUrl.spec.ts::selectEventId_WritesSelectQueryParamViaRouterReplace` sets `store.selectedEventId = 'ffff000011112222'` after the composable is mounted and asserts `router.replace` (not `router.push`) is called with `query.select = 'ffff000011112222'` after the debounce interval elapses.
7. `router.spec.ts::causalByEventRoute_IsLazyLoaded` asserts the route's `component` property is a function (dynamic import) and not a statically-imported component reference.
8. All Phase 1–6 integration tests pass.

**Dependencies:** TRC-P6-008, TRC-P6-007

---

## TRC-P6-011 — Backend unit and integration tests

**Design:** [tracer_phase6_design.md §13](./tracer_phase6_design.md#111-backend-unit-tests)

Implements the full backend test suite across four files. `TraceQueryServiceTests.cs` covers normal/truncated/cross-interval/singleton-trace/convergent-DAG cases. `TraceWalkerTests.cs` covers ancestor depth-limit, root-edge stopping, cycle-guard termination, BFS descendant order, `maxNodes` truncation, and batched `IN`-clause child fetch (asserts exactly one SQL query per BFS level). `TraceEndpointsTests.cs` covers 200/400/404 status codes, parameter clamping, and round-trip timing assertions for all five endpoints. `CausalTreeRoundTripTests.cs` seeds a multi-event trace, queries the live observer, builds an offline bundle, queries the offline viewer, and asserts structural identity of both responses.

**Success conditions:**
1. `TraceQueryServiceTests.GetTraceTree_LinearChainOf5_Returns5Nodes4Edges` inserts 5 events as a parent chain on one `trace_id` and asserts `Nodes.Count == 5`, `Edges.Count == 4`, `Summary.RootCount == 1`, `Summary.LeafCount == 1`.
2. `TraceQueryServiceTests.GetTraceTree_ConvergentDag_BothParentEdgesPresent` inserts events A → C and B → C and asserts `Edges.Count == 2`, `Nodes.Count == 3`, and no duplicate node appears in `Nodes`.
3. `TraceQueryServiceTests.GetTraceTree_CrossIntervalTrace_AllNodesReturnedWithCrossRotationEdges` rotates the interval after 5 events, writes 5 more on the same `trace_id`, and asserts the tree contains all 10 nodes with all 9 edges intact.
4. `TraceWalkerTests.WalkAncestors_CycleInParentPointers_TerminatesWithoutException` constructs a synthetic parent cycle, calls `WalkAncestorsAsync` with `maxDepth = 1000`, and asserts it returns within 1 000 ms and throws no exception.
5. `TraceWalkerTests.WalkDescendants_100Children_IssuesSingleBatchedQuery` instruments the connection to count SQL statements, inserts 100 children of one parent, and asserts exactly 1 SQL statement was issued by the first BFS level (not 100 individual lookups).
6. `TraceEndpointsTests.GetTraceTree_InvalidHexId_Returns400WithProblemDetails` sends a non-hex trace ID and asserts HTTP 400 and a response body deserializable as `ProblemDetails` with `status == 400`.
7. `TraceEndpointsTests.GetTraceTree_Under50Events_RespondsBefore300ms` seeds 50 events, measures round-trip with `Stopwatch`, and fails if elapsed ≥ 300 ms.
8. `TraceEndpointsTests.GetAncestors_10DeepChain_RespondsBefore200ms` seeds a 10-deep ancestor chain and asserts round-trip under 200 ms.
9. `CausalTreeRoundTripTests.LiveAndBundleResponses_AreStructurallyIdentical` pushes a 30-event trace, queries via the live observer, builds a bundle from the same data, queries via the offline viewer, and asserts `nodes`, `edges`, `rootEventIds`, and `leafEventIds` are identical in both responses.
10. All Phase 1–6 integration tests pass.

**Dependencies:** TRC-P6-004, TRC-P6-003, TRC-P6-002, TRC-P6-001

---

## TRC-P6-012 — Frontend tests

**Design:** [tracer_phase6_design.md §14](./tracer_phase6_design.md#113-frontend-unit-tests-vitest)

Completes the frontend test suite with four Vitest unit-test files and one Playwright E2E spec. `causalTreeLayout.spec.ts` extends the cases from TRC-P6-005 success conditions with cycle-defense and multi-root no-duplicate assertions. `causalTreeRenderer.spec.ts` and `causalTreeHitTest.spec.ts` extend TRC-P6-006 success conditions with boundary and nearest-node cases. `useCausalTreeQuery.spec.ts` parameterizes all four request kinds and verifies abort-on-replace and abort-error swallowing. The Playwright spec `causal-tree-view.spec.ts` validates the full cross-view pivot flow: load from timeline `EventInspector`, walk expansion latency under 200 ms, and pivot back to timeline with correct URL.

**Success conditions:**
1. `causalTreeLayout.spec.ts::layout_CycleDefense_ReturnsWithoutHanging` constructs a tree with a fabricated parent cycle and asserts `layout()` returns within 1 000 ms (measured with `performance.now()`) and produces a `LayoutResult` with no duplicate `eventId` keys.
2. `causalTreeLayout.spec.ts::layout_MultiRootDag_EachNodeAppearsExactlyOnce` constructs a 3-root 10-node DAG with two convergent children and asserts `result.nodes.size === 10`.
3. `causalTreeHitTest.spec.ts::findNodeAt_ClickAtRadiusMinusOne_StillReturnsNode` verifies that a query point at distance `radius - 1` from the node center returns the node (inclusive boundary).
4. `useCausalTreeQuery.spec.ts::allFourKinds_EachDispatchesCorrectApiMethod` parameterizes over `{ kind: 'trace', method: 'getTraceTree' }`, `{ kind: 'event', method: 'getTraceByEvent' }`, `{ kind: 'ancestors', method: 'getEventAncestors' }`, `{ kind: 'descendants', method: 'getEventDescendants' }` and asserts each fires exactly its corresponding API method once.
5. `useCausalTreeUrl.spec.ts::routeChange_ModeDescendants_CallsOpenDescendants` simulates `?mode=descendants&maxDepth=15&maxNodes=300` and asserts `store.openDescendants(eventId, 15, 300)`.
6. `causal-tree-view.spec.ts::opensFromTimelineEventInspectorPivot` navigates to the timeline, clicks a canvas marker, waits for `.event-inspector`, clicks "Show causal tree", and asserts navigation to a URL matching `/v/causal/` with `.trace-summary` visible within 300 ms of navigation.
7. `causal-tree-view.spec.ts::walkExpansion_ClickNodeInTree_Under200ms` clicks a known canvas node on a seeded trace, measures elapsed from click until `.event-inspector` shows the node's event ID with `performance.now()`, and asserts elapsed < 200 ms.
8. `causal-tree-view.spec.ts::crossViewPivotToTimeline_NavigatesWithSelectParam` clicks "Show in timeline" in the causal-tree inspector and asserts navigation to a URL matching `/v/timeline/` with a `select` query parameter equal to the event ID.
9. All Phase 1–6 integration tests pass.

**Dependencies:** TRC-P6-007, TRC-P6-008, TRC-P6-009, TRC-P6-010, TRC-P6-011

<!-- PHASE 6 TASKS END -->

<!-- PHASE 7 TASKS BEGIN -->

# Phase 7 — Entity History View, Slow State Time Series, Fast State Drill-Down

**Design:** [tracer_phase7_design.md](./tracer_phase7_design.md)  
**Scope:** [§1 — Phase 7 Scope and Goals](./tracer_phase7_design.md#1-phase-7-scope-and-goals)  
**Architecture:** [tracer_architecture_v1.md §18](./tracer_architecture_v1.md#18-build-sequence)

---

## TRC-P7-001 — Tracer.Storage.Parquet Assembly

**Phase:** 7 — Entity History View, Slow State Time Series, Fast State Drill-Down  
**Design reference:** [tracer_phase7_design.md §4.4](./tracer_phase7_design.md#44-fast-state-parquet-reader)  
**Architecture reference:** [tracer_architecture_v1.md §5](./tracer_architecture_v1.md#5-data-categories) *(fast state lives in Parquet, queried only on demand — phase 7 is the first user-facing code to exercise this path)*

### Scope

**In scope:**
- New `Tracer.Storage.Parquet` project added to `Tracer.sln`; project references `Tracer.Core` and `DuckDB.NET.Data` (already in `Directory.Packages.props`)
- `ParquetReader` class with `InspectSchemaAsync(string parquetPath, CancellationToken)` → `ParquetSchema`
- `ParquetReader.ReadTimeSeriesAsync(string parquetPath, string entityId, IReadOnlyList<string> columns, WallclockTime from, WallclockTime to, int maxSamples, CancellationToken)` → `ParquetTimeSeriesResult`
- `ParquetReader.ReadTimeSeriesAsync(IReadOnlyList<string> parquetPaths, ...)` overload for multi-file queries using `read_parquet([...])` list syntax
- Per-call in-memory DuckDB connections: every method call opens `new DuckDBConnection("Data Source=:memory:")`, uses it, and disposes it — no shared or pooled connection
- Stride downsampling: count rows first, compute `stride = totalSamples / maxSamples`, then use `ROW_NUMBER() OVER (ORDER BY publish_wallclock)` and `WHERE (rn - 1) % $stride = 0`
- Numeric type coercion: all DuckDB integer and float types (`TINYINT`, `SMALLINT`, `INTEGER`, `BIGINT`, `HUGEINT`, `UTINYINT`, `USMALLINT`, `UINTEGER`, `UBIGINT`, `FLOAT`, `DOUBLE`, `DECIMAL`) coerce to `double?` in `ParquetSample.Values`; conversion failure stores `null`
- `SafeColumnIdentifier(string name)` wraps column names in double-quotes, escaping internal `"` as `""`, to prevent SQL injection on column names
- `EscapeSql(string s)` doubles single quotes for Parquet path interpolation
- `IsNumeric(string duckType)` helper returns `true` for numeric DuckDB types, `false` otherwise
- Result records: `ParquetColumn(Name, DuckType, IsNumeric)`, `ParquetSchema(Path, Columns)`, `ParquetSample(PublishWallclock, Values)`, `ParquetTimeSeriesResult { Columns, Samples, TotalSamples, Downsampled }`
- Unit tests: `Tracer.Tests.Unit/Parquet/ParquetReaderTests.cs`, `ParquetSchemaInspectorTests.cs`

**Out of scope:**
- Pooled or persistent DuckDB connections for Parquet queries
- LTTB or M4 downsampling algorithms (deferred to Phase 10+)
- Writing Parquet files (Phase 2 writer is unchanged)
- Reading string/categorical columns into chart data (only numeric columns are considered by the column picker)
- Any dependency on `Tracer.Storage.DuckDB`, `Tracer.WebApi`, or `Tracer.Adapters.Mock`

### Constraints

- Every public method must open and dispose its own `DuckDBConnection("Data Source=:memory:")` — no instance-level connection field
- Column names must always pass through `SafeColumnIdentifier` before being included in SQL; they must never be raw-concatenated
- File paths must always pass through `EscapeSql` before being included in `read_parquet(...)` SQL
- The multi-file overload builds `read_parquet(['path1','path2',...])` with each path individually escaped
- No new `PackageReference` entries needed; `DuckDB.NET.Data` is already in `Directory.Packages.props`
- No dependency on any project other than `Tracer.Core`

### Success Conditions

1. **Test: ProjectAdded_BuildsClean** — Setup: add `Tracer.Storage.Parquet.csproj` to `Tracer.sln` with the correct project reference to `Tracer.Core`. Action: `dotnet build Tracer.sln --configuration Release`. Assert: zero errors and zero warnings.
2. **Test: InspectSchemaAsync_ThreeColumnParquet_ReturnsAllColumns** — Setup: create a temp Parquet file with columns `publish_wallclock` (TIMESTAMP), `instance_key` (VARCHAR), `x` (FLOAT) via DuckDB `COPY`. Action: `InspectSchemaAsync(path, ct)`. Assert: `schema.Columns.Count == 3`; the entry for `"x"` has `IsNumeric == true`; the entry for `"instance_key"` has `IsNumeric == false`.
3. **Test: InspectSchemaAsync_NonExistentPath_PropagatesException** — Action: call `InspectSchemaAsync("nonexistent_file.parquet", ct)`. Assert: an exception is thrown (DuckDB IO error); no `ParquetSchema` is returned.
4. **Test: ReadTimeSeriesAsync_NarrowTimeRange_ReturnsEmpty** — Setup: write 10 samples between T=100 and T=200. Action: `ReadTimeSeriesAsync(path, "ent-A", ["x"], from: T=300, to: T=400, maxSamples: 5000, ct)`. Assert: `result.TotalSamples == 0`; `result.Samples` is empty; `result.Downsampled == false`.
5. **Test: ReadTimeSeriesAsync_BelowMaxSamples_NoDownsampling** — Setup: write 50 samples for `"ent-A"`. Action: `ReadTimeSeriesAsync` with `maxSamples: 100`. Assert: `result.Samples.Count == 50`; `result.Downsampled == false`; `result.TotalSamples == 50`.
6. **Test: ReadTimeSeriesAsync_AboveMaxSamples_StridedDownsampling** — Setup: write 1000 samples. Action: `ReadTimeSeriesAsync` with `maxSamples: 100`. Assert: `result.Downsampled == true`; `result.Samples.Count <= 100`; `result.TotalSamples == 1000`; `result.Samples` are ordered by `PublishWallclock` ascending.
7. **Test: ReadTimeSeriesAsync_MultipleFiles_MergesRows** — Setup: write 50 samples to `fileA` and 50 to `fileB` for the same entity. Action: call the multi-file overload with `[fileA, fileB]`. Assert: `result.TotalSamples == 100`; all returned samples are ordered by `PublishWallclock`.
8. **Test: ReadTimeSeriesAsync_NullNumericValue_CoercedToNull** — Setup: write a sample that stores a DuckDB NULL in column `x`. Action: read it. Assert: `sample.Values["x"] == null`.
9. **Test: SafeColumnIdentifier_EmbeddedDoubleQuote_Escaped** — Direct unit: `SafeColumnIdentifier("col\"name")` returns `"\"col\"\"name\""` (the internal double-quote is escaped as `""`), verifying that embedding this in SQL cannot break out of the identifier.
10. **Test: EscapeSql_SingleQuoteInPath_Doubled** — Direct unit: `EscapeSql("a'b")` returns `"a''b"`.

---

## TRC-P7-002 — Schema Extension: slow_state Entity-Time Index

**Phase:** 7 — Entity History View, Slow State Time Series, Fast State Drill-Down  
**Design reference:** [tracer_phase7_design.md §3.1](./tracer_phase7_design.md#31-slow-state-entity_id-index)  
**Architecture reference:** [tracer_architecture_v1.md §5](./tracer_architecture_v1.md#5-data-categories) *(slow state is DuckDB-resident; entity-id + time-range lookups need an index)*

### Scope

**In scope:**
- Appending `idx_slow_state_entity_time` to `SchemaV1.CreateIndexes` in `Tracer.Storage.DuckDB` under a `-- Phase 7` comment block
- Index SQL exactly as in §3.1: `CREATE INDEX IF NOT EXISTS idx_slow_state_entity_time ON slow_state (entity_id, publish_wallclock) WHERE entity_id IS NOT NULL;`
- Updating the existing `AllIndexes_AreCreated` unit test (or adding a companion test) to assert the new index name is present after schema creation

**Out of scope:**
- Migration of pre-existing `.db` files (documented non-migration policy: pre-existing intervals run without the index until evicted)
- Any change to table columns, other index definitions, or `_schema_meta` version
- A runtime migration path or schema version bump

### Constraints

- The new SQL must appear after the Phase 6 `idx_events_parent_event_id` block, under a `-- Phase 7` comment, exactly matching the string shown in §3.1
- `SchemaV1.CreateIndexes` remains a single `const string`; do not split it
- `CREATE INDEX IF NOT EXISTS` ensures idempotency; running `CreateIndexes` twice on the same database must not throw
- `SchemaV1.Version` must remain `1` — index additions do not require a version bump

### Success Conditions

1. **Test: AllIndexes_AreCreated_IncludesSlowStateEntityTimeIndex** — Setup: call `DuckDbWriter.CreateAsync(tempPath, ...)` and dispose. Action: open a raw DuckDB connection to the same file, run `SELECT index_name FROM duckdb_indexes() WHERE table_name = 'slow_state'`. Assert: exactly one row returned with `index_name = 'idx_slow_state_entity_time'`.
2. **Test: CreateIndexes_IsIdempotent_SlowStateIndex** — Setup: execute `SchemaV1.CreateIndexes` SQL twice on the same in-memory DuckDB connection. Assert: no exception on the second execution; `duckdb_indexes()` shows the index exactly once.
3. **Test: SchemaV1_CreateIndexes_ContainsPhase7CommentBlock** — Assert: the `SchemaV1.CreateIndexes` string contains the literal substring `-- Phase 7`.
4. **Test: SlowStateEntityQuery_WithIndex_CompletesUnder200ms** — Setup: write 50,000 slow-state rows for 10 distinct entity IDs to a temp `.db` file (so the index is created). Action: query `SELECT * FROM slow_state WHERE entity_id = 'entity-1' AND publish_wallclock >= $t1 AND publish_wallclock < $t2` using a `Stopwatch`. Assert: elapsed < 200 ms; only rows for `'entity-1'` are returned.

---

## TRC-P7-003 — EntityDiscoveryService

**Phase:** 7 — Entity History View, Slow State Time Series, Fast State Drill-Down  
**Design reference:** [tracer_phase7_design.md §4.1](./tracer_phase7_design.md#41-entity-discovery)  
**Architecture reference:** [tracer_architecture_v1.md §17](./tracer_architecture_v1.md#17-performance-targets) *(entity-events query < 200 ms target for a 30-min session)*

### Scope

**In scope:**
- `EntityDiscoveryService` class in `Tracer.WebApi.Queries`
- Constructor accepting `LiveMultiIntervalReader` and `ILogger<EntityDiscoveryService>`
- `DiscoverAsync(string sessionId, WallclockTime sessionStart, WallclockTime sessionEnd, string? topicFilter, string? playerFilter, int limit, CancellationToken)` → `IReadOnlyList<EntitySummary>`
- SQL pattern: call `BuildEventsUnionSql` on the acquired connection, then wrap in a CTE and aggregate with `GROUP BY entity_id`, `MIN`/`MAX` for time bounds, `COUNT(*)` for event count, `ANY_VALUE(owning_player_id)` for representative player, `ARRAY_AGG(DISTINCT topic ORDER BY topic)` for topics list
- Optional `topicFilter` and `playerFilter` applied as outer-query WHERE parameters (named `$topicFilter`, `$playerFilter`)
- `ReadStringList(DbDataReader, int)` private helper to extract `ARRAY_AGG` result into `IReadOnlyList<string>`
- `EntitySummary` record: `EntityId`, `FirstSeenUtc`, `LastSeenUtc`, `EventCount`, `SamplePlayerId`, `Topics`
- Unit tests in `Tracer.Tests.Unit/WebApi/EntityDiscoveryServiceTests.cs`

**Out of scope:**
- Pagination beyond the `limit` parameter (no cursor or offset)
- Cross-session queries
- Caching of discovery results
- Entity discovery from Parquet (events table is sufficient)

### Constraints

- `topicFilter` and `playerFilter` must be SQL parameters, never string-interpolated into the query
- Must call `BuildEventsUnionSql` — cannot reference individual interval tables directly
- `ANY_VALUE(owning_player_id)` not `MIN` — represents any observed player, not the lexicographic minimum (per §4.1 rationale)
- `ORDER BY event_count DESC` so most-active entities rank first
- `limit` is clamped to 1–5000 at the `EntityEndpoints` call site, not inside this service

### Success Conditions

1. **Test: DiscoverAsync_ThreeEntities_ReturnedOrderedByEventCount** — Setup: write 20 events for `"ent-A"`, 10 for `"ent-B"`, 5 for `"ent-C"`, and 5 with `entity_id = null`. Action: `DiscoverAsync(sessionId, start, end, null, null, 100, ct)`. Assert: result has 3 entries (null-entity rows excluded); first entry is `EntityId = "ent-A"` with `EventCount = 20`.
2. **Test: DiscoverAsync_TopicFilter_ExcludesOtherEntities** — Setup: 10 events for `"ent-A"` on topic `"pos"`, 10 for `"ent-B"` on topic `"vel"`. Action: `DiscoverAsync(..., topicFilter: "pos", ...)`. Assert: result contains only `"ent-A"`.
3. **Test: DiscoverAsync_PlayerFilter_ExcludesOtherEntities** — Setup: 10 events for `"ent-A"` with `owning_player_id = "p1"`, 10 for `"ent-B"` with `owning_player_id = "p2"`. Action: `DiscoverAsync(..., playerFilter: "p1", ...)`. Assert: result contains only `"ent-A"`.
4. **Test: DiscoverAsync_FirstAndLastSeen_CorrectBounds** — Setup: 3 events for `"ent-X"` at wallclock times T1 < T2 < T3. Assert: `FirstSeenUtc` corresponds to T1; `LastSeenUtc` corresponds to T3.
5. **Test: DiscoverAsync_TopicsArray_DeduplicatedAndSorted** — Setup: `"ent-A"` emits events on topics `"b"`, `"a"`, `"a"` (two on `"a"`). Assert: `EntitySummary.Topics` equals `["a", "b"]` exactly.
6. **Test: DiscoverAsync_EmptySession_ReturnsEmptyList** — Action: `DiscoverAsync` against a session with no events. Assert: returns empty list; no exception.
7. **Test: DiscoverAsync_LimitRespected_ReturnsTruncatedCount** — Setup: 10 distinct entities. Action: `DiscoverAsync(..., limit: 3, ...)`. Assert: result has exactly 3 entries.
8. **Test: DiscoverAsync_TopicFilterSqlInjection_IsParameterized** — Action: `topicFilter = "'; DROP TABLE events; --"`. Assert: call completes without exception; the `events` table still exists in the DuckDB connection.

---

## TRC-P7-004 — EntityEventsService

**Phase:** 7 — Entity History View, Slow State Time Series, Fast State Drill-Down  
**Design reference:** [tracer_phase7_design.md §4.2](./tracer_phase7_design.md#42-entity-events-service)  
**Architecture reference:** [tracer_architecture_v1.md §17](./tracer_architecture_v1.md#17-performance-targets) *(< 200 ms for ~5000 events in a 30-min entity history)*

### Scope

**In scope:**
- `EntityEventsService` class in `Tracer.WebApi.Queries`
- Constructor accepting `LiveMultiIntervalReader` and `ILogger<EntityEventsService>`
- `GetEventsAsync(string entityId, WallclockTime from, WallclockTime to, int limit, CancellationToken)` → `EntityEventsResult`
- SQL: `BuildEventsUnionSql` with `WHERE entity_id = $entityId AND publish_wallclock >= $from AND publish_wallclock < $to`, ordered by `publish_wallclock`, fetching `limit + 1` rows
- Truncation detection: if row count > limit, remove the last row and set `Truncated = true`
- Row mapping via the existing `EventRecordMapper.FromReader`
- `EntityEventsResult` record: `EntityId`, `Events` (`IReadOnlyList<EventRecord>`), `Truncated` (`bool`)
- Unit tests in `Tracer.Tests.Unit/WebApi/EntityEventsServiceTests.cs`

**Out of scope:**
- Server-side topic filtering on this endpoint (callers use `EntityDiscoveryService` to discover topics)
- Pagination cursor or offset
- Sorting by any column other than `publish_wallclock`

### Constraints

- `entityId` must be passed as `$entityId` parameter, never string-interpolated
- `from` and `to` must be typed `DateTimeOffset` parameters (not raw integers or strings)
- The query must issue `LIMIT $limit` where the bound is `limit + 1` (not `limit`) to enable truncation detection without a separate count query
- Must use `BuildEventsUnionSql` — cannot query a single interval table directly

### Success Conditions

1. **Test: GetEventsAsync_FiveEventsForEntity_ReturnsAll** — Setup: write 5 events for `"ent-A"` and 5 for `"ent-B"` in the same time range. Action: `GetEventsAsync("ent-A", from, to, limit: 100, ct)`. Assert: result has 5 events; all have `EntityId == "ent-A"`; `Truncated == false`.
2. **Test: GetEventsAsync_ExceedsLimit_TruncatesAndSetsFlag** — Setup: write 11 events for `"ent-A"`. Action: `GetEventsAsync("ent-A", from, to, limit: 10, ct)`. Assert: `result.Events.Count == 10`; `result.Truncated == true`.
3. **Test: GetEventsAsync_ExactlyAtLimit_NotTruncated** — Setup: write exactly 10 events. Action: `GetEventsAsync(..., limit: 10, ...)`. Assert: `result.Events.Count == 10`; `result.Truncated == false`.
4. **Test: GetEventsAsync_OrderedByWallclockAscending** — Setup: write 5 events inserted in non-chronological order. Assert: `result.Events` are sorted by `PublishWallclock` ascending.
5. **Test: GetEventsAsync_EntityNotFound_ReturnsEmptyNotTruncated** — Action: `GetEventsAsync("nonexistent-entity", from, to, 100, ct)`. Assert: `result.Events` is empty; `result.Truncated == false`; no exception.
6. **Test: GetEventsAsync_EmptyTimeRange_ReturnsEmpty** — Action: call with `from == to`. Assert: returns empty result without throwing.
7. **Test: GetEventsAsync_EntityIdIsParameter_NeverInterpolated** — Inspect the SQL string (expose via a testable builder overload or check via query plan): assert `entity_id = $entityId` appears in the SQL and the entity ID value does not appear as a literal string in the SQL text.

---

## TRC-P7-005 — EntitySlowStateService

**Phase:** 7 — Entity History View, Slow State Time Series, Fast State Drill-Down  
**Design reference:** [tracer_phase7_design.md §4.3](./tracer_phase7_design.md#43-entity-slow-state-service)  
**Architecture reference:** [tracer_architecture_v1.md §5](./tracer_architecture_v1.md#5-data-categories) *(slow state is DuckDB-resident and low-frequency)*

### Scope

**In scope:**
- `EntitySlowStateService` class in `Tracer.WebApi.Queries`
- Constructor accepting `LiveMultiIntervalReader` and `ILogger<EntitySlowStateService>`
- `GetAsync(string entityId, WallclockTime from, WallclockTime to, IReadOnlyList<string>? topicFilter, CancellationToken)` → `EntitySlowStateResult`
- SQL: `BuildSlowStateUnionSql` (TRC-P7-006) with `WHERE entity_id = $entityId AND publish_wallclock >= $from AND publish_wallclock < $to`; optional IN-clause for topic filter with named parameters `$topic0`, `$topic1`, ...
- Results grouped in-memory by topic into `IReadOnlyDictionary<string, IReadOnlyList<SlowStateSample>>`; dictionary keyed by topic name in sorted order
- `SlowStateSample` record: `Topic`, `PublishWallclock`, `PayloadJson`, `TraceId` (mapped as `ulong`; `0` when DB column is null or zero)
- `EntitySlowStateResult` record: `EntityId`, `ByTopic`
- Unit tests in `Tracer.Tests.Unit/WebApi/EntitySlowStateServiceTests.cs`

**Out of scope:**
- Fast-state queries (routed through `EntityFastStateService`)
- Downsampling of slow-state results (slow state is inherently low-frequency)
- Aggregation or statistics over slow-state payload values

### Constraints

- Depends on `BuildSlowStateUnionSql` (TRC-P7-006); this service must not be implemented until that method exists
- Topic filter values must be individually named SQL parameters `$topic0`, `$topic1`, ... — never string-interpolated
- `entityId` must be the named parameter `$entityId`
- `ByTopic` dictionary must have stable ordering: use `SortedDictionary` or sort by key before building the final dictionary

### Success Conditions

1. **Test: GetAsync_TwoTopics_ResultsGroupedCorrectly** — Setup: 5 slow-state rows for `"ent-A"` on topic `"pose"`, 3 on topic `"health"`. Action: `GetAsync("ent-A", from, to, null, ct)`. Assert: `result.ByTopic.Keys` equals `["health", "pose"]` (sorted); `result.ByTopic["pose"].Count == 5`; `result.ByTopic["health"].Count == 3`.
2. **Test: GetAsync_TopicFilter_ExcludesOtherTopics** — Setup: slow state on `"pose"` and `"health"`. Action: `GetAsync("ent-A", from, to, topicFilter: ["pose"], ct)`. Assert: `result.ByTopic` contains `"pose"` but not `"health"`.
3. **Test: GetAsync_SamplesOrderedByWallclockWithinTopic** — Setup: write slow-state rows for `"pose"` in non-chronological insertion order. Assert: `result.ByTopic["pose"]` is sorted by `PublishWallclock` ascending.
4. **Test: GetAsync_EntityNotFound_ReturnsEmptyDictionary** — Action: `GetAsync("nonexistent-entity", ...)`. Assert: `result.ByTopic` is empty (not null); no exception.
5. **Test: GetAsync_TraceIdZero_MappedAs0UL** — Setup: write a slow-state row with `trace_id = 0`. Assert: `sample.TraceId == 0UL`.
6. **Test: GetAsync_TopicFilterSqlInjection_IsParameterized** — Action: `topicFilter = ["'; DROP TABLE slow_state; --"]`. Assert: call completes without exception; `slow_state` table still exists.

---

## TRC-P7-006 — BuildSlowStateUnionSql Extension

**Phase:** 7 — Entity History View, Slow State Time Series, Fast State Drill-Down  
**Design reference:** [tracer_phase7_design.md §4.3](./tracer_phase7_design.md#43-entity-slow-state-service) *(BuildSlowStateUnionSql code snippet)*

### Scope

**In scope:**
- `BuildSlowStateUnionSql(string whereClause = "", string orderByClause = "", int? limit = null)` method added to the same class/type that owns `BuildEventsUnionSql` (the acquired-connection helper in `Tracer.Storage.DuckDB.MultiInterval` or `Tracer.WebApi`)
- Method generates `UNION ALL` across all attached interval databases, selecting from `{alias}.slow_state {whereClause}` in each
- Returns the sentinel `"SELECT NULL WHERE FALSE"` when there are no attachments (identical empty-case behavior to `BuildEventsUnionSql`)
- Unit tests verifying the generated SQL structure

**Out of scope:**
- Any modification to `BuildEventsUnionSql` or other existing builder methods
- Slow-state aggregation or topic-grouping SQL (that belongs in `EntitySlowStateService`)

### Constraints

- Method signature must match the `BuildEventsUnionSql` pattern exactly (same parameter names and default values, same empty-case sentinel)
- Each subquery arm must reference `{alias}.slow_state`, not `{alias}.events`
- Must be on the same type as `BuildEventsUnionSql` so `EntitySlowStateService` can call it through the same acquired-connection pattern

### Success Conditions

1. **Test: BuildSlowStateUnionSql_TwoAttachments_ProducesUnionAll** — Setup: construct an acquired-connection state with two interval aliases `iv0` and `iv1`. Action: `BuildSlowStateUnionSql()`. Assert: returned SQL contains both `FROM iv0.slow_state` and `FROM iv1.slow_state` joined with `UNION ALL`.
2. **Test: BuildSlowStateUnionSql_WhereClause_AppearsInBothArms** — Action: `BuildSlowStateUnionSql(whereClause: "WHERE entity_id = $eid")`. Assert: the WHERE clause text appears in both subquery arms.
3. **Test: BuildSlowStateUnionSql_NoAttachments_ReturnsSentinel** — Action: with zero attachments. Assert: return value equals `"SELECT NULL WHERE FALSE"` (exact string match, trimmed).
4. **Test: BuildSlowStateUnionSql_LimitSet_AppendsLimitClause** — Action: `BuildSlowStateUnionSql(limit: 500)`. Assert: returned SQL contains `LIMIT 500`.
5. **Test: BuildSlowStateUnionSql_DoesNotReferenceEventsTable** — Assert: the returned SQL from a non-empty case does not contain the substring `.events`.

---

## TRC-P7-007 — FastStateFileLocator

**Phase:** 7 — Entity History View, Slow State Time Series, Fast State Drill-Down  
**Design reference:** [tracer_phase7_design.md §4.5](./tracer_phase7_design.md#45-locating-fast-state-files)

### Scope

**In scope:**
- `FastStateFileLocator` class in `Tracer.WebApi.Queries`
- Constructor accepting `IntervalSetTracker` and optional `BundleOpenManager?`
- `LocateFiles(string topic, string entityId)` → `IReadOnlyList<string>`: returns full absolute paths of all `samples.parquet` files for the given (topic, entity) pair; each candidate is checked with `File.Exists` before inclusion
- `GetAvailableTopicsForEntity(string entityId)` → `IReadOnlyList<string>`: walks `fast_state/` subdirectories in all interval roots (and bundle working directory if present) to find topics that have a sub-folder matching the safe-encoded entity ID
- Live mode: iterates `IntervalSetTracker.CurrentSnapshot().Intervals`, building candidate path `{interval.Directory.RootPath}/fast_state/{safeTopic}/{safeEntity}/samples.parquet`
- Offline mode: if `BundleOpenManager.Current` is non-null, also checks `{bundle.WorkingDirectory}/fast_state/{safeTopic}/{safeEntity}/samples.parquet`
- Uses `BundleNaming.SafeFileName(topic)` and `BundleNaming.SafeFileName(entityId)` for directory-safe name encoding (same scheme as Phase 4 §3.1)
- Unit tests in `Tracer.Tests.Unit/WebApi/FastStateFileLocatorTests.cs` using temp directories

**Out of scope:**
- Glob-based or recursive directory scanning (explicit-list form as per §4.5)
- Caching of file-location results (re-scans snapshot on each call)
- Any DuckDB queries inside the locator

### Constraints

- Must call `BundleNaming.SafeFileName` for both topic and entity ID before constructing any path — raw strings may contain characters illegal in directory names (slashes, colons, etc.)
- Must use `File.Exists(candidate)` to verify existence before adding to results; must not throw if the `fast_state` directory for an interval does not exist
- `BundleOpenManager` is nullable; `LocateFiles` must handle `null` gracefully (live-only mode)
- `GetAvailableTopicsForEntity` must enumerate the file system, not query DuckDB

### Success Conditions

1. **Test: LocateFiles_LiveMode_TwoIntervals_ReturnsTwoPaths** — Setup: create two temp interval directories, each containing `fast_state/pos/ent-A/samples.parquet` (empty placeholder files). Action: construct `FastStateFileLocator` with a mocked `IntervalSetTracker` returning those two intervals; call `LocateFiles("pos", "ent-A")`. Assert: 2 paths returned; both end in `samples.parquet`.
2. **Test: LocateFiles_TopicAbsentInInterval_NotIncluded** — Setup: interval directory has `fast_state/vel/ent-A/samples.parquet` but not `fast_state/pos/ent-A/samples.parquet`. Action: `LocateFiles("pos", "ent-A")`. Assert: empty list returned.
3. **Test: LocateFiles_OfflineMode_FindsBundleFile** — Setup: no intervals; bundle working directory contains `fast_state/pos/ent-A/samples.parquet`. Action: construct with a `BundleOpenManager` whose `Current.WorkingDirectory` is that temp dir; call `LocateFiles("pos", "ent-A")`. Assert: exactly 1 path returned.
4. **Test: LocateFiles_TopicWithSlash_SafeFileNameApplied** — Setup: topic name is `"ns/topic"` (contains slash). Action: `LocateFiles("ns/topic", "ent-A")`. Assert: the constructed path uses `BundleNaming.SafeFileName("ns/topic")` as the directory component and does not contain a raw `/` after `fast_state/` in the path segment.
5. **Test: GetAvailableTopicsForEntity_MultipleTopicDirs_ReturnsAll** — Setup: temp interval dir with `fast_state/pos/ent-A/` and `fast_state/vel/ent-A/` directories (no file required). Action: `GetAvailableTopicsForEntity("ent-A")`. Assert: result includes both `"pos"` and `"vel"` (or their safe-decoded forms).
6. **Test: LocateFiles_FileDoesNotExist_DirectoryExists_NotIncluded** — Setup: interval directory has `fast_state/pos/ent-A/` directory but no `samples.parquet` file. Assert: that path is not included in `LocateFiles` results.
7. **Test: LocateFiles_NullBundleManager_LiveModeOnly_NoException** — Construct `FastStateFileLocator` with `bundleOpenManager: null`; call `LocateFiles`. Assert: no `NullReferenceException`.

---

## TRC-P7-008 — EntityFastStateService

**Phase:** 7 — Entity History View, Slow State Time Series, Fast State Drill-Down  
**Design reference:** [tracer_phase7_design.md §4.6](./tracer_phase7_design.md#46-entityfaststateservice)

### Scope

**In scope:**
- `EntityFastStateService` class in `Tracer.WebApi.Queries`
- Constructor accepting `ParquetReader`, `FastStateFileLocator`, `ILogger<EntityFastStateService>`
- `GetAvailableTopics(string entityId)` → `IReadOnlyList<string>`: delegates to `FastStateFileLocator.GetAvailableTopicsForEntity`
- `GetSchemaAsync(string entityId, string topic, CancellationToken)` → `FastStateTopicSchema?`: locates files via `FastStateFileLocator.LocateFiles`; returns `null` if no files found; otherwise calls `ParquetReader.InspectSchemaAsync` on the first file and filters out `publish_wallclock` and `instance_key` columns from the result
- `ReadAsync(string entityId, string topic, IReadOnlyList<string> columns, WallclockTime from, WallclockTime to, int maxSamples, CancellationToken)` → `EntityFastStateResult`: locates all files via `FastStateFileLocator.LocateFiles`, calls `ParquetReader.ReadTimeSeriesAsync(IReadOnlyList<string>, ...)` multi-file overload, maps the result
- `FastStateTopicSchema` record: `EntityId`, `Topic`, `Columns` (list of `ParquetColumn` excluding infrastructure columns)
- `EntityFastStateResult` record: `EntityId`, `Topic`, `Columns`, `Samples`, `TotalSamples`, `Downsampled`
- Unit tests in `Tracer.Tests.Unit/WebApi/EntityFastStateServiceTests.cs`

**Out of scope:**
- Column validation beyond what DuckDB enforces at query time
- Caching of schema or file-location results
- String/categorical column support in chart queries

### Constraints

- `GetSchemaAsync` must return `null` (not throw) when `LocateFiles` returns an empty list
- `ReadAsync` must return an `EntityFastStateResult` with empty `Samples` and `TotalSamples == 0` (not throw) when no files are found
- Schema columns returned by `GetSchemaAsync` must exclude both `publish_wallclock` and `instance_key`
- Depends on TRC-P7-001 (`ParquetReader`) and TRC-P7-007 (`FastStateFileLocator`)

### Success Conditions

1. **Test: GetAvailableTopics_DelegatesToLocator** — Setup: mock `FastStateFileLocator.GetAvailableTopicsForEntity("ent-A")` to return `["pos", "vel"]`. Action: `GetAvailableTopics("ent-A")`. Assert: returns `["pos", "vel"]` unchanged.
2. **Test: GetSchemaAsync_NoFiles_ReturnsNull** — Setup: `FastStateFileLocator.LocateFiles("pos", "ent-A")` returns empty. Action: `GetSchemaAsync("ent-A", "pos", ct)`. Assert: returns `null`.
3. **Test: GetSchemaAsync_ValidFile_ExcludesInfrastructureColumns** — Setup: Parquet file with columns `publish_wallclock`, `instance_key`, `x`, `y`. Action: `GetSchemaAsync("ent-A", "pos", ct)`. Assert: `schema.Columns` contains `x` and `y`; does not contain `publish_wallclock` or `instance_key`.
4. **Test: ReadAsync_NoFiles_ReturnsEmptyResult** — Setup: locator returns no files. Action: `ReadAsync("ent-A", "pos", ["x"], from, to, 5000, ct)`. Assert: `result.Samples` is empty; `result.TotalSamples == 0`; `result.Downsampled == false`; no exception.
5. **Test: ReadAsync_SingleFile_ReturnsCorrectData** — Setup: one Parquet file with 20 samples for `"ent-A"` on topic `"pos"`, column `x`. Action: `ReadAsync("ent-A", "pos", ["x"], from, to, 5000, ct)`. Assert: `result.Samples.Count == 20`; `result.EntityId == "ent-A"`; `result.Topic == "pos"`.
6. **Test: ReadAsync_MultipleFiles_TotalSamplesSummed** — Setup: file-A with 10 samples and file-B with 10 samples for the same entity and topic. Action: `ReadAsync`. Assert: `result.TotalSamples == 20`.
7. **Test: ReadAsync_DownsamplingPropagated** — Setup: 200 samples across one file. Action: `ReadAsync(..., maxSamples: 50, ...)`. Assert: `result.Downsampled == true`; `result.Samples.Count <= 50`.

---

## TRC-P7-009 — Entity Web API Endpoints, DTOs, and Wiring

**Phase:** 7 — Entity History View, Slow State Time Series, Fast State Drill-Down  
**Design reference:** [tracer_phase7_design.md §5.1](./tracer_phase7_design.md#51-endpoint-surface), [§5.2](./tracer_phase7_design.md#52-entityendpointscs), [§5.3](./tracer_phase7_design.md#53-dtos), [§5.4](./tracer_phase7_design.md#54-wiring)

### Scope

**In scope:**
- `EntityEndpoints.cs` in `Tracer.WebApi.Endpoints` with `Map(WebApplication app)` registering the 7 GET routes from §5.1, each decorated with `.WithOpenApi()`
- All 7 handler methods: `HandleListAsync`, `HandleSummaryAsync`, `HandleEventsAsync`, `HandleSlowStateAsync`, `HandleFastStateTopicsAsync`, `HandleFastStateSchemaAsync`, `HandleFastStateAsync`
- All DTOs from §5.3 in `Tracer.WebApi.Contracts.Dto`: `EntityListDto`, `EntitySummaryDto`, `EntityEventsDto`, `EntitySlowStateDto`, `SlowStateSampleDto`, `FastStateTopicSchemaDto`, `FastStateColumnDto`, `EntityFastStateDto`, `FastStateSampleDto`
- DTO mapper helpers (`EntityDtoMapper`, `EntityEventsDtoMapper`, `EntitySlowStateDtoMapper`, `FastStateSchemaDtoMapper`, `EntityFastStateDtoMapper`)
- Input validation in `HandleFastStateAsync`: returns HTTP 400 problem details if `column` array is null or empty; returns HTTP 400 if `maxSamples` is outside [10, 10000]
- `limit` clamped with `Math.Clamp(limit, 1, 5000)` in `HandleListAsync`
- DI registration as `AddSingleton` for all 6 services (`ParquetReader`, `FastStateFileLocator`, `EntityDiscoveryService`, `EntityEventsService`, `EntitySlowStateService`, `EntityFastStateService`) in both `ObserverHostBuilder` and `OfflineViewerHostBuilder`
- `EntityEndpoints.Map(app)` called in the middleware configuration of both host builders
- Unit tests in `Tracer.Tests.Unit/WebApi/EntityEndpointsTests.cs`
- Integration test in `Tracer.Tests.Integration/EntityHistoryRoundTripTests.cs`

**Out of scope:**
- Frontend code or TypeScript DTO types
- Authentication or authorization
- Rate limiting or request throttling
- SSE streaming for entity endpoints (entity history is retrospective per §1.2)

### Constraints

- `HandleFastStateAsync`: `column` null or empty → 400 problem with `Title = "Missing column"`; `maxSamples` outside [10, 10000] → 400 problem with `Title = "maxSamples out of range"`
- `HandleListAsync` clamps `limit` before calling `EntityDiscoveryService`, not inside the service
- `HandleSummaryAsync` returns `NotFound` (404) when the session does not exist AND when the entity ID is not present in the discovery results
- `HandleFastStateSchemaAsync` returns `NotFound` (404) when `EntityFastStateService.GetSchemaAsync` returns `null`
- All 6 services registered as `AddSingleton` (not `AddScoped` or `AddTransient`) in both host builders
- `EventDto` reuses the existing type from Phase 5 — no new event DTO is created

### Success Conditions

1. **Test: GET /api/entities — 200 with populated list** — Setup: session with 3 entities. Action: `GET /api/entities?sessionId=s1`. Assert: HTTP 200; body deserializes to `EntityListDto`; `count == 3`; each entry contains `entityId`, `eventCount`, `topics`.
2. **Test: GET /api/entities — 404 when session missing** — Action: `GET /api/entities?sessionId=does-not-exist`. Assert: HTTP 404 (problem details).
3. **Test: GET /api/entities/{entityId}/summary — 200 for known entity** — Action: `GET /api/entities/ent-A/summary?sessionId=s1`. Assert: HTTP 200; `entitySummaryDto.entityId == "ent-A"`.
4. **Test: GET /api/entities/{entityId}/summary — 404 for unknown entity** — Action: `GET /api/entities/no-such-entity/summary?sessionId=s1`. Assert: HTTP 404.
5. **Test: GET /api/entities/{entityId}/events — 200 with event list** — Setup: 5 events for `"ent-A"`. Action: `GET /api/entities/ent-A/events?from=...&to=...`. Assert: HTTP 200; `entityEventsDto.events.length == 5`; `truncated == false`.
6. **Test: GET /api/entities/{entityId}/events — truncated flag when limit exceeded** — Setup: 15 events. Action: `GET .../events?from=...&to=...&limit=10`. Assert: `events.length == 10`; `truncated == true`.
7. **Test: GET /api/entities/{entityId}/fast-state/{topic} — 400 when no column param** — Action: `GET /api/entities/ent-A/fast-state/pos` (no `column` query parameter). Assert: HTTP 400; problem `title` contains `"column"`.
8. **Test: GET /api/entities/{entityId}/fast-state/{topic} — 400 when maxSamples below minimum** — Action: `GET .../fast-state/pos?column=x&maxSamples=9`. Assert: HTTP 400; problem `title` contains `"maxSamples"`.
9. **Test: GET /api/entities/{entityId}/fast-state/{topic} — 400 when maxSamples above maximum** — Action: `maxSamples=10001`. Assert: HTTP 400.
10. **Test: DI wiring — all 6 entity services resolvable from Observer host** — Setup: build `ObserverHostBuilder` with test configuration. Assert: `IServiceProvider.GetRequiredService<ParquetReader>()`, `FastStateFileLocator`, `EntityDiscoveryService`, `EntityEventsService`, `EntitySlowStateService`, `EntityFastStateService` all resolve without exception.
11. **Test: Integration — entity history round trip** — Setup: inject 20 events for `"ent-X"` and 5 slow-state rows via `TestHarness`. Action: call `GET /api/entities?sessionId=...` → `GET /api/entities/ent-X/events?...` → `GET /api/entities/ent-X/slow-state?...`. Assert: entity appears in list; events endpoint returns 20 events; slow-state endpoint returns the 5 rows grouped by topic.
12. **Test: All Phase 1–6 tests pass** — Run the full test suite after all Phase 7 backend changes. Assert: zero regressions across all existing tests.

---

## TRC-P7-010 — `EntityHistoryView.vue` — View Layout and Shared Time Axis

**Phase:** 7 — Entity History View, Slow State Time Series, Fast State Drill-Down  
**Design reference:** [tracer_phase7_design.md §6](./tracer_phase7_design.md#6-frontend-view-layout)

### Scope

**In scope:**
- `EntityHistoryView.vue` — the main view container; route `/v/entity/:entityId`
- `EntitySummaryStrip` sub-component integration (entity ID, lifespan, player, topics)
- Stacked panel layout: lifecycle ribbon, slow-state rows, event strip, fast-state drill-down
- Shared `timeRange` prop threading to all child panels from `entityHistoryStore`
- Loading spinner and error-with-retry states
- `entityHistoryStore` (Pinia) — all view state: `entityId`, `sessionId`, `timeRange`, `summary`, `events`, `slowStateByTopic`, `fastStateTopics`, `selectedEventId`, `loading`, `error`, `retry()`
- Vue Router registration of the `entity-history` named route

**Out of scope:**
- The rendering logic inside each sub-panel (covered by TRC-P7-011–TRC-P7-014)
- URL synchronisation composable (TRC-P7-016)
- The fetch-orchestration composable (TRC-P7-015)
- `EntityPickerView.vue` / entity discovery in Session Browser (TRC-P7-019)

### Constraints

- All child panels receive `timeRange` from the store; they do not manage their own time state
- The view must render the panels in DOM order: summary strip → lifecycle ribbon → slow-state charts → event strip → fast-state drill-down
- Route name must be `'entity-history'` so cross-view pivots can use `router.push({ name: 'entity-history', ... })`
- `entityHistoryStore` must be the single source of truth for all data visible in the view
- Fast-state drill-down is collapsed by default; the store need not hold its open/closed state (local `ref` in `FastStateDrillDown`)

### Success Conditions

1. **Test: View renders loading state** — Setup: mount `EntityHistoryView` with `store.loading = true` and `store.summary = null`. Assert: `<LoadingSpinner>` is visible; no panel content is rendered.
2. **Test: View renders error state** — Setup: mount with `store.error = "Network error"` and `store.summary = null`. Assert: `<ErrorMessage>` is visible with the correct message; retry button is present.
3. **Test: View renders full panel stack when data present** — Setup: mount with `store.summary` populated and `store.slowStateByTopic` having two topics. Assert: `EntitySummaryStrip`, `EntityLifecycleRibbon`, two `SlowStateChart` instances, `EntityEventStrip`, and `FastStateDrillDown` are all present in the DOM.
4. **Test: entityHistoryStore — setEntity clears prior data** — Setup: store with `summary` and `events` populated; call `store.setEntity('new-id', 'new-session')`. Assert: `summary`, `events`, `slowStateByTopic`, and `selectedEventId` are all reset to null/empty.
5. **Test: entityHistoryStore — setSummary defaults timeRange to entity lifespan** — Setup: store with `timeRange.from === timeRange.to`. Call `store.setSummary({ firstSeenUtc: '2026-01-01T00:00:00Z', lastSeenUtc: '2026-01-01T00:30:00Z', ... })`. Assert: `store.timeRange.from` and `store.timeRange.to` match the entity's first/last seen.
6. **Test: entityHistoryStore — setSummary does NOT override an explicit timeRange** — Setup: store already has `timeRange.from !== timeRange.to` (user-set). Call `setSummary(...)`. Assert: `timeRange` is unchanged.
7. **Test: Vue Router — entity-history route resolved correctly** — Assert: `router.resolve({ name: 'entity-history', params: { entityId: 'e1' } }).href` equals `/v/entity/e1`.
8. **Test: Smoke — view mounts without console errors for an entity with no slow-state** — Setup: `store.slowStateByTopic = {}`. Mount view. Assert: no Vue warnings; no unhandled exceptions; `EntityEventStrip` is still rendered.

---

## TRC-P7-011 — `EntityLifecycleRibbon.vue` — Spawn/Ownership/Destruction Band

**Phase:** 7 — Entity History View, Slow State Time Series, Fast State Drill-Down  
**Design reference:** [tracer_phase7_design.md §7](./tracer_phase7_design.md#7-lifecycle-ribbon)

### Scope

**In scope:**
- `EntityLifecycleRibbon.vue` — horizontal band with three visual layers: ownership-period colour bands, lifecycle event markers
- `lifecycleClassifier.ts` — `classifyLifecycleEvent(topic): LifecycleKind` with suffix matching for spawn / ownership / destruction
- CSS-positioned rendering (no canvas); markers are `<div>` elements with `left: X%` positioning
- Ownership-period bands derived from spawn and ownership events; each band extends from one transition to the next
- Tooltip on each marker and band showing the classified kind and formatted timestamp
- Three distinct visual styles for marker kinds: spawn (green), ownership (accent blue), destruction (red)

**Out of scope:**
- Canvas rendering
- Any server-side classification of lifecycle events
- Configurable lifecycle topic patterns (deferred to Phase 8)

### Constraints

- Component receives `events: EntityEventsDto` and `timeRange: { from: Date; to: Date }` props; it does not fetch
- Lifecycle classification is done in `lifecycleClassifier.ts`, not inline in the component
- Topic pattern matching uses suffix comparison (`topic.split('.').pop()`) against the hardcoded sets defined in §7.1
- If no lifecycle events are found, the ribbon renders the track background only (no error, no empty-state message)
- `xPct` must be clamped to 0–100 before positioning to guard against events outside the time range

### Success Conditions

1. **Test: lifecycleClassifier — spawn suffixes classified correctly** — Assert: `classifyLifecycleEvent('entity.spawned') === 'spawn'`; `classifyLifecycleEvent('sim.created') === 'spawn'`; `classifyLifecycleEvent('player.spawn') === 'spawn'`.
2. **Test: lifecycleClassifier — ownership suffixes classified correctly** — Assert: `classifyLifecycleEvent('obj.ownership_changed') === 'ownership'`; `classifyLifecycleEvent('unit.owner_transferred') === 'ownership'`.
3. **Test: lifecycleClassifier — destruction suffixes classified correctly** — Assert: `classifyLifecycleEvent('unit.destroyed') === 'destruction'`; `classifyLifecycleEvent('obj.killed') === 'destruction'`.
4. **Test: lifecycleClassifier — unrelated topic returns null** — Assert: `classifyLifecycleEvent('vehicle_health') === null`; `classifyLifecycleEvent('transforms') === null`.
5. **Test: Ribbon renders correct number of markers** — Setup: mount component with events containing 1 spawn, 2 ownership, 1 destruction events. Assert: DOM contains 1 `.lifecycle-ribbon__marker--spawn`, 2 `.lifecycle-ribbon__marker--ownership`, 1 `.lifecycle-ribbon__marker--destruction`.
6. **Test: Marker horizontal position matches time** — Setup: `timeRange` is 0–1000 ms; spawn event at 500 ms. Assert: the spawn marker's `style.left` is `"50%"`.
7. **Test: No markers when no lifecycle events** — Setup: events list contains only non-lifecycle events. Assert: no `.lifecycle-ribbon__marker` elements in DOM; ribbon track element is still rendered.
8. **Test: Entity with a single ownership-transfer renders two ownership bands** — Setup: spawn at t=0, ownership_changed at t=500, no destruction. Assert: two `.lifecycle-ribbon__ownership-band` elements; the first ends at the 50% x position.

---

## TRC-P7-012 — `EntityEventStrip.vue` — Event Markers on Timeline

**Phase:** 7 — Entity History View, Slow State Time Series, Fast State Drill-Down  
**Design reference:** [tracer_phase7_design.md §9](./tracer_phase7_design.md#9-event-strip)

### Scope

**In scope:**
- `EntityEventStrip.vue` — canvas-based horizontal strip; one marker per event in `EntityEventsDto`
- `eventStripRenderer.ts` — canvas rendering function; adapts the Phase 5 marker drawing pattern to a single-lane layout; no swimlanes
- Hit-test on click: finds the nearest event within a pixel threshold; emits `select` with the event ID
- Selected event rendered with a highlight ring
- Node-colour mapping via `buildNodeColorMap` (reuse from Phase 5)
- Truncation notice in the header when `events.truncated === true`
- Click on empty space emits `select(null)` (deselects)

**Out of scope:**
- Swimlane-per-node layout (Phase 5 feature; EntityEventStrip is a single lane)
- EventInspector popover (rendered by the parent view; strip only emits `select`)

### Constraints

- Canvas must be redrawn on every change to `events`, `timeRange`, or `selectedEventId`
- `useResizeObserver` must trigger redraw when container width changes
- Marker x position uses the same `(t - from) / (to - from) * width` formula as Phase 5 to guarantee visual alignment with slow-state charts above and below
- The renderer must handle 0 events (clear canvas, return immediately) and 5000 events (no slowdown, markers overlap gracefully)

### Success Conditions

1. **Test: eventStripRenderer — marker at correct x position** — Setup: `timeRange` 0–1000 ms; single event at 250 ms; canvas 1000 px wide. Assert: rendered marker centre is at x ≈ 250 px.
2. **Test: eventStripRenderer — selected event has ring** — Setup: two events; `selectedEventId` set to event 1. Assert: renderer draws a ring around event 1's marker (inspect `ctx.arc` / `ctx.stroke` calls via mock context).
3. **Test: eventStripRenderer — 0 events does not throw** — Setup: empty events list. Action: call `renderEventStrip(ctx, { events: [], ... })`. Assert: no exception thrown; `ctx.clearRect` was called.
4. **Test: EventStrip — click near marker emits select with event ID** — Setup: mount component with one event at centre of canvas. Action: click at centre. Assert: `select` emitted with the event's `eventId`.
5. **Test: EventStrip — click far from any marker emits select(null)** — Action: click at far edge with no markers nearby. Assert: `select` emitted with `null`.
6. **Test: EventStrip — truncation notice shown when events.truncated is true** — Setup: `events.truncated = true`. Assert: header text contains "truncated".
7. **Test: EventStrip — no truncation notice when events.truncated is false** — Assert: "truncated" text not present in header.

---

## TRC-P7-013 — `SlowStateChart.vue` and `slowStateChartRenderer.ts`

**Phase:** 7 — Entity History View, Slow State Time Series, Fast State Drill-Down  
**Design reference:** [tracer_phase7_design.md §8](./tracer_phase7_design.md#8-slow-state-chart)

### Scope

**In scope:**
- `slowStateChartRenderer.ts` — canvas rendering functions `renderSlowStateChart`, `renderNumericLine`, `renderCategoricalBands`; hit-test return value `SlowStateHitEntry[]`
- `SlowStateChart.vue` — canvas component; one instance per slow-state topic; 60 px tall canvas
- `detectFields(samples)` helper inside `SlowStateChart.vue` — inspects first 20 samples; classifies fields as `'numeric'` or `'categorical'`; preferred-field ordering (value, level, health, count / state, status, phase, kind)
- Field-picker `<select>` dropdown allowing user to switch the plotted field
- Click-to-select: emits `selectEvent` with the clicked `SlowStateSampleDto`
- `useResizeObserver` integration for responsive redraw

**Out of scope:**
- Multiple simultaneous fields per chart row (multi-line per row) — Phase 8+
- Configurable chart height
- LTTB or M4 downsampling in the renderer — Phase 10+

### Constraints

- Numeric renderer: stepped line (last-value-held) — horizontal then vertical segment at each sample
- Categorical renderer: filled rectangles from one sample time to the next; text label inside band when pixel width permits; max 15 distinct colours before collapsing to `#888` grey with label "other"
- For numeric with all-identical values: range defaults to 1e-9 to avoid divide-by-zero; line drawn at mid-height
- The `detectFields` result is used only to determine the initial selected field and to populate the dropdown; it does not affect what the backend sends
- The `selectedField` ref resets to the first detected field when `samples` prop changes (new entity loaded)

### Success Conditions

1. **Test: renderNumericLine — stepped-line passes through each sample point** — Setup: 3 samples at t=0,1,2 ms with values 10, 20, 15; canvas 300×60 px; matching timeRange. Assert: canvas path commands include a move/line to the computed y-coordinates for each sample.
2. **Test: renderNumericLine — extends to right edge** — Setup: one sample at t=0 with value 5; timeRange ends at t=100. Assert: the path extends to `x = 300` (right edge of canvas).
3. **Test: renderNumericLine — all-same values renders at mid-height without divide-by-zero** — Setup: 3 samples all with value 7.0. Assert: no exception; all points drawn at the same y (mid canvas).
4. **Test: renderCategoricalBands — band widths proportional to duration** — Setup: 2 samples: 'idle' at t=0, 'attack' at t=500; timeRange 0–1000 ms; canvas 1000 px wide. Assert: first band width ≈ 500 px; second band extends to x=1000.
5. **Test: renderCategoricalBands — 0 samples renders without error** — Action: call with `samples = []`. Assert: no exception; `ctx.clearRect` called.
6. **Test: SlowStateChart — detectFields classifies numeric/categorical correctly** — Setup: samples with payload `{ "health": 100, "state": "idle" }`. Assert: `detectFields` returns two entries: `{ name: 'health', type: 'numeric' }` and `{ name: 'state', type: 'categorical' }`.
7. **Test: SlowStateChart — preferred field is selected by default** — Setup: payload has fields `{ "x": 1, "value": 5, "state": "a" }`. Assert: `selectedField` is `'value'` (preferred numeric name) rather than `'x'`.
8. **Test: SlowStateChart — click emits selectEvent with the correct sample** — Setup: canvas 1000 px wide; single sample at t=500 ms; timeRange 0–1000 ms. Action: click at x=500. Assert: `selectEvent` emitted with that sample.
9. **Test: Smoke — entity with no slow-state renders zero SlowStateChart instances** — Setup: `store.slowStateByTopic = {}`. Mount `EntityHistoryView`. Assert: no `SlowStateChart` in DOM.

---

## TRC-P7-014 — `FastStateDrillDown.vue`, `FastStateColumnPicker.vue`, and `fastStateChartRenderer.ts`

**Phase:** 7 — Entity History View, Slow State Time Series, Fast State Drill-Down  
**Design reference:** [tracer_phase7_design.md §10](./tracer_phase7_design.md#10-fast-state-drill-down)

### Scope

**In scope:**
- `FastStateDrillDown.vue` — collapsible panel; topic `<select>`, embedded `FastStateColumnPicker`, loading/error/empty states, downsampled notice, local expanded `ref`
- `FastStateColumnPicker.vue` — checkbox chip UI; shows only numeric columns (filters non-numeric); `v-model:selected` binding; "(non-numeric columns hidden)" hint when applicable
- `fastStateChartRenderer.ts` — `renderFastStateChart`; one line per selected column; shared Y axis; legend with colour-coded column names; gaps on null values
- `FastStateChart.vue` — canvas wrapper calling `renderFastStateChart`
- Default behaviour: on topic selection, auto-select the first numeric column
- Downsampled notice: shown when `data.downsampled === true`

**Out of scope:**
- Multiple Y axes (Phase 10+)
- Tooltip cross-hair on hover (Phase 10+)
- URL state for fast-state selection (TRC-P7-016 / TRC-P7-017)
- Fetching logic abstracted into `useFastStateChart` composable (TRC-P7-017); for Phase 7 the fetch may live directly in `FastStateDrillDown.vue`

### Constraints

- Panel is collapsed by default; `expanded` is local component state (not in `entityHistoryStore`)
- When `availableTopics` is empty, the panel renders its toggle button with a "(no fast-state data)" hint but does not show the body on expand
- The chart appears within 1 second for a 30-min entity history at typical sample rates (success criterion §1.3 point 5) — verified via performance test, not asserted in unit tests
- `FastStateColumnPicker` emits `update:selected` with the full new column array (not individual toggle events)
- The colour palette for column lines is deterministic: column at index `i` gets `colors[i % colors.length]` from a predefined array

### Success Conditions

1. **Test: FastStateDrillDown — collapsed by default** — Setup: mount component with `availableTopics = ['pos']`. Assert: `.fast-state-drill-down__body` is not visible (v-show=false).
2. **Test: FastStateDrillDown — toggle button expands the body** — Action: click `.fast-state-drill-down__toggle`. Assert: `.fast-state-drill-down__body` becomes visible.
3. **Test: FastStateDrillDown — no data hint when availableTopics is empty** — Setup: `availableTopics = []`. Assert: toggle button text includes "no fast-state data".
4. **Test: FastStateDrillDown — expand with no topics does not show body** — Setup: `availableTopics = []`. Action: click toggle. Assert: body remains hidden.
5. **Test: FastStateDrillDown — auto-selects first numeric column on topic selection** — Setup: `getEntityFastStateSchema` returns schema with columns `[{ name: 'ts', isNumeric: false }, { name: 'x', isNumeric: true }]`. Action: select a topic. Assert: `selectedColumns` becomes `['x']`.
6. **Test: FastStateDrillDown — downsampled notice shown when data.downsampled is true** — Setup: API returns `{ downsampled: true, totalSamples: 200000, samples: [...5000...] }`. Assert: notice text contains "200,000".
7. **Test: FastStateColumnPicker — renders only numeric columns** — Setup: schema with `[{ name: 'label', isNumeric: false }, { name: 'x', isNumeric: true }, { name: 'y', isNumeric: true }]`. Assert: two chip elements rendered (x, y); label chip absent.
8. **Test: FastStateColumnPicker — toggle emits update:selected** — Setup: `selected = ['x']`. Action: click chip for 'y'. Assert: `update:selected` emitted with `['x', 'y']`.
9. **Test: FastStateColumnPicker — unchecking column emits update:selected without it** — Setup: `selected = ['x', 'y']`. Action: click chip for 'x'. Assert: emitted with `['y']`.
10. **Test: fastStateChartRenderer — two columns drawn with distinct colors** — Setup: data with 2 columns; 5 samples each. Assert: `ctx.strokeStyle` set to two different colors across the render call.
11. **Test: fastStateChartRenderer — null values break the line** — Setup: samples with column values `[1.0, null, 3.0]`. Assert: the path contains at least two separate `moveTo` calls (line is lifted at the null).
12. **Test: fastStateChartRenderer — 0 samples renders without error** — Action: call with `data.samples = []`. Assert: no exception.

---

## TRC-P7-015 — `useEntityHistoryQuery.ts` and `entityHistoryStore.ts` (Fetch Orchestration)

**Phase:** 7 — Entity History View, Slow State Time Series, Fast State Drill-Down  
**Design reference:** [tracer_phase7_design.md §6.4](./tracer_phase7_design.md#64-useentityhistoryquery)

### Scope

**In scope:**
- `useEntityHistoryQuery.ts` — composable that watches `(store.entityId, store.sessionId)` and orchestrates the fetch sequence:
  1. Fetch entity summary (sequential first)
  2. Derive `from/to` from the summary's `firstSeenUtc/lastSeenUtc`
  3. Fetch events, slow-state, and fast-state topics in parallel (`Promise.all`)
- Abort on rapid entity switches via `AbortController`; ignore `AbortError` silently
- Store `loading` set to `true` at start, `false` in `finally`
- Store `error` set on non-abort failure
- All API calls go through the typed `useApi()` composable
- `entityHistoryStore.ts` (Pinia `defineStore`): complete state, getters (if any), and actions as specified in §6.3

**Out of scope:**
- URL synchronisation (TRC-P7-016)
- Fast-state data fetch (TRC-P7-017; triggered from `FastStateDrillDown` separately)
- Slow-state time-range narrowing (the full entity lifespan is fetched; zooming is a Phase 10+ feature)

### Constraints

- The sequential → parallel fetch order is mandatory: `summary` must resolve before the other three queries fire, because their `from/to` parameters come from the summary
- Aborting an in-flight fetch and starting a new one must not cause a race where stale data from the old fetch overwrites fresh data from the new fetch
- `useEntityHistoryQuery` must be idempotent: calling it multiple times from the same component (e.g., due to `<StrictMode>` double-mount) must not result in duplicate fetches
- The composable must be called from `EntityHistoryView.vue` setup, not from the store

### Success Conditions

1. **Test: Sequential then parallel — summary fetched before events** — Setup: mock API; record call order. Action: set `store.entityId = 'e1'`. Assert: `getEntitySummary` is called before `getEntityEvents`.
2. **Test: Parallel fetch — events, slowState, fastStateTopics called concurrently** — Setup: mock API with controlled promises. Assert: all three calls are in-flight simultaneously (none waits for another to resolve before starting).
3. **Test: AbortController — switching entity cancels prior fetch** — Setup: mock API with a never-resolving `getEntitySummary`. Action: set entity to 'e1', then immediately set to 'e2'. Assert: the fetch for 'e1' is aborted; no stale data written to store.
4. **Test: Error handling — network error sets store.error** — Setup: mock API throws `new Error('Timeout')`. Action: set `store.entityId = 'e1'`. Assert: `store.error === 'Timeout'`; `store.loading === false`.
5. **Test: AbortError is swallowed — store.error not set on abort** — Setup: abort the fetch before it completes. Assert: `store.error` remains `null`.
6. **Test: loading flag lifecycle** — Assert: `store.loading === true` while fetches are in-flight; `store.loading === false` after all settle (success or error).
7. **Test: Time range defaults to entity lifespan** — Setup: summary returns `firstSeenUtc='2026-01-01T00:00Z'`, `lastSeenUtc='2026-01-01T00:30Z'`. Assert: `store.timeRange.from` and `store.timeRange.to` match those values after fetch completes.

---

## TRC-P7-016 — `useEntityHistoryUrl.ts` — URL State

**Phase:** 7 — Entity History View, Slow State Time Series, Fast State Drill-Down  
**Design reference:** [tracer_phase7_design.md §11](./tracer_phase7_design.md#11-url-state-and-cross-view-navigation)

### Scope

**In scope:**
- `useEntityHistoryUrl.ts` — bidirectional URL ↔ store sync composable
- URL → store: on mount and on route-change, read `entityId` (path param), `session`, `from`, `to`, `select` from URL; call `store.setEntity`, `store.setTimeRange`, `store.selectedEventId = ...`
- Store → URL: debounced (250 ms) `router.replace` whenever `timeRange.from`, `timeRange.to`, or `selectedEventId` changes
- URL schema: `/v/entity/{entityId}?session=...&from=...&to=...&select=...` (see §11.1)
- `fastStateTopic` and `fastStateColumns` URL params are wired in TRC-P7-017; this task handles the base params only

**Out of scope:**
- `fastStateTopic` / `fastStateColumns` URL params (TRC-P7-017)
- History push (use `router.replace` to avoid polluting browser history on every pan/zoom)

### Constraints

- URL writes are debounced at 250 ms to avoid flooding browser history during rapid interaction
- The composable uses `watch` with `{ immediate: true }` for URL → store direction so the state is restored on page load
- `from` and `to` must be written as ISO 8601 strings; parsed back as `new Date(string)` without timezone ambiguity
- If `from` or `to` URL params are absent, the time range is not overwritten (leaves it at the entity-lifespan default set by `setSummary`)
- The composable must be called once from `EntityHistoryView.vue` setup, co-located with `useEntityHistoryQuery`

### Success Conditions

1. **Test: URL → store on mount — entityId and sessionId populated from route** — Setup: mount composable with route `{ params: { entityId: 'e1' }, query: { session: 's1' } }`. Assert: `store.entityId === 'e1'`; `store.sessionId === 's1'`.
2. **Test: URL → store — from/to parsed correctly** — Setup: route query includes `from=2026-01-01T00:00:00.000Z&to=2026-01-01T00:30:00.000Z`. Assert: `store.timeRange.from.toISOString() === '2026-01-01T00:00:00.000Z'`; `to` matches.
3. **Test: URL → store — select param sets selectedEventId** — Setup: route query `select=evt-42`. Assert: `store.selectedEventId === 'evt-42'`.
4. **Test: URL → store — missing from/to leaves timeRange unchanged** — Setup: store already has a non-default timeRange; mount composable with no from/to in URL. Assert: `store.timeRange` is unchanged.
5. **Test: Store → URL — timeRange change triggers debounced router.replace** — Setup: spy on `router.replace`. Action: change `store.timeRange.from`. After 250 ms debounce. Assert: `router.replace` called with `query.from` updated.
6. **Test: Store → URL — selectedEventId appears in URL** — Action: set `store.selectedEventId = 'ev-99'`. After debounce. Assert: `router.replace` called with `query.select === 'ev-99'`.
7. **Test: Shareable URL round-trip** — Setup: navigate to `/v/entity/e1?session=s1&from=2026-01-01T00:00:00Z&to=2026-01-01T01:00:00Z&select=ev-7`. Assert: after mount, store reflects all four values exactly.

---

## TRC-P7-017 — `useFastStateChart.ts` — On-Demand Fast State

**Phase:** 7 — Entity History View, Slow State Time Series, Fast State Drill-Down  
**Design reference:** [tracer_phase7_design.md §10](./tracer_phase7_design.md#10-fast-state-drill-down)

### Scope

**In scope:**
- `useFastStateChart.ts` — composable encapsulating all fast-state fetch logic: schema load on topic change, data load on (topic + columns + timeRange) change
- Exposed reactive refs: `schema`, `data`, `loading`, `error`
- Independent `loading` state per-fetch sequence (schema load and data load are distinct loading states or a combined one)
- `AbortController` pattern: cancels in-flight schema/data fetch when topic or columns change
- URL params: extend `useEntityHistoryUrl` (or handle in this composable) to read/write `fastStateTopic` and `fastStateColumns` from/to the URL
- Integration with `FastStateDrillDown.vue`: the component uses this composable instead of inline fetch logic

**Out of scope:**
- Canvas rendering (TRC-P7-014)
- Column picker UI (TRC-P7-014)
- `maxSamples` configuration UI (hardcoded at 5000 for Phase 7)

### Constraints

- Topic change must trigger schema refetch AND clear current data and selected columns
- Selected columns change (while topic stays the same) must trigger data refetch only (not schema refetch)
- Time range change must trigger data refetch only
- If the entity has no fast-state topics (`availableTopics` is empty), the composable should remain idle (no fetch)
- Schema fetch failure must not prevent data fetch attempts from prior successful schema; it sets `error` and leaves `schema` at its prior value

### Success Conditions

1. **Test: Topic change triggers schema fetch** — Setup: mock API. Action: set `selectedTopic = 'pos'`. Assert: `getEntityFastStateSchema('ent-1', 'pos')` called once.
2. **Test: Topic change clears previous columns and data** — Setup: prior `data` and `selectedColumns` populated. Action: change topic. Assert: `data` and `selectedColumns` reset to empty before new schema arrives.
3. **Test: Column change does NOT refetch schema** — Action: `selectedTopic` stays constant; change `selectedColumns`. Assert: `getEntityFastStateSchema` not called again.
4. **Test: Data fetch triggered after schema resolves and columns are selected** — Action: topic set → schema resolves → first numeric column auto-selected. Assert: `getEntityFastState` called with the auto-selected column.
5. **Test: TimeRange change triggers data refetch** — Setup: topic and columns already selected. Action: change `timeRange`. Assert: `getEntityFastState` called again with the new from/to.
6. **Test: loading true during fetch, false after** — Assert: `loading.value === true` while `getEntityFastState` is pending; `false` after resolution.
7. **Test: URL param round-trip — fastStateTopic and fastStateColumns in URL** — Setup: navigate to `?fastStateTopic=transforms&fastStateColumns=x,y`. Assert: composable sets `selectedTopic = 'transforms'` and `selectedColumns = ['x', 'y']` on mount; after user changes columns, URL updated with new values.

---

## TRC-P7-018 — Cross-View Navigation Pivots

**Phase:** 7 — Entity History View, Slow State Time Series, Fast State Drill-Down  
**Design reference:** [tracer_phase7_design.md §11.3](./tracer_phase7_design.md#113-cross-view-pivots)

### Scope

**In scope:**
- Enable the "Show entity history" pivot in `EventInspector.vue` (stubbed false in Phase 6): set `showEntityHistoryPivot` prop to `true` when `event.entityId != null`; call `pivotToEntity()` using `router.push({ name: 'entity-history', ... })`
- "Show in timeline" pivot from event strip and slow-state chart clicks in `EntityHistoryView` — routes to `/v/timeline/{sessionId}?from=(t-2s)&to=(t+2s)&select={eventId}`
- "Show causal tree" pivot from any event marker in `EntityHistoryView` — routes to `/v/causal/{traceId}` if the event has a non-zero `trace_id`
- "Show causal tree" pivot from slow-state chart click — enabled only when the slow-state sample's `trace_id` is non-zero
- CausalTreeView — add "Open entity history" action to its event inspector (same pattern as Timeline)
- All pivots use `router.push` (new entry) not `router.replace`

**Out of scope:**
- "Compare with entity X" multi-entity pivot (Phase 10+)
- Deep-link to a specific slow-state sample (no `slowStateEvent` URL param in Phase 7)

### Constraints

- The entity-history pivot must not appear in `EventInspector` when `event.entityId` is null or undefined
- The causal-tree pivot from EntityHistoryView must be disabled (greyed out or absent) when `trace_id === 0`
- Navigator targets must use named routes (`'timeline'`, `'entity-history'`, `'causal-tree'`) so URL structure changes don't break pivots
- The same `EventInspector` component serves all three views — the pivot's visibility is controlled by props, not by detecting which view it is mounted in

### Success Conditions

1. **Test: EventInspector — entity-history pivot visible when entityId present** — Setup: mount `EventInspector` with `event.entityId = 'e1'` and `showEntityHistoryPivot = true`. Assert: pivot button is rendered and enabled.
2. **Test: EventInspector — entity-history pivot absent when entityId null** — Setup: `event.entityId = null`. Assert: pivot button is not rendered (or is hidden/disabled).
3. **Test: EventInspector — clicking entity pivot navigates to EntityHistoryView** — Action: click the entity-history pivot button. Assert: `router.push` called with `{ name: 'entity-history', params: { entityId: 'e1' }, query: { session: ... } }`.
4. **Test: EntityHistoryView event strip — "Show in timeline" emits correct route** — Setup: event at t=10000 ms UTC. Action: click "Show in timeline" from the event marker context menu or inspector. Assert: navigation to `{ name: 'timeline', query: { from: t-2000ms ISO, to: t+2000ms ISO, select: eventId } }`.
5. **Test: EntityHistoryView event strip — "Show causal tree" navigates when trace_id non-zero** — Setup: event with `trace_id = 42`. Assert: navigation to `{ name: 'causal-tree', params: { traceId: '42' } }` (or equivalent route shape from Phase 6).
6. **Test: EntityHistoryView event strip — "Show causal tree" absent/disabled when trace_id is 0** — Setup: event with `trace_id = 0`. Assert: the causal-tree pivot button is absent or disabled.
7. **Test: Slow-state click — causal tree pivot disabled when trace_id = 0** — Setup: slow-state sample has `traceId = '0'` or `null`. Assert: causal-tree action not available.

---

## TRC-P7-019 — Entity Discovery in Session Browser

**Phase:** 7 — Entity History View, Slow State Time Series, Fast State Drill-Down  
**Design reference:** [tracer_phase7_design.md §11.5](./tracer_phase7_design.md#115-entitypickerview)

### Scope

**In scope:**
- `EntityPickerView.vue` — standalone view at `/v/entities/:sessionId`; fetches `GET /api/entities?sessionId=...`; shows a filterable list; clicking an entity navigates to `EntityHistoryView`
- "Entities" tab/button added to the Session Browser (Phase 3 `SessionBrowserView.vue`): each session card gets an "Entities" link pointing to `/v/entities/{sessionId}`
- Vue Router route `{ path: '/v/entities/:sessionId', name: 'entity-picker', component: EntityPickerView }`
- Client-side filter: case-insensitive substring match against `entityId`, `samplePlayerId`, and any topic name
- Entity list items show: entity ID, event count, topic count, sample player ID, first five topic names with "+N more" overflow

**Out of scope:**
- Server-side pagination of entity list (Phase 7 fetches up to the limit=200 default)
- Server-side full-text filter (all filtering is client-side for Phase 7)
- Topic filter dropdown on this view (Phase 8+)

### Constraints

- The Session Browser change must not break existing session-card layout; the "Entities" link is additive
- `EntityPickerView` must show a loading state while the API call is in-flight
- The component must handle an empty result gracefully (zero entities message, no JS error)
- On selecting an entity, navigation uses `router.push({ name: 'entity-history', params: { entityId }, query: { session: sessionId } })` — the same target as all other cross-view pivots

### Success Conditions

1. **Test: EntityPickerView — loads and renders entity list** — Setup: mock API returns 3 entities. Mount view. Assert: 3 `li.entity-picker__item` elements rendered.
2. **Test: EntityPickerView — loading state shown during fetch** — Setup: API promise not yet resolved. Assert: loading text/spinner visible; list not yet rendered.
3. **Test: EntityPickerView — empty list shows graceful message** — Setup: API returns `{ entities: [], count: 0 }`. Assert: list is empty; no JS error; "Loading" not shown; empty-state message present.
4. **Test: EntityPickerView — filter hides non-matching entities** — Setup: 3 entities; filter input set to a string matching only one. Assert: only 1 item visible.
5. **Test: EntityPickerView — clicking entity navigates to EntityHistoryView** — Action: click first entity item. Assert: `router.push` called with `{ name: 'entity-history', params: { entityId: ... }, query: { session: sessionId } }`.
6. **Test: EntityPickerView — topics overflow shows "+N more"** — Setup: entity with 8 topics. Assert: rendered item shows first 5 topic names and the text "+3 more".
7. **Test: Session Browser — Entities link present on session card** — Mount `SessionBrowserView` with one session. Assert: an anchor/button linking to `/v/entities/{sessionId}` is present on the session card.

---

## TRC-P7-020 — Phase 7 Tests (Backend Unit, Integration, Frontend, E2E)

**Phase:** 7 — Entity History View, Slow State Time Series, Fast State Drill-Down  
**Design reference:** [tracer_phase7_design.md §12](./tracer_phase7_design.md#12-test-plan-for-phase-7)

### Scope

**In scope:**

*Backend unit tests* (`Tracer.Tests.Unit/`):
- `Parquet/ParquetReaderTests.cs` — schema inspection, time-range filtering, stride downsampling, multi-file merge, empty result on missing file
- `Parquet/ParquetSchemaInspectorTests.cs` — `DESCRIBE` syntax against a synthetic Parquet file; numeric/non-numeric flag
- `WebApi/EntityDiscoveryServiceTests.cs` — entity list with summary fields, topic filter, player filter, empty session, limit clamping
- `WebApi/EntityEventsServiceTests.cs` — events for entity in range, empty result, truncation flag
- `WebApi/EntitySlowStateServiceTests.cs` — grouping by topic, empty result for entity with no slow-state, topic filter
- `WebApi/EntityFastStateServiceTests.cs` — topic discovery, null schema when no Parquet, read with expected samples, multi-interval merge
- `WebApi/EntityEndpointsTests.cs` — HTTP status codes; invalid time range 400; `maxSamples` out-of-range 400; empty column list 400; unknown entity cases

*Backend integration tests* (`Tracer.Tests.Integration/`):
- `EntityHistoryRoundTripTests.cs` — inject events + slow-state + fast-state Parquet via `TestHarness`; query all entity endpoints; bundle-mode round-trip
- `FastStateParquetRoundTripTests.cs` — write Parquet with known data; read via `ParquetReader`; assert exact equality; multi-interval merge

*Frontend unit tests* (`tracer-viewer/tests/unit/`):
- `slowStateChartRenderer.spec.ts` — numeric stepped line, categorical bands, empty samples, single-sample, all-same values
- `eventStripRenderer.spec.ts` — marker positions, selected ring, zero events
- `fastStateChartRenderer.spec.ts` — multiple column lines, distinct colors, null gaps, 0 samples
- `useEntityHistoryQuery.spec.ts` — sequential then parallel fetches, abort on entity switch, error handling, loading flag

*E2E* (`tracer-viewer/tests/e2e/`):
- `entity-history-view.spec.ts` — full workflow Playwright tests (see §12.4)

**Out of scope:**
- Performance profiling infrastructure (the performance thresholds from §12.5 are verified manually or in a dedicated perf suite, not as part of CI unit/integration tests)
- Tests for components covered by earlier tasks that are already tested in TRC-P7-011–TRC-P7-019 individual success conditions

### Constraints

- Backend unit tests must not depend on a real DuckDB file; use `TestHarness` fixtures or synthetic in-memory data
- `FastStateParquetRoundTripTests` must write a real Parquet file to a temp directory, read it back, then delete the temp file in teardown
- Playwright E2E tests run against the built SPA served by the OfflineViewer with a seeded bundle; the bundle must include at least one entity with slow-state and fast-state data
- All Phase 1–6 tests must continue to pass after Phase 7 changes (no regressions)

### Success Conditions

1. **Test: ParquetReaderTests — schema inspection returns expected columns** — Setup: write a Parquet file with columns `publish_wallclock TIMESTAMP`, `instance_key VARCHAR`, `x DOUBLE`, `label VARCHAR`. Action: `InspectSchemaAsync(path)`. Assert: 4 columns returned; `x` is numeric; `label` is not numeric.
2. **Test: ParquetReaderTests — time-range filter excludes out-of-range samples** — Setup: Parquet with 10 samples spanning 0–9 s; query `from=2s, to=7s`. Assert: 5 samples returned.
3. **Test: ParquetReaderTests — stride downsampling kicks in above maxSamples** — Setup: Parquet with 10000 samples; `maxSamples = 100`. Assert: result has `<= 100` samples; `Downsampled = true`.
4. **Test: ParquetReaderTests — missing file returns empty result without exception** — Action: call `ReadTimeSeriesAsync` on a non-existent path. Assert: `Samples.Count == 0`; no exception.
5. **Test: EntityDiscoveryServiceTests — topics list populated in discovery result** — Setup: 10 events for entity-A across 3 distinct topics. Assert: `DiscoverAsync` result for entity-A has all 3 topics.
6. **Test: EntityEventsServiceTests — truncated flag set at limit** — Setup: 11 events; limit = 10. Assert: `Truncated == true`; `Events.Count == 10`.
7. **Test: EntitySlowStateServiceTests — groups samples by topic** — Setup: 4 slow-state rows: 2 for `"health_topic"`, 2 for `"phase_topic"`. Assert: `ByTopic` has two keys; each with 2 samples.
8. **Test: EntityFastStateServiceTests — multi-interval: samples from two interval dirs merged** — Setup: two interval directories each containing a Parquet for `(entity-A, pos_topic)`. Assert: `ReadAsync` returns samples from both files, ordered by time.
9. **Test: EntityEndpointsTests — empty column list returns 400** — Action: `GET /api/entities/e1/fast-state/pos?from=...&to=...` (no `column` param). Assert: HTTP 400.
10. **Test: EntityHistoryRoundTripTests — bundle-mode round-trip** — Setup: create a bundle with events, slow-state, and Parquet fast-state for entity-X. Open in `OfflineViewer`. Assert: all entity API endpoints return the expected data.
11. **Test: FastStateParquetRoundTripTests — exact sample equality** — Setup: write Parquet with 50 known samples. Read back with `ParquetReader`. Assert: every sample's `publish_wallclock` and numeric values match exactly.
12. **Test: useEntityHistoryQuery.spec.ts — abort on entity switch** — Setup: first entity fetch stalls. Change entity. Assert: stalled fetch is aborted; store holds data only for the second entity.
13. **Test: slowStateChartRenderer.spec.ts — numeric renderer draws stepped path** — Setup: 3 samples at known positions. Assert: path moves then steps horizontally before each vertical transition.
14. **Test: E2E — entity-history-view.spec.ts — navigate from timeline to EntityHistoryView** — Action: open timeline, click event with entity_id, click pivot. Assert: URL matches `/v/entity/...`; `.slow-state-chart` visible.
15. **Test: E2E — entity-history-view.spec.ts — fast-state drill-down expand and plot** — Action: navigate to known entity, click toggle, select topic and column. Assert: a `<canvas>` is visible inside `.fast-state-drill-down__body`; no error banner.
16. **Test: E2E — entity-history-view.spec.ts — shareable URL restores view** — Action: navigate directly to URL with `session`, `from`, `to`. Assert: view loads with the correct entity; slow-state and event strip panels visible.
17. **Test: Regression — all Phase 1–6 tests pass** — Run the full test suite after all Phase 7 changes applied. Assert: zero test failures; zero new warnings in the C# build.

<!-- PHASE 7 TASKS END -->

<!-- PHASE 8 TASKS BEGIN -->

# Phase 8 — Annotations, Saved Views, Trigger Evaluation Log, Multi-Persona Polish

---

## TRC-P8-001 — Tracer.Storage.Annotations Assembly

**Phase:** 8 — Annotations, Saved Views, Trigger Evaluation Log, Multi-Persona Polish
**Design reference:** [tracer_phase8_design.md §3](./tracer_phase8_design.md#3-annotations-data-model-and-storage)

### Scope
**In scope:** New `Tracer.Storage.Annotations` assembly (`Tracer.Storage.Annotations.csproj`). `IAnnotationStore` interface. `AnnotationRecord` sealed record and `AnnotationKind` enum. `AnnotationFilter` record with defaults. `AnnotationsSchema` static class exposing the table and index DDL.
**Out of scope:** Store implementations (`SqliteAnnotationStore`, `BundleAnnotationStore`) — covered in TRC-P8-002 and TRC-P8-003. Web API wiring — covered in TRC-P8-005.

### Constraints
- `Tracer.Storage.Annotations.csproj` references `Tracer.Core` and `Microsoft.Data.Sqlite` only; no DuckDB reference.
- `AnnotationRecord` uses init-only properties; the record is immutable.
- All `IAnnotationStore` methods accept `CancellationToken` as their last parameter.
- `AnnotationFilter.Limit` must default to `500`.

### Success Conditions

1. **Test: AssemblyBuildsClean** — Setup: add `Tracer.Storage.Annotations` to `Tracer.sln`. Action: `dotnet build --configuration Release`. Assert: exit code 0; zero warnings in the new assembly.

2. **Test: AnnotationRecord_FieldsComplete** — Setup: construct a fully-populated `AnnotationRecord` with all thirteen fields from §3.2 (`AnnotationId`, `SessionId`, `Kind`, `EventId`, `EntityId`, `TraceId`, `TargetWallclock`, `Body`, `Title`, `Tags`, `Author`, `CreatedAtUtc`, `ModifiedAtUtc`). Assert: every property accessor compiles and returns the constructed value.

3. **Test: AnnotationKind_FourValues** — Assert: `AnnotationKind` defines exactly four members: `Event`, `Entity`, `Trace`, `TimePoint`; no extras.

4. **Test: IAnnotationStore_SixMethods** — Setup: create a minimal test class implementing `IAnnotationStore`. Assert: the compiler enforces exactly the six methods from §3.3 — `ListAsync`, `GetAsync`, `CreateAsync`, `UpdateAsync`, `DeleteAsync`, `ExportAllForSessionAsync` — each returning `Task` or `Task<T>` with a `CancellationToken` parameter.

5. **Test: AnnotationFilter_LimitDefaultIs500** — Setup: `new AnnotationFilter()`. Assert: `filter.Limit == 500`; all other properties are null.

6. **Test: AnnotationsSchema_ExecutesWithoutError** — Setup: open an in-memory SQLite connection. Action: execute `AnnotationsSchema.CreateSql`. Assert: no exception is thrown; the `annotations` table exists; all five indexes (`idx_annotations_session`, `idx_annotations_event_id`, `idx_annotations_entity_id`, `idx_annotations_trace_id`, `idx_annotations_created_at`) exist in `sqlite_master`.

7. **Test: AnnotationsSchema_IsIdempotent** — Setup: execute `AnnotationsSchema.CreateSql` once on an in-memory connection, then execute it a second time. Assert: the second execution does not throw (all statements use `IF NOT EXISTS`).

8. **Test: NoForbiddenPackageReferences** — Setup: open `Tracer.Storage.Annotations.csproj`. Assert: contains exactly one `<PackageReference Include="Microsoft.Data.Sqlite" …>` entry; the file contains no reference to DuckDB or any other data-access package.

---

## TRC-P8-002 — SqliteAnnotationStore

**Phase:** 8 — Annotations, Saved Views, Trigger Evaluation Log, Multi-Persona Polish
**Design reference:** [tracer_phase8_design.md §3.4](./tracer_phase8_design.md#34-sqliteannotationstore--live-observer)

### Scope
**In scope:** `SqliteAnnotationStore` class in `Tracer.Storage.Annotations`, implementing `IAnnotationStore`. Constructor accepting `(string dbPath, ILogger<SqliteAnnotationStore> logger)`. `InitializeAsync` method. Full CRUD and `ExportAllForSessionAsync` with `SemaphoreSlim` write lock. Private `BuildSelectSql`, `BindRecordParameters`, and `MapRecord` helpers. Unit tests in `Tracer.Tests.Unit/Annotations/SqliteAnnotationStoreTests.cs`.
**Out of scope:** DI registration (TRC-P8-005). REST layer (TRC-P8-005). Bundle variant (TRC-P8-003).

### Constraints
- All user-supplied values are bound via named `$parameter` style; no string interpolation of field values in SQL text.
- `CreateAsync`, `UpdateAsync`, and `DeleteAsync` acquire `_writeLock` (`SemaphoreSlim(1,1)`) before opening a connection; read operations open a `Mode=ReadOnly` connection without holding the lock.
- If `AnnotationId` is null or empty on `CreateAsync`, a new ULID string is assigned.
- If `CreatedAtUtc` is `default(DateTimeOffset)` on `CreateAsync`, it is set to `DateTimeOffset.UtcNow`.

### Success Conditions

1. **Test: InitializeAsync_CreatesSchema** — Setup: `SqliteAnnotationStore` pointing to a temp path that does not yet exist. Action: `await store.InitializeAsync(CancellationToken.None)`. Assert: the database file exists on disk; the `annotations` table and all five named indexes are present in `sqlite_master`.

2. **Test: InitializeAsync_IsIdempotent** — Setup: call `InitializeAsync` on an already-initialized store. Assert: the second call does not throw and the schema remains valid.

3. **Test: CreateAsync_GeneratesUlid_WhenIdEmpty** — Setup: build a valid `AnnotationRecord` with `AnnotationId = ""`. Action: `var result = await store.CreateAsync(record, ct)`. Assert: `result.AnnotationId` is a non-empty 26-character ULID string.

4. **Test: CreateAsync_SetsCreatedAtUtc_WhenDefault** — Setup: build a record with `CreatedAtUtc = default`. Action: `CreateAsync`. Assert: `result.CreatedAtUtc` is within 5 seconds of `DateTimeOffset.UtcNow`.

5. **Test: UpdateAsync_SetsModifiedAtUtc** — Setup: create a record, then build an updated copy (same ID). Action: `await store.UpdateAsync(updated, ct)`. Assert: `result.ModifiedAtUtc` is non-null and is ≥ `record.CreatedAtUtc`.

6. **Test: UpdateAsync_UnknownId_ReturnsNull** — Setup: empty store. Action: `await store.UpdateAsync(record with { AnnotationId = "nonexistent" }, ct)`. Assert: return value is `null`.

7. **Test: DeleteAsync_UnknownId_ReturnsFalse** — Action: `await store.DeleteAsync("nonexistent", ct)`. Assert: returns `false`.

8. **Test: ListAsync_FilterBySessionId_ReturnsOnlyMatchingSession** — Setup: annotations for two different session IDs in the same store. Action: `ListAsync(new AnnotationFilter { SessionId = "session-A" }, ct)`. Assert: every returned record has `SessionId == "session-A"`; session-B records are absent.

9. **Test: ListAsync_OrdersByCreatedAtDesc** — Setup: create three annotations with distinct `CreatedAtUtc` values (oldest to newest). Action: `ListAsync(new AnnotationFilter { SessionId = sid }, ct)`. Assert: returned list is ordered by `CreatedAtUtc` descending (newest first).

10. **Test: ListAsync_RespectsLimit** — Setup: create 10 annotations for the same session. Action: `ListAsync(new AnnotationFilter { SessionId = sid, Limit = 3 }, ct)`. Assert: exactly 3 records returned.

11. **Test: Tags_RoundTripThroughJsonSerialization** — Setup: create an annotation with `Tags = ["alpha", "beta", "gamma"]`. Action: retrieve via `GetAsync`. Assert: `result.Tags` equals `["alpha", "beta", "gamma"]` element-by-element.

12. **Test: NoSqlInjection_BodyContainingSqlText** — Setup: create an annotation whose `Body` equals `"'; DROP TABLE annotations; --"`. Assert: the raw SQL text in `BuildSelectSql` does not contain that literal; the annotation is stored and retrieved correctly; the `annotations` table still exists afterwards.

---

## TRC-P8-003 — BundleAnnotationStore

**Phase:** 8 — Annotations, Saved Views, Trigger Evaluation Log, Multi-Persona Polish
**Design reference:** [tracer_phase8_design.md §3.6](./tracer_phase8_design.md#36-bundleannotationstore--offline-viewer)

### Scope
**In scope:** `BundleAnnotationStore` class in `Tracer.Storage.Annotations`, implementing `IAnnotationStore` for read-only offline bundle mode. Reads from `{bundlePath}/annotations/annotations.json`. Lazy-load with in-memory `_cache`. All filtering applied in memory. Write methods (`CreateAsync`, `UpdateAsync`, `DeleteAsync`) throw `InvalidOperationException`. Unit tests in `Tracer.Tests.Unit/Annotations/BundleAnnotationStoreTests.cs`.
**Out of scope:** `LazyBundleAnnotationStore` adapter (TRC-P8-005). DI registration (TRC-P8-005).

### Constraints
- Write operations must throw `InvalidOperationException` with a message containing the word "read-only".
- `_cache` must be populated on the first call and reused on all subsequent calls without re-reading the file.
- Returns an empty list (not an exception) when `annotations.json` does not exist at the expected path.

### Success Conditions

1. **Test: ListAsync_FileAbsent_ReturnsEmpty** — Setup: `BundleAnnotationStore` pointing to a bundle directory where `annotations/annotations.json` does not exist. Action: `ListAsync(new AnnotationFilter(), ct)`. Assert: returns an empty list; no exception.

2. **Test: ListAsync_ValidFile_ReturnsParsedRecords** — Setup: write a valid `annotations.json` containing two serialized `AnnotationRecord` entries at the expected path. Action: `ListAsync(new AnnotationFilter(), ct)`. Assert: returns exactly two records with field values matching the file content.

3. **Test: GetAsync_MatchingId_ReturnsRecord** — Setup: file containing one record. Action: `GetAsync(record.AnnotationId, ct)`. Assert: returns the matching record.

4. **Test: GetAsync_UnknownId_ReturnsNull** — Action: `GetAsync("does-not-exist", ct)`. Assert: returns `null`.

5. **Test: CreateAsync_ThrowsInvalidOperationException** — Action: `store.CreateAsync(record, ct)`. Assert: throws `InvalidOperationException` whose `Message` contains "read-only".

6. **Test: UpdateAsync_ThrowsInvalidOperationException** — Action: `store.UpdateAsync(record, ct)`. Assert: throws `InvalidOperationException` whose `Message` contains "read-only".

7. **Test: DeleteAsync_ThrowsInvalidOperationException** — Action: `store.DeleteAsync("id", ct)`. Assert: throws `InvalidOperationException` whose `Message` contains "read-only".

8. **Test: ExportAllForSessionAsync_FiltersBySessionId** — Setup: file with annotations for two distinct session IDs. Action: `ExportAllForSessionAsync("session-A", ct)`. Assert: only annotations with `SessionId == "session-A"` are returned.

9. **Test: Cache_NotRefreshedOnSecondCall** — Setup: call `ListAsync` once (populates cache). Action: overwrite the file on disk with different records; call `ListAsync` again. Assert: the second call returns the same data as the first (stale cache, no re-read).

10. **Test: ListAsync_FilterByKind_AppliedInMemory** — Setup: file with two `AnnotationKind.Event` records and one `AnnotationKind.Trace` record. Action: `ListAsync(new AnnotationFilter { Kind = AnnotationKind.Event }, ct)`. Assert: exactly two records returned, both with `Kind == Event`.

---

## TRC-P8-004 — Tracer.Storage.SavedViews Assembly

**Phase:** 8 — Annotations, Saved Views, Trigger Evaluation Log, Multi-Persona Polish
**Design reference:** [tracer_phase8_design.md §6.1](./tracer_phase8_design.md#61-data-model), [§6.2](./tracer_phase8_design.md#62-sqlitesavedviewstore)

### Scope
**In scope:** New `Tracer.Storage.SavedViews` assembly. `SavedViewRecord` sealed record with all fields from §6.1. `SavedViewKind` enum (`SavedView`, `Bookmark`). `ISavedViewStore` interface (CRUD plus `RecordOpenedAsync`). `SqliteSavedViewStore` implementation sharing the same SQLite database file as annotations. Schema DDL with the `saved_views` table and three indexes from §6.2. `SavedViewFilter` record. Unit tests in `Tracer.Tests.Unit/SavedViews/SqliteSavedViewStoreTests.cs`.
**Out of scope:** REST endpoints (TRC-P8-006). Bundle read-only behavior for saved views. Frontend save/bookmark UI.

### Constraints
- `SqliteSavedViewStore` accepts the same `dbPath` as `SqliteAnnotationStore`; both tables live in the same file.
- All SQL uses named `$parameter` style; no string interpolation of user input.
- `SavedViewId` is a ULID; the store generates one on `CreateAsync` when the provided ID is empty.
- `RecordOpenedAsync` increments `open_count` and sets `last_opened_at` atomically in a single UPDATE statement.

### Success Conditions

1. **Test: AssemblyBuildsClean** — Action: `dotnet build Tracer.Storage.SavedViews --configuration Release`. Assert: exit code 0; zero warnings.

2. **Test: SavedViewRecord_FieldsComplete** — Setup: construct `SavedViewRecord` with all twelve fields (`SavedViewId`, `SessionId`, `Kind`, `ViewType`, `Url`, `Label`, `Description`, `Persona`, `Author`, `CreatedAtUtc`, `LastOpenedAtUtc`, `OpenCount`). Assert: all properties compile and return their init values.

3. **Test: SavedViewKind_TwoValues** — Assert: `SavedViewKind` has exactly two members: `SavedView` and `Bookmark`.

4. **Test: SchemaInitialization_IsIdempotent** — Setup: call schema initialization twice on the same SQLite file used for annotations. Assert: no exception on either call; `saved_views` table and three indexes (`idx_saved_views_session_persona`, `idx_saved_views_session_kind`, `idx_saved_views_last_opened`) all exist in `sqlite_master`.

5. **Test: CreateAsync_AssignsUlid_WhenIdEmpty** — Setup: `CreateAsync` called with `SavedViewId = ""`. Assert: returned record has a non-empty 26-character ULID string as `SavedViewId`.

6. **Test: RecordOpenedAsync_IncrementsOpenCount** — Setup: create a view (`OpenCount = 0`). Action: `await store.RecordOpenedAsync(id, ct)`. Assert: `GetAsync(id, ct)` returns a record with `OpenCount == 1` and non-null `LastOpenedAtUtc`.

7. **Test: RecordOpenedAsync_CalledTwice_OpenCountIsTwo** — Action: call `RecordOpenedAsync` twice in sequence. Assert: `GetAsync` returns `OpenCount == 2`.

8. **Test: FilterByPersona_ReturnsOnlyMatchingPersona** — Setup: views for `"engineer"` and `"scenario-author"`. Action: `ListAsync(new SavedViewFilter { SessionId = sid, Persona = "engineer" }, ct)`. Assert: only views with `Persona == "engineer"` returned.

9. **Test: FilterByKind_ReturnsOnlyBookmarks** — Setup: one `SavedView` and two `Bookmark` entries for the same session. Action: `ListAsync(new SavedViewFilter { SessionId = sid, Kind = SavedViewKind.Bookmark }, ct)`. Assert: exactly two records returned, both `Bookmark`.

10. **Test: UpdateAsync_UpdatesLabelAndDescription** — Setup: create a view with `Label = "old"`. Action: `UpdateAsync(record with { Label = "new", Description = "desc" }, ct)`. Assert: `GetAsync` returns `Label == "new"` and `Description == "desc"`.

---

## TRC-P8-005 — Annotation REST API Endpoints

**Phase:** 8 — Annotations, Saved Views, Trigger Evaluation Log, Multi-Persona Polish
**Design reference:** [tracer_phase8_design.md §4](./tracer_phase8_design.md#4-annotation-web-api), [§4.4](./tracer_phase8_design.md#44-wiring)

### Scope
**In scope:** `AnnotationEndpoints.cs` with the five endpoints from §4.1. `AnnotationDto`, `CreateAnnotationDto`, `UpdateAnnotationDto` DTOs from §4.3. `AnnotationDtoMapper` (record ↔ DTO). `ValidateCreate` validation logic. Bundle-mode 405 responses with `ProblemDetails`. `IAnnotationStore` singleton registration in `ObserverHostBuilder` (SQLite store at `{DataRoot}/annotations.db`) and `OfflineViewerHostBuilder` (via `LazyBundleAnnotationStore`). `LazyBundleAnnotationStore` adapter. `AnnotationEndpoints.Map(app)` call in both middleware pipelines. Unit tests in `Tracer.Tests.Unit/WebApi/AnnotationEndpointsTests.cs`.
**Out of scope:** Frontend annotation UI (TRC-P8-011+). Saved views endpoints (TRC-P8-006).

### Constraints
- `ValidateCreate` rejects: empty `Body`; empty `SessionId`; a request where the count of non-null target identifiers (`EventId`, `EntityId`, `TraceId`, `TargetWallclockUtc`) is not exactly one.
- Bundle-mode write attempts must return HTTP 405 `ProblemDetails` (not 500) by catching `InvalidOperationException` from `BundleAnnotationStore`.
- `POST /api/annotations` returns HTTP 201 with a `Location` header pointing to `/api/annotations/{id}`.

### Success Conditions

1. **Test: POST_ValidRequest_Returns201Created** — Setup: `IAnnotationStore` backed by `SqliteAnnotationStore`. Action: `POST /api/annotations` with a valid `CreateAnnotationDto` (non-empty `body`, `sessionId`, `kind = "Event"`, `eventId` set). Assert: HTTP 201; `Location` header equals `/api/annotations/{annotationId}`; response body is a valid `AnnotationDto` with a non-empty `annotationId`.

2. **Test: POST_EmptyBody_Returns400** — Action: POST with `body = ""`. Assert: HTTP 400 `ProblemDetails`; title indicates `Body` is required.

3. **Test: POST_MultipleTargetIdentifiers_Returns400** — Action: POST with both `eventId` and `entityId` set. Assert: HTTP 400 `ProblemDetails` referencing the one-target constraint.

4. **Test: POST_NoTargetIdentifier_Returns400** — Action: POST with `eventId`, `entityId`, `traceId`, and `targetWallclockUtc` all null. Assert: HTTP 400.

5. **Test: POST_BundleMode_Returns405** — Setup: `IAnnotationStore` is a `BundleAnnotationStore` (throws `InvalidOperationException` on write). Action: `POST /api/annotations`. Assert: HTTP 405 `ProblemDetails` with `Status = 405`; title contains "read-only".

6. **Test: PUT_NonExistentId_Returns404** — Action: `PUT /api/annotations/{unknown-id}` with a valid body. Assert: HTTP 404.

7. **Test: PUT_BundleMode_Returns405** — Setup: `BundleAnnotationStore`. Action: `PUT /api/annotations/{id}`. Assert: HTTP 405.

8. **Test: DELETE_NonExistentId_Returns404** — Action: `DELETE /api/annotations/{unknown-id}`. Assert: HTTP 404.

9. **Test: DELETE_BundleMode_Returns405** — Setup: `BundleAnnotationStore`. Action: `DELETE /api/annotations/{id}`. Assert: HTTP 405.

10. **Test: GET_List_FiltersBySessionId** — Setup: annotations for two different session IDs. Action: `GET /api/annotations?sessionId=A`. Assert: all returned `AnnotationDto` items have `sessionId == "A"`.

11. **Test: GET_Single_Returns200WithDto** — Setup: annotation created via `CreateAsync`. Action: `GET /api/annotations/{id}`. Assert: HTTP 200; returned DTO has matching `annotationId`.

12. **Test: GET_Single_UnknownId_Returns404** — Action: `GET /api/annotations/{unknown-id}`. Assert: HTTP 404.

13. **Test: DI_Observer_RegistersSqliteAnnotationStore** — Setup: build Observer DI container with a valid `ObserverConfig`. Assert: `IAnnotationStore` resolves to a `SqliteAnnotationStore` instance without throwing.

---

## TRC-P8-006 — Saved Views REST API Endpoints

**Phase:** 8 — Annotations, Saved Views, Trigger Evaluation Log, Multi-Persona Polish
**Design reference:** [tracer_phase8_design.md §6.4](./tracer_phase8_design.md#64-api-endpoints)

### Scope
**In scope:** `SavedViewEndpoints.cs` with the six endpoints from §6.4 (`GET /api/saved-views`, `POST /api/saved-views`, `GET /api/saved-views/{id}`, `PUT /api/saved-views/{id}`, `DELETE /api/saved-views/{id}`, `POST /api/saved-views/{id}/opened`). `SavedViewDto`, `CreateSavedViewDto`, `UpdateSavedViewDto` DTOs. `SavedViewDtoMapper`. `ISavedViewStore` DI registration in both host builders. `SavedViewEndpoints.Map(app)` call in both middleware pipelines. Unit tests in `Tracer.Tests.Unit/WebApi/SavedViewEndpointsTests.cs`.
**Out of scope:** Frontend `SaveViewButton`, `BookmarkBar`, `SavedViewsView` (TRC-P8-011+). Bundle export of saved views (covered as part of TRC-P8-009 scope extension if needed).

### Constraints
- `POST /api/saved-views/{id}/opened` returns HTTP 204 for both known and unknown IDs (fire-and-forget; no client-facing error on a stale ID).
- `GET /api/saved-views` supports `orderBy` parameter: `"created"` (default — `created_at DESC`) and `"recent"` (`last_opened_at DESC`, nulls last).
- `limit` parameter is clamped to `[1, 500]` on list endpoints.

### Success Conditions

1. **Test: POST_CreatesSavedView_Returns201** — Action: `POST /api/saved-views` with a valid `CreateSavedViewDto` (sessionId, kind, viewType, url, label, persona). Assert: HTTP 201; `Location` header set; response body has non-empty `savedViewId`.

2. **Test: GET_List_FiltersByPersona** — Setup: two saved views with personas `"engineer"` and `"scenario-author"`. Action: `GET /api/saved-views?sessionId=X&persona=engineer`. Assert: only views with `persona = "engineer"` returned.

3. **Test: GET_List_FiltersByKind** — Setup: `SavedView` and `Bookmark` entries. Action: `GET /api/saved-views?sessionId=X&kind=Bookmark`. Assert: only bookmarks returned.

4. **Test: GET_List_OrderByRecent_UsesLastOpenedAt** — Setup: view A with a recent `lastOpenedAtUtc`, view B with null `lastOpenedAtUtc`. Action: `GET /api/saved-views?sessionId=X&orderBy=recent`. Assert: view A precedes view B in the returned list.

5. **Test: POST_Opened_IncrementsOpenCount** — Setup: saved view with `openCount = 0`. Action: `POST /api/saved-views/{id}/opened`. Assert: subsequent `GET /api/saved-views/{id}` returns `openCount = 1` and non-null `lastOpenedAtUtc`.

6. **Test: POST_Opened_UnknownId_Returns204** — Action: `POST /api/saved-views/{unknown-id}/opened`. Assert: HTTP 204; no exception or error body.

7. **Test: PUT_UpdatesLabel** — Setup: created view with `label = "old"`. Action: `PUT /api/saved-views/{id}` with `label = "new"`. Assert: HTTP 200; response `label == "new"`.

8. **Test: DELETE_RemovesSavedView** — Setup: created view. Action: `DELETE /api/saved-views/{id}`. Assert: HTTP 204; subsequent `GET /api/saved-views/{id}` returns HTTP 404.

9. **Test: GET_Single_Returns200Or404** — Assert: existing ID returns HTTP 200 with matching DTO; unknown ID returns HTTP 404.

10. **Test: DI_Observer_RegistersISavedViewStore** — Assert: `ISavedViewStore` resolves from the Observer DI container without exception.

---

## TRC-P8-007 — TriggerEvalService

**Phase:** 8 — Annotations, Saved Views, Trigger Evaluation Log, Multi-Persona Polish
**Design reference:** [tracer_phase8_design.md §8.2](./tracer_phase8_design.md#82-backend-triggerevaleservice)

### Scope
**In scope:** `TriggerEvalService` class in `Tracer.WebApi.Queries`. `TriggerEvaluation` record. `TriggerResult` enum (`Fired`, `NotFired`). `TriggerEvalResult` record. Private `ParseEvaluation` method. All DuckDB queries use named `$parameter` style via `LiveMultiIntervalReader`. Unit tests in `Tracer.Tests.Unit/WebApi/TriggerEvalServiceTests.cs`.
**Out of scope:** REST endpoint layer (TRC-P8-008). `TriggerEvalView.vue` (frontend tasks).

### Constraints
- The base WHERE clause `topic = 'scenario.trigger_evaluated'` is hard-coded in the SQL; `triggerId` and `result` filters are applied via additional JSON payload extraction clauses — all using named parameters.
- `ParseEvaluation` must not throw on malformed payload; it returns a degraded `TriggerEvaluation` with `TriggerId = "(malformed payload)"` and `Inputs` set to the raw payload string.
- `limit` is enforced inside the SQL query (`LIMIT $limit`), not by post-query truncation.

### Success Conditions

1. **Test: ListAsync_OnlyReturnsTriggerEvaluatedEvents** — Setup: populate a test interval with a mix of `scenario.trigger_evaluated` events and events of other topics. Action: `ListAsync(sessionId, from, to, null, null, 1000, ct)`. Assert: every item in `Evaluations` comes from an event with `topic = "scenario.trigger_evaluated"`; other-topic events are absent.

2. **Test: ListAsync_FilterByTriggerId** — Setup: two trigger-evaluated events with payload `triggerId = "trigger-A"` and `"trigger-B"`. Action: `ListAsync(…, triggerIdFilter: "trigger-A", …)`. Assert: only evaluations with `TriggerId == "trigger-A"` returned.

3. **Test: ListAsync_FilterByResult_Fired** — Setup: mix of fired and not-fired evaluations. Action: `ListAsync(…, resultFilter: TriggerResult.Fired, …)`. Assert: every returned evaluation has `Result == TriggerResult.Fired`.

4. **Test: ListAsync_TimeRangeRespected** — Setup: evaluations before `from` and within `[from, to)`. Action: specify explicit `from` and `to`. Assert: no evaluation with `EvaluatedAtUtc < from` or `EvaluatedAtUtc >= to` is returned.

5. **Test: ParseEvaluation_ExtractsAllPayloadFields** — Setup: event with payload `{"triggerId":"t1","triggerLabel":"My Trigger","inputs":{"speed":12},"result":"fired","nextEventId":"00000000000000FF"}`. Assert: parsed `TriggerEvaluation` has `TriggerId = "t1"`, `TriggerLabel = "My Trigger"`, `Result = TriggerResult.Fired`, `Inputs` contains `"speed"`, `NextEventId` resolves to decimal 255.

6. **Test: ParseEvaluation_NotFiredResult** — Setup: event payload with `"result":"not-fired"`. Assert: `Result == TriggerResult.NotFired`.

7. **Test: ParseEvaluation_MalformedPayload_ReturnsDegradedResult** — Setup: event with `PayloadJson = "not-json"`. Assert: `ParseEvaluation` does not throw; returns `TriggerEvaluation` with `TriggerId = "(malformed payload)"` and `Inputs == "not-json"`.

8. **Test: ListAsync_EmptyResult_NoException** — Setup: no `scenario.trigger_evaluated` events in any interval. Assert: `Evaluations` is an empty list; no exception.

9. **Test: ListAsync_LimitRespected** — Setup: 50 trigger evaluations. Action: `ListAsync(…, limit: 5, …)`. Assert: `Evaluations.Count == 5`.

---

## TRC-P8-008 — Trigger Evaluation API Endpoints

**Phase:** 8 — Annotations, Saved Views, Trigger Evaluation Log, Multi-Persona Polish
**Design reference:** [tracer_phase8_design.md §8.3](./tracer_phase8_design.md#83-endpoint)

### Scope
**In scope:** `TriggerEvalEndpoints.cs` with `GET /api/scenario/triggers` from §8.3. `TriggerEvaluationListDto` and `TriggerEvaluationDto` (with all fields from `TriggerEvaluation`). `TriggerEvalDtoMapper`. `TriggerEvalService` and `TriggerEvalEndpoints.Map(app)` wired in `ObserverHostBuilder` and `OfflineViewerHostBuilder`. Unit tests in `Tracer.Tests.Unit/WebApi/TriggerEvalEndpointsTests.cs`.
**Out of scope:** `TriggerEvalService` internals (TRC-P8-007). Frontend view (frontend tasks).

### Constraints
- When `sessionId` does not resolve via `SessionQueryService.GetAsync`, return HTTP 404.
- The `result` query parameter is parsed case-insensitively; an unrecognised value is silently treated as "all results" (no 400 error).
- `limit` is clamped to `[1, 5000]`.
- `TriggerEvaluationDto.NextEventId` is serialized as a 16-character uppercase hex string or JSON `null`.

### Success Conditions

1. **Test: GET_ValidSessionId_Returns200** — Setup: known session with trigger-evaluated events registered in `SessionQueryService`. Action: `GET /api/scenario/triggers?sessionId={id}`. Assert: HTTP 200; response body deserializes to `TriggerEvaluationListDto` with non-empty `evaluations`.

2. **Test: GET_UnknownSessionId_Returns404** — Setup: `SessionQueryService` returns null for the session ID. Action: `GET /api/scenario/triggers?sessionId=unknown`. Assert: HTTP 404.

3. **Test: GET_InvalidResultParam_ReturnsAll** — Action: `GET /api/scenario/triggers?sessionId={id}&result=garbage`. Assert: HTTP 200 (not 400); evaluations of all results are included in the response.

4. **Test: GET_LimitClamped_ToMaximum** — Action: `GET /api/scenario/triggers?sessionId={id}&limit=99999`. Assert: `TriggerEvalService.ListAsync` receives `limit = 5000` (clamped); HTTP 200.

5. **Test: TriggerEvaluationDto_NextEventId_FormattedAsHex16** — Setup: evaluation whose `NextEventId.Value == 255`. Assert: `TriggerEvaluationDto.NextEventId == "00000000000000FF"`.

6. **Test: TriggerEvaluationDto_NullNextEventId_SerializedAsNull** — Setup: evaluation with `NextEventId = null`. Assert: `TriggerEvaluationDto.NextEventId` is JSON `null` (not an all-zero string).

7. **Test: DI_TriggerEvalService_Resolves** — Assert: `TriggerEvalService` resolves from the Observer's DI container without exception.

---

## TRC-P8-009 — AnnotationsExporter

**Phase:** 8 — Annotations, Saved Views, Trigger Evaluation Log, Multi-Persona Polish
**Design reference:** [tracer_phase8_design.md §3.7](./tracer_phase8_design.md#37-annotationsexporter--live--bundle)

### Scope
**In scope:** `AnnotationsExporter` static class in `Tracer.Aggregator.Consolidation`. `ExportAsync(IAnnotationStore, string sessionId, string bundleStagingPath, CancellationToken)` static method. New `AggregationStage.AnnotationsExported` enum value in `Tracer.Aggregator`. Wiring in `AggregationOrchestrator.RunAsync` between step 7 (metadata write) and step 8 (manifest computation) via optional `_annotationStore` dependency. Unit tests in `Tracer.Tests.Unit/Aggregator/AnnotationsExporterTests.cs`. Integration test in `Tracer.Tests.Integration/AnnotationsRoundTripTests.cs`.
**Out of scope:** `IAnnotationStore` implementation (TRC-P8-002). Reading the exported file in offline mode (TRC-P8-003 and TRC-P8-005).

### Constraints
- `ExportAsync` must **not** create or touch `annotations/annotations.json` when the store returns zero annotations.
- The output path must be exactly `{bundleStagingPath}/annotations/annotations.json` — this must equal the path `BundleAnnotationStore` reads from, so they stay in sync.
- `AggregationOrchestrator` treats `_annotationStore` as optional; export is skipped (no exception) when the field is null.
- The export must run **before** `ManifestBuilder.BuildAsync` so that `annotations.json` is included in the manifest checksums.

### Success Conditions

1. **Test: ExportAsync_NoAnnotations_DoesNotCreateFile** — Setup: `IAnnotationStore` returning empty list for the session. Action: `await AnnotationsExporter.ExportAsync(store, sessionId, stagingPath, ct)`. Assert: `{stagingPath}/annotations/annotations.json` does not exist on disk.

2. **Test: ExportAsync_WithAnnotations_WritesJsonFile** — Setup: store returning three annotations for the session. Action: `ExportAsync`. Assert: `{stagingPath}/annotations/annotations.json` exists; deserializing its content produces a list of 3 `AnnotationRecord` objects with field values matching the originals.

3. **Test: ExportAsync_FiltersToTargetSession** — Setup: store containing annotations for session A and session B. Action: `ExportAsync(store, "session-A", stagingPath, ct)`. Assert: the written JSON contains only annotations with `SessionId == "session-A"`.

4. **Test: ExportAsync_OutputPathMatchesBundleAnnotationStore** — Assert: the path `Path.Combine(stagingPath, "annotations", "annotations.json")` equals the path that `new BundleAnnotationStore(stagingPath)` would read from (verified by comparing normalized path strings).

5. **Test: AggregationStage_AnnotationsExported_EnumValueExists** — Assert: `Enum.IsDefined(typeof(AggregationStage), AggregationStage.AnnotationsExported)` is `true`.

6. **Test: AggregationOrchestrator_WithAnnotationStore_CallsExporter** — Setup: `AggregationOrchestrator` constructed with a mock `IAnnotationStore`. Action: `await orchestrator.RunAsync(request, progress, ct)`. Assert: `IAnnotationStore.ExportAllForSessionAsync` was called; the progress reporter received `AggregationStage.AnnotationsExported` at some point before `AggregationStage.Completed`.

7. **Test: AggregationOrchestrator_WithoutAnnotationStore_SkipsExport** — Setup: `AggregationOrchestrator` constructed without `IAnnotationStore` (null). Action: `RunAsync`. Assert: `AggregationStage.AnnotationsExported` is never reported; no `NullReferenceException` or related exception thrown.

8. **Test: Integration_AnnotationsRoundTrip** — Setup: start Observer; create 3 annotations via `POST /api/annotations`. Action: trigger bundle build; wait for `AggregationStage.Completed`; open the bundle in offline viewer. Assert: `GET /api/annotations?sessionId={id}` on the offline viewer returns all 3 annotations with matching fields; a subsequent `POST /api/annotations` on the offline viewer returns HTTP 405.

---

## TRC-P8-010 — Lifecycle Topic Configuration

**Phase:** 8 — Annotations, Saved Views, Trigger Evaluation Log, Multi-Persona Polish
**Design reference:** [tracer_phase8_design.md §9](./tracer_phase8_design.md#9-lifecycle-topic-configuration)

### Scope
**In scope:** `LifecycleClassificationConfig` class with `SpawnSuffixes`, `OwnershipSuffixes`, `DestructionSuffixes`, and optional `LifecycleRegexPatterns Regex` from §9.1. New `LifecycleClassification` section in `ObserverConfig`. `ILifecycleTopicClassifier` interface with `Classify(string topic)` returning `"spawn"`, `"ownership"`, `"destruction"`, or `null`. `ConfigurableLifecycleTopicClassifier` implementation (regex takes precedence over suffix matching). `ConfigEndpoints.cs` with `GET /api/config/lifecycle-classification`. `LifecycleConfigDto`. DI registration in both `ObserverHostBuilder` and `OfflineViewerHostBuilder`. Removal of Phase 7 hardcoded classification logic in favour of `ILifecycleTopicClassifier`. Unit tests in `Tracer.Tests.Unit/Agent/LifecycleTopicClassifierTests.cs`.
**Out of scope:** Frontend `lifecycleConfigStore.ts` (frontend tasks). Bundle metadata capture of lifecycle config (incidental to `MetadataWriter`, noted in §9.3).

### Constraints
- Default suffix values (when no config section is present) must match §9.1 exactly: spawn = `["spawn", "created", "spawned"]`; ownership = `["ownership_changed", "owner_transferred", "owner_changed"]`; destruction = `["destroyed", "killed", "removed", "despawned"]`.
- When a regex pattern is non-null, it is tested first; if it matches the topic, the corresponding classification is returned and suffix matching is not performed for that category.
- Phase 7 callers of hardcoded lifecycle detection must be updated to use `ILifecycleTopicClassifier`; no raw string literals like `"*.spawn"` or `"*.created"` may remain in the Phase 7 classification code paths.
- `LifecycleClassificationConfig` must bind from the `"LifecycleClassification"` section of `appsettings.json` via the Options pattern.

### Success Conditions

1. **Test: DefaultConfig_SpawnSuffixes** — Setup: `ConfigurableLifecycleTopicClassifier` with `new LifecycleClassificationConfig()`. Assert: `Classify("vehicle.spawn") == "spawn"`, `Classify("vehicle.created") == "spawn"`, `Classify("vehicle.spawned") == "spawn"`.

2. **Test: DefaultConfig_OwnershipSuffixes** — Assert: `Classify("team.ownership_changed") == "ownership"`, `Classify("unit.owner_transferred") == "ownership"`, `Classify("player.owner_changed") == "ownership"`.

3. **Test: DefaultConfig_DestructionSuffixes** — Assert: `Classify("unit.destroyed") == "destruction"`, `Classify("vehicle.killed") == "destruction"`, `Classify("npc.removed") == "destruction"`, `Classify("entity.despawned") == "destruction"`.

4. **Test: DefaultConfig_UnknownTopic_ReturnsNull** — Assert: `Classify("sensors.telemetry") == null`, `Classify("weapons.fire") == null`, `Classify("vehicle.update") == null`.

5. **Test: CustomSuffixes_ReplaceBuiltIn** — Setup: `LifecycleClassificationConfig` with `SpawnSuffixes = ["instantiated"]`. Assert: `Classify("thing.instantiated") == "spawn"`; `Classify("thing.spawn") == null` (built-in suffix no longer active).

6. **Test: RegexOverride_TakesPrecedenceOverSuffixes** — Setup: config with `Regex.Spawn = "^entity\\.new_"`. Assert: `Classify("entity.new_fighter") == "spawn"` (matched by regex); suffix-matching is not applied for the spawn category when regex is set, so `Classify("vehicle.spawn")` returns null (assuming no ownership/destruction regex).

7. **Test: GET_LifecycleClassification_Returns200WithConfig** — Setup: Observer configured with `SpawnSuffixes = ["born"]`. Action: `GET /api/config/lifecycle-classification`. Assert: HTTP 200; response `LifecycleConfigDto` has `spawnSuffixes = ["born"]`.

8. **Test: HardcodedClassifier_IsReplaced** — Assert: searching the Phase 7 lifecycle classification implementation files (e.g., `EntityQueryService.cs`, `EntityLifecycleService.cs`, or equivalent) finds no direct string comparisons against hardcoded suffix literals like `"spawn"`, `"created"`, etc.; all classification is delegated to an `ILifecycleTopicClassifier` call.

9. **Test: DI_BothHosts_ResolveILifecycleTopicClassifier** — Assert: `ILifecycleTopicClassifier` resolves from both the Observer and Offline Viewer DI containers as a `ConfigurableLifecycleTopicClassifier` instance; neither throws a resolution exception.

10. **Test: DefaultValues_MatchDesignSpec** — Setup: `new LifecycleClassificationConfig()`. Assert: `SpawnSuffixes` equals `["spawn", "created", "spawned"]`; `OwnershipSuffixes` equals `["ownership_changed", "owner_transferred", "owner_changed"]`; `DestructionSuffixes` equals `["destroyed", "killed", "removed", "despawned"]`; `Regex` is `null`.

---

## TRC-P8-011 — `AnnotationMarker.vue` and Annotation Overlay Integration

**Phase:** 8 — Annotations, Saved Views, Trigger Evaluation Log, Multi-Persona Polish
**Design reference:** [tracer_phase8_design.md §5.3](./tracer_phase8_design.md#53-annotation-indicators-in-views)

### Scope
**In scope:** `AnnotationMarker.vue` shared visual primitive (small badge/icon with hover tooltip); overlay rendering of the marker in Timeline (Phase 5 canvas), Causal Tree (Phase 6 canvas), Entity History event strip (Phase 7), and Scenario View (Phase 3) when an annotation exists for that event, entity, or trace; click on marker opens `AnnotationEditor` in view-mode pre-populated with the annotation; density-threshold suppression (marker hidden below 8 px event footprint at high zoom); integration with `useAnnotations` to determine whether a marker should render for a given `eventId`/`entityId`/`traceId`.
**Out of scope:** Annotation CRUD (TRC-P8-013); `AnnotationList` sidebar (TRC-P8-012); new canvas rendering infrastructure; styling beyond the marker badge and tooltip.

### Constraints
- Must not trigger a new network request per visible event; markers are derived from the already-loaded `annotationStore` state.
- Marker icon must be accessible (aria-label and keyboard-focusable).
- No new npm packages.

### Success Conditions
1. **Test: Marker_RendersWhenAnnotationExists** — Setup: `annotationStore` seeded with one annotation for `eventId='AAAA'`. Render `<AnnotationMarker eventId="AAAA" />`. Assert: marker element with class `.annotation-marker` is present in the DOM.
2. **Test: Marker_HiddenWhenNoAnnotation** — Setup: `annotationStore` seeded with no entries matching `eventId='BBBB'`. Assert: `<AnnotationMarker eventId="BBBB" />` renders nothing (v-if false).
3. **Test: Marker_Tooltip_ShowsAnnotationTitle** — Setup: annotation with `title='Suspicious spike'` for an event. Mount marker; hover the element. Assert: tooltip contains "Suspicious spike".
4. **Test: Marker_Tooltip_FallsBackToBodyFirstLine** — Setup: annotation with `title=null`, `body='This is line one\nThis is line two'`. Assert: tooltip text is "This is line one".
5. **Test: Marker_Click_EmitsEditEvent** — Mount `<AnnotationMarker eventId="CCCC" />` with a matching annotation. Click the marker. Assert: component emits an `edit` event carrying the `AnnotationDto`.
6. **Test: Timeline_OverlayVisible** — Integration: render `TimelineView` with a session that has an annotated event in the viewport. Assert: at least one `.annotation-marker` element is visible in the view.
7. **Test: CausalTree_OverlayVisible** — Integration: render `CausalTreeView` with a session whose root event has an annotation. Assert: `.annotation-marker` present on the root node.
8. **Test: EntityHistory_EventStrip_OverlayVisible** — Integration: `EntityHistoryView` with an annotated event for the current entity. Assert: `.annotation-marker` present in `.entity-event-strip`.

---

## TRC-P8-012 — `AnnotationEditor.vue` and `AnnotationList.vue`

**Phase:** 8 — Annotations, Saved Views, Trigger Evaluation Log, Multi-Persona Polish
**Design reference:** [tracer_phase8_design.md §5.2](./tracer_phase8_design.md#52-annotationeditorvue); [§5.4](./tracer_phase8_design.md#54-inspector-integration)

### Scope
**In scope:** `AnnotationEditor.vue` — modal overlay with title input, multi-line body textarea, tag management (add on Enter or comma, remove by ×), Save/Cancel buttons, Delete button (edit mode only); author name read from `localStorage['tracer:authorName']`; `AnnotationList.vue` — vertical list of `AnnotationDto` items for the current context (event/entity/trace); each row shows title or body excerpt, author, relative timestamp, and Edit button; click/enter on a row emits `select` (parent scrolls or highlights the target); integration of both into `EventInspector.vue` from Phase 5.
**Out of scope:** CRUD API calls (those belong in `useAnnotations` — TRC-P8-013); annotation markers in canvas views (TRC-P8-011); new routing.

### Constraints
- Editor body textarea: `autofocus` on open.
- Save button disabled when body is blank.
- Delete button must only appear in edit mode (when `initial` prop is non-null).
- `AnnotationList` must be scrollable when more than 5 entries overflow.

### Success Conditions
1. **Test: Editor_SaveDisabled_WhenBodyBlank** — Mount `<AnnotationEditor visible />`. Assert: `.annotation-editor__save` has `disabled` attribute.
2. **Test: Editor_SaveEnabled_WhenBodyFilled** — Mount editor, type into body textarea. Assert: save button no longer disabled.
3. **Test: Editor_PopulatesFromInitialProp** — Mount with `initial` containing `body='hello'`, `title='world'`, `tags=['foo']`. Assert: textarea has value "hello", title input has value "world", tag chip "foo" is rendered.
4. **Test: Editor_DeleteButton_HiddenInCreateMode** — Mount with `initial=null`. Assert: `.annotation-editor__delete` is not present.
5. **Test: Editor_DeleteButton_VisibleInEditMode** — Mount with `initial` = an existing annotation. Assert: `.annotation-editor__delete` is present.
6. **Test: Editor_EmitsSaveWithCorrectData** — Mount editor. Fill body = "test body". Fill title = "test title". Click save. Assert: emitted `save` event contains `{ body: 'test body', title: 'test title', tags: [] }`.
7. **Test: Editor_TagManagement_AddAndRemove** — Add tag "foo" (press Enter). Assert tag chip renders. Click × on chip. Assert tag chip removed. Emitted payload has `tags: []`.
8. **Test: Editor_CancelEmitsCancel** — Click Cancel. Assert: `cancel` event emitted; no `save` event.
9. **Test: List_RendersAnnotations** — Mount `<AnnotationList :annotations="[...two items...]" />`. Assert: two list rows rendered.
10. **Test: List_ClickRowEmitsSelect** — Click first row. Assert: `select` event emitted with that `AnnotationDto`.
11. **Test: List_EditButtonEmitsEdit** — Click Edit button on a row. Assert: `edit` event emitted with that `AnnotationDto`.
12. **Test: Inspector_ShowsAddNoteButton** — Render `EventInspector` with an event that has no annotations. Assert: `.event-inspector__add-note` button is visible.
13. **Test: Inspector_OpenEditor_OnAddNote** — Click "Add note". Assert: `<AnnotationEditor>` becomes visible.

---

## TRC-P8-013 — `useAnnotations.ts` and `annotationStore.ts`

**Phase:** 8 — Annotations, Saved Views, Trigger Evaluation Log, Multi-Persona Polish
**Design reference:** [tracer_phase8_design.md §5.1](./tracer_phase8_design.md#51-the-composable-useannotations)

### Scope
**In scope:** `useAnnotations.ts` composable — reactive `annotations` ref loaded from `GET /api/annotations`; `create(body, kind, target, title?, tags?)` calling `POST /api/annotations`; `update(id, body, title?, tags?)` calling `PUT /api/annotations/{id}`; `remove(id)` calling `DELETE /api/annotations/{id}`; optimistic local state update after each mutation; `watch` on `sessionId` + target filter params triggers reload; `annotationStore.ts` Pinia store holding all annotations for the current view (keyed by `annotationId`); accessors `byEventId(id)`, `byEntityId(id)`, `byTraceId(id)` for O(1) lookup used by `AnnotationMarker`.
**Out of scope:** The API HTTP client itself (part of the shared `useApi` composable); UI components (TRC-P8-011, TRC-P8-012).

### Constraints
- The store must not duplicate entries when `load` is called multiple times.
- `create` must append to the local list immediately without waiting for a store reload.
- Error from a write operation must propagate (let calling component decide how to surface it).
- Author name is read from `localStorage['tracer:authorName']` — falls back to `'anonymous'`.

### Success Conditions
1. **Test: useAnnotations_LoadsOnMount** — Mock `api.listAnnotations` to return two items. Mount a component using `useAnnotations`. Assert: `annotations.value` has two entries after load.
2. **Test: useAnnotations_ReloadsOnSessionIdChange** — Set up composable with reactive `sessionId`. Change sessionId. Assert: `api.listAnnotations` called again with new sessionId.
3. **Test: useAnnotations_Create_AddsToLocalList** — Call `create('body', 'Event', { eventId: 'X' })`. Mock API to return a new `AnnotationDto`. Assert: `annotations.value` length increased by 1; new entry is at index 0.
4. **Test: useAnnotations_Update_PatchesLocalEntry** — Load two annotations. Call `update(annotations.value[0].annotationId, 'new body')`. Assert: `annotations.value[0].body === 'new body'`.
5. **Test: useAnnotations_Remove_DeletesLocalEntry** — Load one annotation. Call `remove(id)`. Assert: `annotations.value` is empty.
6. **Test: annotationStore_ByEventId_ReturnsCorrectAnnotations** — Seed store with three annotations (two for eventId='A', one for eventId='B'). Assert: `byEventId('A')` returns two entries; `byEventId('B')` returns one.
7. **Test: annotationStore_NoDuplicatesOnDoubleLoad** — Call `load` twice with the same data. Assert: store length equals original item count (no duplication).
8. **Test: annotationStore_IsEmpty_WhenNoSessionLoaded** — Fresh store with no `load` call. Assert: `byEventId(any)` returns empty array.

---

## TRC-P8-014 — `SavedViewsView.vue` and `SaveViewButton.vue`

**Phase:** 8 — Annotations, Saved Views, Trigger Evaluation Log, Multi-Persona Polish
**Design reference:** [tracer_phase8_design.md §6.5](./tracer_phase8_design.md#65-saveviewbutton); [§6.7](./tracer_phase8_design.md#67-savedviewsview)

### Scope
**In scope:** `SaveViewButton.vue` — two-part toolbar control: bookmark icon (one-click, calls `POST /api/saved-views` with `kind:'Bookmark'` and auto-generated label from URL params) and "Save view" text button (opens inline dialog for label + optional description, then POSTs with `kind:'SavedView'`); both read current persona from `usePersona`; `SavedViewsView.vue` — route `/v/saved-views/:sessionId`; loads all `kind:'SavedView'` entries via `GET /api/saved-views`; grouped by `viewType`; filterable by persona (dropdown); click navigates to saved URL after calling `POST /api/saved-views/{id}/opened`; delete with confirmation prompt; empty-state message.
**Out of scope:** `BookmarkBar` (TRC-P8-015); `useSavedViews` composable details beyond what is needed for these two components; saved-views backend (TRC-P8-006).

### Constraints
- The save dialog's Save button is disabled while the label field is blank.
- Route `/v/saved-views/:sessionId` must be registered in the Vue Router config.
- Auto-label generation uses `route.query` (topic, trace, entity) and current time; must not be empty.

### Success Conditions
1. **Test: SaveViewButton_BookmarkClick_CallsAPI** — Mount `<SaveViewButton sessionId="s1" viewType="timeline" />`. Click the bookmark button. Assert: `api.createSavedView` called once with `kind: 'Bookmark'`.
2. **Test: SaveViewButton_AutoLabel_NotEmpty** — Mock `useRoute` with no query params. Click bookmark. Assert: `label` in the API call is a non-empty string.
3. **Test: SaveViewButton_AutoLabel_IncludesTopic** — Mock route with `query.topic = ['weapons.fire']`. Click bookmark. Assert: label includes "weapons.fire".
4. **Test: SaveViewButton_SaveDialog_OpenOnClick** — Click "Save view" button. Assert: dialog element with class `.save-view-dialog` is visible.
5. **Test: SaveViewButton_SaveDisabled_WhenLabelBlank** — Open dialog. Assert: save button inside dialog has `disabled` attribute.
6. **Test: SaveViewButton_SaveExplicit_CallsAPI** — Open dialog; fill label = "Test view". Click Save. Assert: `api.createSavedView` called with `kind: 'SavedView'`, `label: 'Test view'`.
7. **Test: SaveViewButton_SaveDialog_ClosesAfterSave** — After successful save, dialog element is removed from DOM.
8. **Test: SavedViewsView_RendersViewsGroupedByType** — Mount `SavedViewsView` with mocked API returning 3 views across 2 viewTypes. Assert: two `<section>` group headings are rendered.
9. **Test: SavedViewsView_PersonaFilterChange_Reloads** — Change persona filter dropdown. Assert: `api.listSavedViews` called again with updated persona param.
10. **Test: SavedViewsView_EmptyState_Shown** — Mock API returns empty array. Assert: empty-state message visible.
11. **Test: SavedViewsView_DeleteView_CallsAPIAndReloads** — Click Delete on a view (accept confirm). Assert: `api.deleteSavedView` called; list reloads.
12. **Test: SavedViewsView_OpenView_NavigatesAndRecordsOpen** — Click a saved view row. Assert: `router.push` called with the saved URL; `api.recordSavedViewOpened` called with the view's id.

---

## TRC-P8-015 — `BookmarkBar.vue` and `useBookmarks.ts`

**Phase:** 8 — Annotations, Saved Views, Trigger Evaluation Log, Multi-Persona Polish
**Design reference:** [tracer_phase8_design.md §6.6](./tracer_phase8_design.md#66-bookmarkbar); [§6.1](./tracer_phase8_design.md#61-data-model)

### Scope
**In scope:** `BookmarkBar.vue` — horizontal strip below the toolbar; hidden when no bookmarks exist for the current `viewType` + `persona`; renders up to 10 chips, each with truncated label, click navigates to saved URL and records an open; `useBookmarks.ts` composable — `bookmarkCurrentUrl(sessionId, viewType)` posts a bookmark with auto-label; `listBookmarks(sessionId, viewType)` fetches up to 10 `kind:'Bookmark'` entries ordered by recency; `removeBookmark(id)` deletes; reactivity: reloads automatically when `persona` store changes.
**Out of scope:** `SaveViewButton` (TRC-P8-014); backend saved-views store (TRC-P8-006).

### Constraints
- `BookmarkBar` must not render a wrapping element when the bookmark list is empty (keep DOM clean for view layout).
- Chip text max-width 16rem with `text-overflow: ellipsis`.
- Reacts to persona store changes without a full page reload.

### Success Conditions
1. **Test: BookmarkBar_Hidden_WhenNoBookmarks** — Mount `<BookmarkBar sessionId="s1" viewType="timeline" />` with mocked API returning empty list. Assert: component root element not present in DOM (v-if).
2. **Test: BookmarkBar_RendersChips** — Mock API returns 3 bookmarks. Assert: 3 `.bookmark-bar__chip` elements rendered.
3. **Test: BookmarkBar_ChipClick_NavigatesAndRecords** — Click a chip. Assert: `router.push` called with the bookmark's URL; `api.recordSavedViewOpened` called.
4. **Test: BookmarkBar_ReloadsOnPersonaChange** — Change persona store value. Assert: `api.listSavedViews` called again with updated persona.
5. **Test: useBookmarks_BookmarkCurrentUrl_CallsAPI** — Call `bookmarkCurrentUrl('s1', 'timeline')`. Assert: `api.createSavedView` called with `kind: 'Bookmark'`, non-empty label, `viewType: 'timeline'`.
6. **Test: useBookmarks_ListBookmarks_ReturnsOnlyBookmarks** — Mock API to return a mix of `SavedView` and `Bookmark` kinds. Call `listBookmarks`. Assert: returned list contains only `Bookmark` items (composable enforces the kind filter).
7. **Test: useBookmarks_RemoveBookmark_CallsDelete** — Call `removeBookmark('id-1')`. Assert: `api.deleteSavedView` called with `'id-1'`.
8. **Test: useBookmarks_LimitTen** — Mock API to return 12 bookmarks. Call `listBookmarks`. Assert: result has at most 10 items (composable passes `limit: 10` to the API).

---

## TRC-P8-016 — `TriggerEvalView.vue` and `TriggerEvalRow.vue`

**Phase:** 8 — Annotations, Saved Views, Trigger Evaluation Log, Multi-Persona Polish
**Design reference:** [tracer_phase8_design.md §8.4](./tracer_phase8_design.md#84-triggerevalview); [§8.1](./tracer_phase8_design.md#81-what-this-view-is-for)

### Scope
**In scope:** `TriggerEvalView.vue` — route `/v/triggers/:sessionId`; loads from `GET /api/scenario/triggers`; filter controls: trigger-ID select (populated from distinct trigger IDs in the loaded data) and result select (All / Fired / Not fired); re-fetches on filter change; loading state; empty state; `TriggerEvalRow.vue` — single table row rendering a `TriggerEvaluationDto` (time, triggerId, label, publisherNode, result pill); click on row toggles inline inputs JSON expansion panel; "Timeline" action button navigates to Timeline at ±5 s around the evaluation time; "Tree" action button navigates to `CausalTreeView` seeded with the trigger evaluation event's `eventId`; route registration in Vue Router.
**Out of scope:** Backend `TriggerEvalService` and `TriggerEvalEndpoints` (TRC-P8-007, TRC-P8-008); annotation markers on trigger rows (deferred — annotations attach to events, not trigger rows; the event is navigated to via the causal tree).

### Constraints
- The route `/v/triggers/:sessionId` must be registered before this task is done.
- Result pill CSS class must be `trigger-eval-view__pill--Fired` or `trigger-eval-view__pill--NotFired` to allow targeted test selectors and styling.
- Inline inputs expansion must show raw JSON, not parsed fields.

### Success Conditions
1. **Test: TriggerEvalView_LoadsOnMount** — Mock `api.listTriggerEvaluations` to return 5 evaluations. Mount view. Assert: 5 `<tr>` rows rendered in `tbody`.
2. **Test: TriggerEvalView_LoadingState** — Delay API response. Assert: loading indicator present before response resolves.
3. **Test: TriggerEvalView_EmptyState** — Mock returns empty list. Assert: empty-state message or zero rows; no JS error.
4. **Test: TriggerEvalView_ResultFilterChange_Refetches** — Select "Fired" from result filter dropdown. Assert: `api.listTriggerEvaluations` called again with `result: 'fired'`.
5. **Test: TriggerEvalView_TriggerIdFilter_Refetches** — Select a trigger ID from the trigger select. Assert: API called with `triggerId` matching selected value.
6. **Test: TriggerEvalView_DistinctTriggerIds_PopulateSelect** — 5 evaluations with 3 distinct trigger IDs. Assert: trigger select has 4 options ("All" + 3 distinct).
7. **Test: TriggerEvalRow_FiredPill_HasCorrectClass** — Mount row with `result: 'Fired'`. Assert: pill element has class `trigger-eval-view__pill--Fired`.
8. **Test: TriggerEvalRow_NotFiredPill_HasCorrectClass** — Mount row with `result: 'NotFired'`. Assert: pill element has class `trigger-eval-view__pill--NotFired`.
9. **Test: TriggerEvalRow_TimelineButton_Navigates** — Click "Timeline" button on a row with `evaluatedAtUtc='2026-01-01T10:00:00Z'`. Assert: router navigates to timeline route with `from` = 9:59:55Z and `to` = 10:00:05Z and `select` = the row's `eventId`.
10. **Test: TriggerEvalRow_TreeButton_Navigates** — Click "Tree" button. Assert: router navigates to causal tree route with the row's `eventId`.
11. **Test: TriggerEvalRow_InlineExpansion_TogglesOnClick** — Click the row body. Assert: inputs JSON panel becomes visible. Click again. Assert: panel hidden.
12. **Test: TriggerEvalRow_InputsPanel_ShowsRawJson** — Inputs JSON is `{"speed":10}`. Click row. Assert: expansion panel contains the string `"speed"`.

---

## TRC-P8-017 — `PersonaSwitcher.vue`, `usePersona.ts`, and `personaStore.ts`

**Phase:** 8 — Annotations, Saved Views, Trigger Evaluation Log, Multi-Persona Polish
**Design reference:** [tracer_phase8_design.md §7](./tracer_phase8_design.md#7-persona-switcher)

### Scope
**In scope:** `personaStore.ts` Pinia store — `current` persona (`'engineer' | 'scenario-author' | 'operator'`); `set(persona)` action persists to `localStorage['tracer:persona']`; initialised from localStorage on store creation, defaulting to `'engineer'`; `usePersona.ts` composable — thin wrapper exposing `persona` (computed ref) and `setPersona(p)` convenience function, plus `allPersonas` constant array; `PersonaSwitcher.vue` — segmented button group in `AppHeader` showing three buttons (Engineer 🔧 / Scenario Author 🎬 / Operator 🖥️); active button highlighted; click calls `store.set()`; integration with `SessionCard.vue` — click routes to `timeline` for Engineer, `scenario` for Scenario Author and Operator; `SavedViewsView` and `BookmarkBar` default filter reads from persona store.
**Out of scope:** Authorization or access control (design §7.4 explicitly excludes this); per-persona dashboards (future phase).

### Constraints
- Persona store must initialise synchronously (localStorage read in `state()` factory).
- Switching persona must not trigger a navigation; it only changes stored preference.
- `AppHeader.vue` must mount `<PersonaSwitcher />` in its template.

### Success Conditions
1. **Test: personaStore_DefaultIsEngineer_WhenLocalStorageEmpty** — Clear `localStorage`. Initialise store. Assert: `store.current === 'engineer'`.
2. **Test: personaStore_RestoresFromLocalStorage** — Set `localStorage['tracer:persona'] = 'operator'`. Initialise store. Assert: `store.current === 'operator'`.
3. **Test: personaStore_Set_PersistsToLocalStorage** — Call `store.set('scenario-author')`. Assert: `localStorage.getItem('tracer:persona') === 'scenario-author'`.
4. **Test: personaStore_Set_UpdatesCurrentReactively** — Watch `store.current`. Call `store.set('operator')`. Assert: watcher fires with new value `'operator'`.
5. **Test: usePersona_Persona_MatchesStore** — `usePersona().persona.value === personaStore.current`.
6. **Test: PersonaSwitcher_ActiveButton_MatchesCurrent** — Set store to `'engineer'`. Mount `<PersonaSwitcher />`. Assert: button with text "Engineer" has class `persona-switcher__btn--active`; others do not.
7. **Test: PersonaSwitcher_Click_SetsPersona** — Click "Scenario Author" button. Assert: `store.current === 'scenario-author'`.
8. **Test: PersonaSwitcher_AllThreeButtons_Render** — Assert: three `.persona-switcher__btn` elements exist with correct labels.
9. **Test: SessionCard_Engineer_RoutesToTimeline** — Set persona = `'engineer'`. Click session card. Assert: `router.push` called with route name `'timeline'`.
10. **Test: SessionCard_ScenarioAuthor_RoutesToScenario** — Set persona = `'scenario-author'`. Click session card. Assert: `router.push` called with route name `'scenario'`.
11. **Test: SessionCard_Operator_RoutesToScenario** — Set persona = `'operator'`. Click session card. Assert: `router.push` called with route name `'scenario'`.
12. **Test: AppHeader_ContainsPersonaSwitcher** — Mount `AppHeader`. Assert: `PersonaSwitcher` component rendered within it.

---

## TRC-P8-018 — Phase 8 Tests (Backend Unit, Integration, Frontend)

**Phase:** 8 — Annotations, Saved Views, Trigger Evaluation Log, Multi-Persona Polish
**Design reference:** [tracer_phase8_design.md §10](./tracer_phase8_design.md#10-test-plan-for-phase-8)

### Scope
**In scope:**
- **Backend unit** (`Tracer.Tests.Unit`): `SqliteAnnotationStoreTests` — full CRUD, tag serialization, filter combinations, ordering, limit; `BundleAnnotationStoreTests` — read from JSON, empty file, write operations throw; `SqliteSavedViewStoreTests` — CRUD, open-count increment, `last_opened_at` update, persona/kind filter; `AnnotationEndpointsTests` — all endpoints, 201/400/404/405 status codes, bundle-mode 405; `SavedViewEndpointsTests` — CRUD, `/opened` endpoint, ordering; `TriggerEvalServiceTests` — correct topic filter, trigger-ID filter, result filter, time range, malformed payload tolerance; `TriggerEvalEndpointsTests` — 200 with valid session, 404 unknown session, result value normalisation, limit clamping; `AnnotationsExporterTests` — no file created when empty, correct JSON output, session ID scoping, file path.
- **Backend integration** (`Tracer.Tests.Integration`): `AnnotationsRoundTripTests` — Observer live session with 3 annotations → bundle build → offline viewer returns same 3; write attempts in bundle mode → 405; `SavedViewsRoundTripTests` — same pattern for saved views; `TriggerEvalIntegrationTests` — push synthetic `scenario.trigger_evaluated` events, filter by trigger ID, result, time range.
- **Frontend Vitest** (`tracer-viewer/tests/unit`): `annotationStore.spec.ts` — see TRC-P8-013 success conditions; `useAnnotations.spec.ts` — composable lifecycle (load, create, update, remove, duplicate-prevention); `usePersona.spec.ts` — default, persist, restore; `useBookmarks.spec.ts` — listBookmarks kind filter, limit enforcement, removeBookmark.
- **E2E Playwright** (`tracer-viewer/tests/e2e`): `annotations-flow.spec.ts` — create annotation on event via inspector, reload, verify marker visible; `saved-views-flow.spec.ts` — save a view with label, navigate away, open saved-views list, click to restore URL with same query params; `persona-switcher.spec.ts` — switch Engineer → Timeline on session card click; switch Scenario Author → Scenario on session card click.
**Out of scope:** Performance benchmarks (referenced in design §10.5 — tracked separately as an operational concern, not a unit/integration test); Phase 1-7 regression tests (already covered by their respective task test files).

### Constraints
- `AnnotationsRoundTripTests` must use `Tracer.TestHarness` and real SQLite + DuckDB; no mocks for storage.
- E2E tests require the full stack (`tracer-aggregate` + Observer + Vue SPA running on `localhost:5300`).
- All new backend tests must go under the correct subdirectory path listed in §2 of the design.
- Frontend tests must not depend on a running backend (`vi.mock('@/api/useApi')`).

### Success Conditions
1. **Test: SqliteAnnotationStore_Idempotent_InitializeAsync** — Call `InitializeAsync` twice. Assert: schema created successfully; second call does not throw.
2. **Test: SqliteAnnotationStore_CreateThenGet_RoundTrips** — Create an annotation with all fields populated (including tags, author, title). Call `GetAsync` with returned `AnnotationId`. Assert: returned record equals created record field-by-field.
3. **Test: SqliteAnnotationStore_Update_SetsModifiedAt** — Create then update; assert `ModifiedAtUtc > CreatedAtUtc`.
4. **Test: SqliteAnnotationStore_Delete_ReturnsTrueOnce** — Delete once → `true`; delete again → `false`.
5. **Test: SqliteAnnotationStore_List_RespectsLimit** — Insert 10 annotations; query with `Limit = 3`. Assert: exactly 3 returned.
6. **Test: BundleAnnotationStore_MissingFile_ReturnsEmpty** — Point store at a directory with no `annotations/annotations.json`. Call `ListAsync`. Assert: empty list, no exception.
7. **Test: BundleAnnotationStore_WriteOps_Throw** — Call `CreateAsync`, `UpdateAsync`, `DeleteAsync` each. Assert: `InvalidOperationException` in each case.
8. **Test: AnnotationEndpoints_POST_ValidBody_Returns201** — POST to `/api/annotations` with a valid `CreateAnnotationDto` (one target, non-empty body). Assert: HTTP 201, `Location` header present.
9. **Test: AnnotationEndpoints_POST_MultipleTargets_Returns400** — POST with both `eventId` and `entityId` set. Assert: HTTP 400.
10. **Test: AnnotationEndpoints_BundleMode_POST_Returns405** — Configure test host in bundle (read-only) mode. POST. Assert: HTTP 405 with `ProblemDetails` title containing "read-only".
11. **Test: TriggerEvalService_FiltersToTriggerTopic** — Seed events with mixed topics including `scenario.trigger_evaluated`. Query service. Assert: only `scenario.trigger_evaluated` events returned.
12. **Test: TriggerEvalService_MalformedPayload_ReturnsDegradedRow** — Seed one event with `topic='scenario.trigger_evaluated'` and invalid JSON payload. Query service. Assert: result contains one entry with `triggerId == "(malformed payload)"`; no exception.
13. **Test: AnnotationsRoundTrip_LiveToBundleToOffline** — Create 3 annotations via Observer API. Run aggregator. Start offline viewer on bundle. GET `/api/annotations?sessionId=...`. Assert: 3 annotations returned matching the originals.
14. **Test: AnnotationsRoundTrip_BundleMode_PostReturns405** — After round-trip bundle is open in offline viewer: POST to `/api/annotations`. Assert: HTTP 405.
15. **Test: E2E_CreateAnnotation_PersistsAfterReload** — Playwright: create annotation on event via inspector; reload; assert `.annotation-marker` visible in timeline.
16. **Test: E2E_SavedView_RestoresFilterState** — Playwright: save a view with `topic=weapons.fire`; navigate to saved-views list; click the saved view; assert URL contains `topic=weapons.fire`.
17. **Test: E2E_PersonaSwitcher_EngineerLandsOnTimeline** — Playwright: set persona to Engineer; click first session card; assert URL matches `/v/timeline/`.

<!-- PHASE 8 TASKS END -->

<!-- PHASE 9 TASKS BEGIN -->

# Phase 9 — Replication Latency, Gap Detection, Network Topology

---

## TRC-P9-001 — `LatencyBudget` and Core Latency Types

**Phase:** 9 — Replication Latency, Gap Detection, Network Topology  
**Design reference:** [tracer_phase9_design.md §3 — The Per-Subscriber Data Shape](./tracer_phase9_design.md#3-the-per-subscriber-data-shape), [§6.1 Latency Budgets](./tracer_phase9_design.md#61-latency-budgets), [§6.2 Budget Storage in the Bundle](./tracer_phase9_design.md#62-budget-storage-in-the-bundle), [§1.1 What Phase 9 Delivers (Latency Budgets)](./tracer_phase9_design.md#11-what-phase-9-delivers)

### Scope

**In scope:**
- New `LatencyBudget.cs` record in `Tracer.Core/Domain/` with properties: `Topic` (string, required), `P99BudgetMs` (double?, nullable), `AbsoluteMaxMs` (double?, nullable)
- The record follows the pattern of existing domain types: `sealed record`, `required` init-only properties, no third-party dependencies
- Unit tests in `Tracer.Tests.Unit` verifying construction, equality, and null-budget sentinel handling

**Out of scope:**
- Budget persistence, reading, or writing (handled in TRC-P9-009 `BudgetService`)
- Any endpoint or DTO changes
- Frontend types (TypeScript equivalents are defined per Phase 9 frontend tasks)

### Constraints
- `Tracer.Core` must remain free of all third-party package references (Directory.Build.props policy, Phase 1 §2.2)
- Properties must be nullable (`double?`), not defaulted to zero, so callers can distinguish "no budget declared" from "budget is 0ms"

### Success Conditions
1. **Test: LatencyBudget_RequiredTopic_ConstructsCorrectly** — Construct `new LatencyBudget { Topic = "weapons.fire", P99BudgetMs = 50.0, AbsoluteMaxMs = 200.0 }`. Assert: all properties match supplied values.
2. **Test: LatencyBudget_NullableBudgets_AreNull** — Construct with only `Topic` set; `P99BudgetMs` and `AbsoluteMaxMs` omitted. Assert: both are `null`, not zero.
3. **Test: LatencyBudget_Equality_SameValues** — Two records with identical properties are value-equal (`==` returns true).
4. **Test: LatencyBudget_Equality_DifferentTopic** — Two records differing only in `Topic` are not equal.
5. **Test: LatencyBudget_NoBudget_NullIsDistinctFromZero** — Assert `new LatencyBudget { Topic = "x" }.P99BudgetMs is null` and `null != 0.0`.
6. `dotnet build Tracer.Core --configuration Release` succeeds with zero warnings.
7. `Tracer.Core.csproj` contains no `<PackageReference>` entries after this task.

---

## TRC-P9-002 — `FakeNetworkModel` — Synthetic Per-Subscriber Receive Times

**Phase:** 9 — Replication Latency, Gap Detection, Network Topology  
**Design reference:** [tracer_phase9_design.md §13 — FakeNode Network Simulation](./tracer_phase9_design.md#13-fakenode-network-simulation), [§13.1 FakeNetworkModel](./tracer_phase9_design.md#131-fakenetworkmodel), [§13.2 Integration into Test Fixtures](./tracer_phase9_design.md#132-integration-into-test-fixtures), [§2 Project Layout Additions](./tracer_phase9_design.md#2-project-layout-additions)

### Scope

**In scope:**
- New `FakeNetworkModel.cs` in `Tracer.Adapters.Mock/`
- Constructor accepts `IReadOnlyList<string> allNodes` and `int seed`; initializes per-link `LinkProfile` records (sealed private record) with `BaseLatencyMs`, `JitterStdMs`, `DropProbability`, `SpikeProbability`, `SpikeAdditionalMs`
- 15% of links are assigned a "bad" profile (elevated base latency and jitter) as described in §13.1
- `SimulateDelivery(string publisherNode, DateTimeOffset publishWallclock, IReadOnlyList<string> subscriberNodes)` returns `IEnumerable<(string subscriberNode, DateTimeOffset receiveWallclock)>`:
  - Self-subscribe rows: near-zero additional latency (< 200 µs)
  - Simulated drops: yield omitted (no entry for that subscriber)
  - Normal delivery: Box–Muller normal jitter + occasional spike
- `SampleNormal` helper uses Box–Muller transform as shown in §13.1
- Integration test fixtures in `Tracer.Tests.Integration` use `FakeNetworkModel` to produce four bundle profiles: healthy, degraded (one high-latency link), lossy (elevated drops), spike (occasional 100ms+ spikes)
- Unit tests verifying: deterministic output given same seed, self-subscribe latency < 1ms, drop probability respected (over 10,000 calls, drop rate within 2× of configured rate), spike events detectable in 100,000 samples

**Out of scope:**
- FakeNode transport-layer changes (FakeNetworkModel is invoked by test fixtures; no changes to FakeNode's live ingestion path)
- Frontend test data generation
- Real DDS adapter integration (Phase 11)

### Constraints
- Must be deterministic given the same `seed` — required for reproducible integration tests
- No third-party dependencies; uses only `System.Random` and `System.Math`

### Success Conditions
1. **Test: FakeNetworkModel_SameSeed_DeterministicOutput** — Construct two `FakeNetworkModel` instances with identical `allNodes` and `seed=42`; call `SimulateDelivery` with identical inputs on both. Assert: `subscriberNode`/`receiveWallclock` sequences are identical.
2. **Test: FakeNetworkModel_SelfSubscribe_LowLatency** — Call `SimulateDelivery` where `publisherNode` is in `subscriberNodes`. Assert: the self-subscribe entry's `receiveWallclock - publishWallclock` is < 1 ms.
3. **Test: FakeNetworkModel_Drop_NotReturned** — Over 100,000 deliveries on a link with `DropProbability = 0.01`, count returned entries. Assert: omitted entries are between 0.5% and 2% of calls (within 2× of declared rate).
4. **Test: FakeNetworkModel_BadLink_ElevatedP99** — Construct model, identify a "bad" link by checking that its baseline > 5 ms. Over 1,000 deliveries, compute p99. Assert: p99 > 10 ms.
5. **Test: FakeNetworkModel_Spike_ElevatedTail** — Over 100,000 deliveries on a spike-configured profile (`SpikeProbability = 0.001`), assert at least one delivery's latency > `SpikeAdditionalMs * 0.5`.
6. Integration test fixture `HealthyNetworkFixture` using `FakeNetworkModel(seed=1)` produces a bundle in which `GET /api/latency/distribution` returns p99 < 5 ms for all pairs.
7. Integration test fixture `DegradedNetworkFixture` produces a bundle in which at least one (publisher, subscriber) pair reports p99 > 15 ms.

---

## TRC-P9-003 — `QuantileSink` and `HistogramSink` Utilities

**Phase:** 9 — Replication Latency, Gap Detection, Network Topology  
**Design reference:** [tracer_phase9_design.md §4.2 DuckDB's Built-in Statistics](./tracer_phase9_design.md#42-duckdbs-built-in-statistics), [§2 Project Layout Additions](./tracer_phase9_design.md#2-project-layout-additions), [§14.1 Backend Unit Tests — QuantileSinkTests / HistogramSinkTests](./tracer_phase9_design.md#141-backend-unit-tests)

### Scope

**In scope:**
- `QuantileSink.cs` in `Tracer.WebApi/Util/`:
  - Streaming reservoir-sampling approximate quantile computation
  - Public API: `void Add(double value)`, `double GetQuantile(double q)` (q ∈ [0,1]), `long Count`
  - Default reservoir size: 10,000 samples; configurable via constructor parameter
  - Reservoir sampling uses Algorithm R (uniform random replacement once full)
  - Used as a fallback path and in tests; primary path is DuckDB `APPROX_QUANTILE`
- `HistogramSink.cs` in `Tracer.WebApi/Util/`:
  - Log-bucket histogram aggregator (same bucketing as §4.2: `FLOOR(LOG2(GREATEST(latency_ms, 0.001)) * 4)`)
  - Public API: `void Add(double valueMs)`, `IReadOnlyList<HistogramBucket> GetBuckets()`
  - Returns only non-empty buckets
  - Computes `(LowMs, HighMs)` bounds per bucket as `(2^(index/4), 2^((index+1)/4))`
- Unit tests in `Tracer.Tests.Unit/Util/`

**Out of scope:**
- t-digest or DDSketch implementations (reservoir sampling is sufficient for Phase 9 scale)
- Persistence or serialization of sink state
- Integration with DuckDB query path (sinks are standalone; DuckDB is the primary path)

### Constraints
- No third-party dependencies
- `QuantileSink.GetQuantile` must sort the reservoir before returning; sorting occurs on-demand, not on every `Add`
- Both classes must be thread-unsafe by design (used only in single-threaded query paths); document this in XML doc comments

### Success Conditions
1. **Test: QuantileSink_Empty_ThrowsOrReturnsNaN** — Call `GetQuantile(0.99)` on an empty sink. Assert: either `InvalidOperationException` thrown or `double.NaN` returned (consistent behaviour, documented in API).
2. **Test: QuantileSink_KnownDistribution_P50Accurate** — Add values 1..1000 (uniform). Call `GetQuantile(0.5)`. Assert: result is in [490, 510] (within 2% of true median 500.5).
3. **Test: QuantileSink_KnownDistribution_P99Accurate** — Same uniform input. `GetQuantile(0.99)`. Assert: result is in [980, 1000].
4. **Test: QuantileSink_ReservoirFull_OlderValuesReplaced** — Add 20,000 values (> default 10,000 reservoir); assert `Count == 20000` but internal reservoir size does not exceed 10,000.
5. **Test: HistogramSink_Empty_ReturnsNoBuckets** — `GetBuckets()` on empty sink returns empty list.
6. **Test: HistogramSink_SingleValue_OneBucket** — `Add(2.0)`. Assert: exactly one bucket, count = 1; `LowMs <= 2.0 <= HighMs`.
7. **Test: HistogramSink_BucketBounds_Logarithmic** — Add values `[1.0, 2.0, 4.0, 8.0]`. Assert: each falls in its own distinct bucket; `HighMs / LowMs ≈ 2^(1/4)` (≈ 1.189) for each bucket.
8. **Test: HistogramSink_NegativeAndNearZero_ClampedToMinBucket** — Add values `[-0.5, 0.0, 0.0001]`. Assert: no exception; all land in the lowest bucket (≤ 0.001 ms clamped to `GREATEST(x, 0.001)`).
9. **Test: HistogramSink_TotalCount_MatchesAdds** — Add 500 values. Assert: sum of all bucket `Count` values == 500.

---

## TRC-P9-004 — `LatencyDistributionService`

**Phase:** 9 — Replication Latency, Gap Detection, Network Topology  
**Design reference:** [tracer_phase9_design.md §4 — Backend: Latency Distribution Service](./tracer_phase9_design.md#4-backend-latency-distribution-service), [§4.3 LatencyDistributionService](./tracer_phase9_design.md#43-latencydistributionservice), [§4.4 The Per-Tuple Aggregate Query](./tracer_phase9_design.md#44-the-per-tuple-aggregate-query), [§3.2 The Self-Subscribe Row](./tracer_phase9_design.md#32-the-self-subscribe-row)

### Scope

**In scope:**
- `LatencyDistributionService.cs` in `Tracer.WebApi/Queries/`
- `GetAsync(LatencyQuery query, CancellationToken ct)` → `LatencyDistribution`:
  - DuckDB SQL with `APPROX_QUANTILE` for p50/p90/p99/p99.9, `MAX`, `MIN`, `AVG`, `STDDEV_POP`
  - Histogram using `FLOOR(LOG2(GREATEST(latency_ms, 0.001)) * 4)` bucketing; returns `IReadOnlyList<HistogramBucket>`
  - `ExcludeSelfSubscribe` (default `true`): adds `publisher_node != subscriber_node` WHERE clause
  - Dynamic WHERE clause composition for `Topic`, `PublisherNode`, `SubscriberNode`, `From`, `To`
- `ListByPairAsync(WallclockTime from, WallclockTime to, int minSamples, int limit, CancellationToken ct)` → `IReadOnlyList<LatencyPairSummary>`:
  - Returns per-(topic, publisher, subscriber) p50/p99/max/count, sorted by p99 DESC
  - Filters tuples with fewer than `minSamples` events (default 50)
- Domain record types: `LatencyQuery`, `LatencyDistribution`, `HistogramBucket`, `LatencyPairSummary` (in `Tracer.WebApi/Queries/`)
- Unit tests for all filter combinations and edge cases

**Out of scope:**
- DTO mapping (handled in TRC-P9-010)
- HTTP endpoints (TRC-P9-010)
- Self-subscribe toggle in UI (frontend task)

### Constraints
- **Bundle-mode only**: service uses `LiveMultiIntervalReader`; calling it against a live Observer is not gated here — the mode gate is applied at the endpoint layer (TRC-P9-010). The service itself does not check mode.
- Negative latency values must NOT be filtered — they are kept and included in all statistics and buckets (§3.3 documents this as intentional)
- `SampleCount == 0` returns an empty `Buckets` list and zero-value statistics; must not throw

### Success Conditions
1. **Test: LatencyDistributionService_EmptyBundle_ZeroCount** — Bundle with no events. Call `GetAsync`. Assert: `SampleCount == 0`, `Buckets` is empty, no exception.
2. **Test: LatencyDistributionService_SingleSample_AllPercentilesEqual** — Bundle with one event, 5ms latency. Assert: `P50Ms == P90Ms == P99Ms == P999Ms ≈ 5.0`, `SampleCount == 1`, one bucket.
3. **Test: LatencyDistributionService_ExcludeSelf_Filters** — Bundle with 4 events: 2 where `publisher_node == subscriber_node`, 2 where they differ. Call with `ExcludeSelfSubscribe = true`. Assert: `SampleCount == 2`. Repeat with `false`. Assert: `SampleCount == 4`.
4. **Test: LatencyDistributionService_TopicFilter_Isolates** — Bundle with events on two topics. Call with `Topic = "weapons.fire"`. Assert: returned samples match only that topic.
5. **Test: LatencyDistributionService_TimeRange_Respected** — Bundle with events spread across 60 minutes. Query a 10-minute sub-range. Assert: `SampleCount` matches only that window.
6. **Test: LatencyDistributionService_NegativeLatency_Included** — Bundle with one event where `receive_wallclock < publish_wallclock` (clock skew simulation). Assert: `SampleCount == 1`, `MinMs < 0`, no exception.
7. **Test: LatencyDistributionService_BucketBounds_AreLogarithmic** — Uniform-latency bundle. Assert: for each returned bucket, `HighMs / LowMs ≈ 2^(1/4)`.
8. **Test: LatencyDistributionService_ListByPair_SortedByP99Desc** — Bundle with 3 tuples with distinct p99s. Assert: returned list is sorted descending by `P99Ms`.
9. **Test: LatencyDistributionService_ListByPair_MinSamplesFilter** — One tuple has 10 events, another has 100. Call with `minSamples = 50`. Assert: only the 100-event tuple is returned.

---

## TRC-P9-005 — `LatencyTimeSeriesService`

**Phase:** 9 — Replication Latency, Gap Detection, Network Topology  
**Design reference:** [tracer_phase9_design.md §5 — Backend: Latency Time-Series Service](./tracer_phase9_design.md#5-backend-latency-time-series-service), [§5.2 The Query](./tracer_phase9_design.md#52-the-query), [§5.3 Service](./tracer_phase9_design.md#53-service)

### Scope

**In scope:**
- `LatencyTimeSeriesService.cs` in `Tracer.WebApi/Queries/`
- `GetAsync(LatencyTimeSeriesQuery query, CancellationToken ct)` → `LatencyTimeSeries`:
  - DuckDB `time_bucket` aggregation producing `(bucket_start, p50, p99, count)` per bucket
  - `ChooseBucket(double spanMs)` private method selecting bucket size from session span: ≥ 4h → `5 minutes`; ≥ 1h → `1 minute`; ≥ 30m → `30 seconds`; ≥ 5m → `10 seconds`; ≥ 1m → `1 second`; default → `100 milliseconds`
  - Empty buckets are not emitted (DuckDB GROUP BY naturally omits empty groups)
  - Dynamic filter composition identical to `LatencyDistributionService`: `Topic`, `PublisherNode`, `SubscriberNode`, `From`, `To`, `ExcludeSelfSubscribe`
- Domain records: `LatencyTimeSeriesQuery`, `LatencyTimeSeries`, `LatencyTimePoint` (in `Tracer.WebApi/Queries/`)
- Unit tests covering bucket-size selection and time-series shape

**Out of scope:**
- DTO mapping and HTTP endpoint (TRC-P9-010)
- Frontend chart rendering

### Constraints
- **Bundle-mode only**: mode gate applied at endpoint layer (TRC-P9-010); service does not check mode
- Bucket size must be deterministic given session span — no random or environment-dependent selection

### Success Conditions
1. **Test: LatencyTimeSeriesService_EmptyBundle_EmptyPoints** — Bundle with no events. Assert: `Points` is empty, no exception.
2. **Test: LatencyTimeSeriesService_OneHourSession_OneMinuteBuckets** — Session span 60 minutes. Assert: `BucketSize == "1 minute"`.
3. **Test: LatencyTimeSeriesService_FourHourSession_FiveMinuteBuckets** — Session span 4 hours. Assert: `BucketSize == "5 minutes"`.
4. **Test: LatencyTimeSeriesService_SubMinuteSession_HundredMsBuckets** — Session span 30 seconds. Assert: `BucketSize == "1 second"`. Session span 45 seconds. Assert: `BucketSize == "10 seconds"`.
5. **Test: LatencyTimeSeriesService_BucketCounts_SumToTotal** — Insert 120 events evenly across a 2-hour span. Assert: sum of all `Point.SampleCount` values == 120.
6. **Test: LatencyTimeSeriesService_EmptyBuckets_NotEmitted** — Insert events in first and last 5-minute window only; leave the middle 50 minutes empty. Assert: only 2 buckets returned.
7. **Test: LatencyTimeSeriesService_P99_PlausibleAgainstInput** — Insert 100 events with known latency distribution into a single bucket. Assert: bucket's `P99Ms` is within 5% of the true 99th percentile of the input.
8. **Test: LatencyTimeSeriesService_LiveMode_Returns409** — No bundle open (`BundleOpenManager` not registered). Call endpoint. Assert: HTTP 409 with `detail` containing "bundle mode".

---

## TRC-P9-006 — `LatencyOutlierService`

**Phase:** 9 — Replication Latency, Gap Detection, Network Topology  
**Design reference:** [tracer_phase9_design.md §6.4 LatencyOutlierService](./tracer_phase9_design.md#64-latencyoutlierservice), [§6.1 Latency Budgets](./tracer_phase9_design.md#61-latency-budgets), [§1.1 What Phase 9 Delivers (Outlier Identification)](./tracer_phase9_design.md#11-what-phase-9-delivers)

### Scope

**In scope:**
- `LatencyOutlierService.cs` in `Tracer.WebApi/Queries/`
- `FindAsync(LatencyOutlierQuery query, CancellationToken ct)` → `LatencyOutlierResult`:
  - If `query.ThresholdMs` is set: simple `WHERE latency_ms > threshold` path
  - If `query.ThresholdMs` is null: per-topic threshold from `BudgetService.GetBudgetsAsync` (uses `AbsoluteMaxMs`), falling back to per-topic `APPROX_QUANTILE(latency_ms, 0.999)` when no budget exists
  - Always excludes `publisher_node == subscriber_node` rows
  - `BudgetSource` field on each outlier: `"budget"` when `AbsoluteMaxMs` was used, `"top-0.1%"` when fallback applied
  - Returns up to `query.Limit` outliers (default 100), sorted by `latency_ms DESC`
  - Result includes the list of budgets used (for the frontend to display threshold lines)
- Depends on `BudgetService` (TRC-P9-009); injected via constructor
- Domain records: `LatencyOutlierQuery`, `LatencyOutlier`, `LatencyOutlierResult` (in `Tracer.WebApi/Queries/`)
- Unit tests for all threshold-source paths

**Out of scope:**
- DTO mapping and HTTP endpoint (TRC-P9-010)
- Frontend outlier table rendering

### Constraints
- **Bundle-mode only**: mode gate at endpoint layer (TRC-P9-010)
- Must exclude `publisher_node == subscriber_node` rows unconditionally — self-subscribe latencies are not meaningful outliers
- When no events exceed the threshold, returns empty `Outliers` list — not an error

### Success Conditions
1. **Test: LatencyOutlierService_ExplicitThreshold_ReturnsAboveOnly** — Bundle with events at 5, 10, 50, 100 ms. Call with `ThresholdMs = 20`. Assert: only 50ms and 100ms events returned.
2. **Test: LatencyOutlierService_ExplicitThreshold_SortedDesc** — Call with threshold. Assert: `Outliers[0].LatencyMs >= Outliers[1].LatencyMs` (descending).
3. **Test: LatencyOutlierService_NoBudget_Top0_1Pct** — Bundle with 1000 events, no budget declared. Assert: events with latency > `APPROX_QUANTILE(0.999)` are returned; `BudgetSource == "top-0.1%"`.
4. **Test: LatencyOutlierService_WithBudget_UsesAbsoluteMax** — Bundle with budget `AbsoluteMaxMs = 50`. Assert: events > 50ms returned; `BudgetSource == "budget"`.
5. **Test: LatencyOutlierService_PerTopicBudgets_Applied** — Bundle with two topics; topic A budget = 30ms, topic B budget = 80ms. Assert: returned outliers for topic A exceed 30ms; for topic B exceed 80ms (not 30ms).
6. **Test: LatencyOutlierService_SelfSubscribe_Excluded** — Bundle with self-subscribe events at 200ms latency (clock-noise artifact). Assert: these events do not appear in outlier results.
7. **Test: LatencyOutlierService_NoOutliers_EmptyResult** — Bundle with all events well within budget. Assert: empty `Outliers` list, no exception.
8. **Test: LatencyOutlierService_Limit_Respected** — Bundle with 500 events all exceeding threshold. Call with `Limit = 10`. Assert: exactly 10 returned.
9. **Test: LatencyOutlierService_LiveMode_Returns409** — No bundle open. Call endpoint. Assert: HTTP 409.

---

## TRC-P9-007 — `GapDetectionService`

**Phase:** 9 — Replication Latency, Gap Detection, Network Topology  
**Design reference:** [tracer_phase9_design.md §7 — Backend: Gap Detection Service](./tracer_phase9_design.md#7-backend-gap-detection-service), [§7.2 The Algorithm](./tracer_phase9_design.md#72-the-algorithm), [§7.3 Service](./tracer_phase9_design.md#73-service), [§7.4 First-Sample Edge Case](./tracer_phase9_design.md#74-first-sample-edge-case)

### Scope

**In scope:**
- `GapDetectionService.cs` in `Tracer.WebApi/Queries/`
- `FindGapsAsync(GapDetectionQuery query, CancellationToken ct)` → `GapDetectionResult`:
  - DuckDB window function `LAG(sequence_number) OVER (PARTITION BY topic, publisher_node, subscriber_node ORDER BY sequence_number)` to detect discontinuities
  - Reports each gap as: `Topic`, `PublisherNode`, `SubscriberNode`, `ResumedAtSequence`, `PreviousSequence`, `MissingCount`, `ResumedAtWallclockUtc`
  - Filters to `gap_size > 1` (gaps of exactly 1 means next sequential number; NULL LAG is first row — filtered)
  - Dynamic WHERE: `Topic`, `PublisherNode`, `SubscriberNode`, `From`, `To`; always excludes `publisher_node == subscriber_node`
  - Result sorted by `missing DESC, publish_wallclock`; limited to `query.Limit` (default 500, max 5000)
  - First-sample edge case (§7.4): reported as-is with `PreviousSequence = 0`; not filtered; documented behavior
- Domain records: `GapDetectionQuery`, `Gap`, `GapDetectionResult` (in `Tracer.WebApi/Queries/`)
- Unit tests for gap detection, edge cases, and filter combinations

**Out of scope:**
- DTO mapping and HTTP endpoint (TRC-P9-010)
- Frontend gap list rendering
- Cross-referencing subscriber-join events to filter first-sample pseudo-gaps (deferred, §7.4)

### Constraints
- **Bundle-mode only**: mode gate at endpoint layer (TRC-P9-010)
- The first-sample edge case (PreviousSequence = 0) is **intentionally reported** — the frontend UI handles this with a toggle; the service must not suppress it
- `publisher_node == subscriber_node` rows excluded unconditionally (self-subscribe sequence-number gaps are a DDS internals concern, not a network gap)

### Success Conditions
1. **Test: GapDetectionService_ContinuousSequence_NoGaps** — Bundle with events seq 1, 2, 3, 4, 5 for one (topic, pub, sub) tuple. Assert: `Gaps` is empty.
2. **Test: GapDetectionService_SingleGap_Detected** — Bundle with events seq 1, 2, 5 (missing 3, 4). Assert: one gap with `PreviousSequence = 2`, `ResumedAtSequence = 5`, `MissingCount = 2`.
3. **Test: GapDetectionService_MultipleGaps_AllReported** — Bundle with three discontinuities. Assert: three gap entries.
4. **Test: GapDetectionService_FirstSample_ReportedWithZeroPrevious** — Bundle where subscriber B's first event has seq = 10 (joined late). Assert: gap reported with `PreviousSequence = 0`, `ResumedAtSequence = 10`, `MissingCount = 9`.
5. **Test: GapDetectionService_TupleFilter_Isolates** — Bundle with gaps in two different (topic, pub, sub) tuples. Call with one tuple's filters. Assert: only that tuple's gaps returned.
6. **Test: GapDetectionService_SelfSubscribe_Excluded** — Bundle with self-subscribe rows that have gap. Assert: self-subscribe rows do not appear in results.
7. **Test: GapDetectionService_TimeRange_Respected** — Bundle with gaps in first and second half. Query second half only. Assert: first-half gaps absent.
8. **Test: GapDetectionService_SortedByMissingDesc** — Bundle with gaps of size 3, 10, 1. Assert: returned order is 10, 3, 1.
9. **Test: GapDetectionService_LiveMode_Returns409** — No bundle open. Call endpoint. Assert: HTTP 409.

---

## TRC-P9-008 — `TopologyService`

**Phase:** 9 — Replication Latency, Gap Detection, Network Topology  
**Design reference:** [tracer_phase9_design.md §8 — Backend: Topology Service](./tracer_phase9_design.md#8-backend-topology-service), [§8.1 What This Returns](./tracer_phase9_design.md#81-what-this-returns), [§8.2 Service](./tracer_phase9_design.md#82-service)

### Scope

**In scope:**
- `TopologyService.cs` in `Tracer.WebApi/Queries/` (extends or replaces Phase 3 stub if present)
- `GetNetworkTopologyAsync(string sessionId, WallclockTime from, WallclockTime to, CancellationToken ct)` → `NetworkTopology`:
  - DuckDB GROUP BY `(topic, publisher_node, subscriber_node)` with `COUNT(*)`, `MIN(publish_wallclock)`, `MAX(publish_wallclock)`
  - Excludes `publisher_node == subscriber_node` rows
  - Result sorted by `message_count DESC`
  - Produces `NetworkTopology` with `IReadOnlyList<string> Nodes` (distinct union of publisher and subscriber nodes, sorted) and `IReadOnlyList<TopologyEdge> Edges`
- Domain records: `NetworkTopology`, `TopologyEdge` (in `Tracer.WebApi/Queries/`)
- Unit tests for edge cases

**Out of scope:**
- DTO mapping and HTTP endpoint (TRC-P9-010)
- Frontend graph layout (`networkGraphLayout.ts` is a frontend task)
- Anomaly detection ("node should be subscribing but isn't") — deferred

### Constraints
- **Bundle-mode only**: mode gate at endpoint layer (TRC-P9-010)
- Self-subscribe edges excluded unconditionally; do not appear in `Edges` or contribute to `Nodes` count
- Performance target: full-session topology query < 200 ms (§1.3 criterion 9)

### Success Conditions
1. **Test: TopologyService_ThreeNodeBundle_CorrectEdges** — Bundle with nodes A, B, C; edges A→B and A→C on topic `foo`. Assert: 2 edges, 3 nodes (`{A, B, C}`).
2. **Test: TopologyService_SelfSubscribe_Excluded** — Bundle with A→A rows. Assert: `Edges` empty (only self-subscribe rows in bundle).
3. **Test: TopologyService_MessageCount_Aggregated** — Bundle with 5 events A→B and 3 events A→C. Assert: `Edges[0].MessageCount == 5`, `Edges[1].MessageCount == 3` (sorted DESC).
4. **Test: TopologyService_Nodes_AreUnionOfPubAndSub** — Bundle with edges A→B and C→B. Assert: `Nodes` contains `{A, B, C}`, sorted alphabetically.
5. **Test: TopologyService_FirstLastSeen_Accurate** — Bundle with events spanning 10 minutes. Assert: `FirstSeenUtc < LastSeenUtc`; both within the session time range.
6. **Test: TopologyService_MultiTopic_EachTopicHasOwnEdge** — Bundle with A→B on both `topic1` and `topic2`. Assert: 2 edges (not merged), each with its own `Topic`.
7. **Test: TopologyService_EmptyBundle_EmptyResult** — Assert: `Nodes` and `Edges` both empty, no exception.
8. **Test: TopologyService_LiveMode_Returns409** — No bundle open. Call endpoint. Assert: HTTP 409.

---

## TRC-P9-009 — `BudgetService`

**Phase:** 9 — Replication Latency, Gap Detection, Network Topology  
**Design reference:** [tracer_phase9_design.md §6.3 BudgetService](./tracer_phase9_design.md#63-budgetservice), [§6.2 Budget Storage in the Bundle](./tracer_phase9_design.md#62-budget-storage-in-the-bundle), [§1.1 What Phase 9 Delivers (Latency Budgets)](./tracer_phase9_design.md#11-what-phase-9-delivers)

### Scope

**In scope:**
- `BudgetService.cs` in `Tracer.WebApi/Queries/`
- `GetBudgetsAsync(string sessionId, CancellationToken ct)` → `IReadOnlyList<LatencyBudget>`:
  - Bundle mode: reads `latencyBudgets` array from `{bundle.WorkingDirectory}/metadata.json`; deserializes to `IReadOnlyList<LatencyBudget>`
  - Returns empty list if `metadata.json` absent, if `latencyBudgets` key missing, or if the array is empty
  - Live mode: returns empty list (or from `InMemoryBudgetRegistry` if registered — registry support is a stub; not required to be fully functional in Phase 9)
- Constructor accepts `BundleOpenManager?` (nullable) and `InMemoryBudgetRegistry?` (nullable) — both optional to support both modes without conditional DI registration
- `InMemoryBudgetRegistry.cs` stub in `Tracer.WebApi/Queries/`: `GetAll()` returns `IReadOnlyList<LatencyBudget>` from an in-memory list; `Register(LatencyBudget budget)` adds to the list; no persistence
- Unit tests for all read paths

**Out of scope:**
- Budget editing or writing (Tracer is read-only for budgets, §1.2)
- Ingestion of `scenario.metadata.latency_budgets` DDS events (Phase 11+)
- DTO mapping and HTTP endpoint (TRC-P9-010)

### Constraints
- Reads `metadata.json` with `System.Text.Json`; must handle malformed JSON gracefully (log and return empty, do not throw to callers)
- `GetBudgetsAsync` must never throw; exceptions from file I/O or JSON parsing are caught and logged

### Success Conditions
1. **Test: BudgetService_BundleWithBudgets_ReturnsParsedList** — Write `metadata.json` with `latencyBudgets: [{ topic: "x", p99BudgetMs: 30, absoluteMaxMs: 100 }]`. Call `GetBudgetsAsync`. Assert: one `LatencyBudget` with correct field values.
2. **Test: BudgetService_NoBudgetsSection_ReturnsEmpty** — Write `metadata.json` without `latencyBudgets` key. Assert: empty list.
3. **Test: BudgetService_MetadataFileMissing_ReturnsEmpty** — Point service at a directory with no `metadata.json`. Assert: empty list, no exception.
4. **Test: BudgetService_MalformedJson_ReturnsEmpty** — Write malformed JSON. Assert: empty list, no exception (logs the error).
5. **Test: BudgetService_NullableFields_PreservedAsNull** — Budget entry with only `topic` key (no `p99BudgetMs`, no `absoluteMaxMs`). Assert: `P99BudgetMs == null`, `AbsoluteMaxMs == null`.
6. **Test: BudgetService_LiveMode_ReturnsEmpty** — `BundleOpenManager` is null. Assert: empty list.
7. **Test: BudgetService_InMemoryRegistry_ReturnsRegistered** — Register two budgets in `InMemoryBudgetRegistry`; pass it to service with null `BundleOpenManager`. Assert: two budgets returned.
8. **Test: BudgetService_MultipleBudgets_AllReturned** — Write `metadata.json` with 5 budget entries. Assert: all 5 returned.

---

## TRC-P9-010 — Phase 9 API Endpoints, DTOs, `BundleModeGate`, and DI Wiring

**Phase:** 9 — Replication Latency, Gap Detection, Network Topology  
**Design reference:** [tracer_phase9_design.md §9 — Web API Endpoints and Mode Gating](./tracer_phase9_design.md#9-web-api-endpoints-and-mode-gating), [§9.1 Endpoint Surface](./tracer_phase9_design.md#91-endpoint-surface), [§9.2 Mode Gate](./tracer_phase9_design.md#92-mode-gate), [§9.3 LatencyEndpoints](./tracer_phase9_design.md#93-latencyendpoints), [§9.4 GapEndpoints and TopologyEndpoints](./tracer_phase9_design.md#94-gapendpoints-and-topologyendpoints), [§9.5 DTOs](./tracer_phase9_design.md#95-dtos), [§3.4 Index Considerations](./tracer_phase9_design.md#34-index-considerations)

### Scope

**In scope:**
- `Tracer.WebApi/Util/BundleModeGate.cs`: static helper `CheckBundleOrLive(IServiceProvider sp)` → `IResult?`; returns `ProblemDetails` 409 when `BundleOpenManager` is absent from DI, null otherwise
- `Tracer.WebApi/Endpoints/LatencyEndpoints.cs`: `Map(WebApplication app)` registering:
  - `GET /api/latency/distribution` → `HandleDistributionAsync` (query params: `sessionId`, `from`, `to`, `topic?`, `publisherNode?`, `subscriberNode?`, `excludeSelf=true`)
  - `GET /api/latency/pairs` → `HandlePairsAsync` (query params: `sessionId`, `from`, `to`, `minSamples=50`, `limit=100`)
  - `GET /api/latency/timeseries` → `HandleTimeSeriesAsync` (query params: `sessionId`, `from`, `to`, `topic?`, `publisherNode?`, `subscriberNode?`)
  - `GET /api/latency/outliers` → `HandleOutliersAsync` (query params: `sessionId`, `from`, `to`, `topic?`, `thresholdMs?`, `limit=100`)
- `Tracer.WebApi/Endpoints/GapEndpoints.cs`: `GET /api/gaps` (query params: `sessionId`, `from`, `to`, `topic?`, `publisherNode?`, `subscriberNode?`, `limit=500`)
- `Tracer.WebApi/Endpoints/TopologyEndpoints.cs`: `GET /api/topology/network` (query params: `sessionId`, `from`, `to`)
- `Tracer.WebApi/Endpoints/BudgetEndpoints.cs`: `GET /api/scenario/budgets` (query param: `sessionId`) — **no 409 gate**: budgets endpoint works in both modes (returns empty list in live mode)
- DTO types in `Tracer.WebApi/Contracts/Dto/`: `LatencyDistributionDto`, `HistogramBucketDto`, `LatencyPairSummaryDto`, `LatencyTimeSeriesDto`, `LatencyTimePointDto`, `LatencyOutlierDto`, `LatencyOutlierListDto`, `GapDto`, `GapResultDto`, `TopologyDto`, `TopologyEdgeDto`, `BudgetDto`, `BudgetListDto` — as specified in §9.5
- DTO mapper helpers (static methods or classes) in `Tracer.WebApi/Contracts/Dto/`
- DI registration in `Program.cs` or a `ServiceCollectionExtensions` class: all Phase 9 services (`LatencyDistributionService`, `LatencyTimeSeriesService`, `LatencyOutlierService`, `GapDetectionService`, `TopologyService`, `BudgetService`, `InMemoryBudgetRegistry`)
- All endpoints decorated with `.WithOpenApi()`
- Parameter validation: `limit` clamped to [1, 5000]; `from > to` returns HTTP 400 `ProblemDetails`; missing required params return 400
- `idx_events_topic_pub_sub` index creation in `EventsConsolidator.Finalize` as described in §3.4
- Integration tests in `Tracer.Tests.Integration/LatencyAnalysisRoundTripTests.cs`, `GapDetectionIntegrationTests.cs`, `TopologyIntegrationTests.cs`
- Endpoint unit tests: all Phase 9 endpoints return 409 in live mode; return 200 with valid DTOs in bundle mode

**Out of scope:**
- Frontend integration (separate tasks TRC-P9-011+)
- OpenAPI client code generation triggering (CI concern)

### Constraints
- **All latency, gap, and topology endpoints return HTTP 409** with `ProblemDetails` (`Title = "Bundle mode required"`, `Status = 409`) when called against a live Observer (i.e., when `BundleOpenManager` is not registered in DI). The body detail must include text explaining bundle mode is required (matches §9.2 exactly).
- `GET /api/scenario/budgets` is **exempt from the 409 gate** — it returns an empty list in live mode; this is intentional (§6.3)
- DTO types must be `sealed record` with `required` init-only properties
- All endpoints must have `.WithOpenApi()` — required for TypeScript client generation in the CI pipeline

### Success Conditions
1. **Test: LatencyEndpoints_Distribution_LiveMode_Returns409** — Configure test host without `BundleOpenManager`. GET `/api/latency/distribution`. Assert: HTTP 409, `Content-Type: application/problem+json`, body contains `"Bundle mode required"`.
2. **Test: LatencyEndpoints_Distribution_BundleMode_Returns200** — Configure test host with bundle open, `FakeNetworkModel` fixture events. GET `/api/latency/distribution?sessionId=...&from=...&to=...`. Assert: HTTP 200; `sampleCount > 0`; `p99Ms > 0`; `buckets` non-empty.
3. **Test: GapEndpoints_LiveMode_Returns409** — No bundle open. GET `/api/gaps`. Assert: HTTP 409.
4. **Test: GapEndpoints_BundleMode_GapDetected** — Bundle with injected gaps. GET `/api/gaps?sessionId=...`. Assert: HTTP 200; `gaps` list non-empty; `missingCount > 0`.
5. **Test: TopologyEndpoints_LiveMode_Returns409** — No bundle open. GET `/api/topology/network`. Assert: HTTP 409.
6. **Test: TopologyEndpoints_BundleMode_Returns200** — Multi-node bundle. GET `/api/topology/network?sessionId=...`. Assert: `nodes` and `edges` non-empty.
7. **Test: BudgetEndpoints_LiveMode_Returns200Empty** — No bundle open. GET `/api/scenario/budgets?sessionId=...`. Assert: HTTP 200, `budgets` is empty array (not 409).
8. **Test: BudgetEndpoints_BundleWithBudgets_Returns200List** — Bundle with `latencyBudgets` in metadata. GET `/api/scenario/budgets?sessionId=...`. Assert: HTTP 200; `budgets` contains the declared entries.
9. **Test: LatencyEndpoints_InvalidLimit_Returns400** — GET `/api/latency/outliers?limit=-5`. Assert: HTTP 400 `ProblemDetails`.
10. **Test: LatencyEndpoints_FromAfterTo_Returns400** — GET `/api/latency/distribution?from=<later>&to=<earlier>`. Assert: HTTP 400 `ProblemDetails`.
11. **Test: AllPhase9Endpoints_HaveOpenApi** — Enumerate `app.Services.GetService<IApiDescriptionGroupCollectionProvider>()`. Assert: all 7 Phase 9 routes appear in OpenAPI description groups.
12. **Test: LatencyRoundTrip_FakeNetwork_MatchesExpected** — Build bundle with `HealthyNetworkFixture`; query distribution; assert p99 < 5ms. Build with `DegradedNetworkFixture`; query; assert at least one pair reports p99 > 15ms.
13. **Test: GapRoundTrip_LossyNetwork_GapsDetected** — Build bundle with `LossyNetworkFixture`; query gaps; assert gap count > 0 and `missingCount > 0`.
14. **Test: TopologyRoundTrip_MultiNode_CorrectGraph** — Build bundle with 3-node `FakeNetworkModel` fixture; query topology; assert `nodes.length == 3`, `edges.length >= 2`.
15. **Test: EventsConsolidator_CreatesIndex_OnFinalize** — After bundle consolidation, connect to `events.duckdb`; query `PRAGMA database_list`; assert index `idx_events_topic_pub_sub` exists on `events` table.

*(frontend tasks TRC-P9-011+ to be appended)*

---

## TRC-P9-011 — `ReplicationLatencyView.vue` — Main Latency View

**Phase:** 9 — Replication Latency, Gap Detection, Network Topology  
**Design reference:** [tracer_phase9_design.md §10](./tracer_phase9_design.md#10-frontend-replication-latency-view)

### Scope
**In scope:**
- `src/views/ReplicationLatencyView.vue` mounted at route `/v/latency/:sessionId`; route name `replication-latency`
- On mount: fetch session range (from/to), latency budgets via `GET /api/scenario/budgets`, and pair list via `GET /api/latency/pairs` (minSamples=50, limit=200)
- `selectedPair` reactive ref (initially `null`) — when set, narrows the distribution, time-series, and outlier composables to `{ topic, publisherNode, subscriberNode }` from that pair; × button clears it
- Renders `PublisherSubscriberMatrix`, `LatencyDistributionChart`, `LatencyTimeSeriesChart`, `LatencyOutliersTable` in the three-panel layout (§10.1): matrix left, distribution + timeseries centre, outliers right
- `BundleModeRequiredBanner` rendered (all content panels hidden) when any Phase 9 endpoint returns HTTP 409
- Link to `/v/latency/:sessionId` added to bundle session card
- Route registered in the SPA router

**Out of scope:**
- Sub-components themselves (TRC-P9-012 through TRC-P9-015 and TRC-P9-018)
- Backend endpoints (TRC-P9-010)
- Additional filter dropdowns for topic/pub/sub (pair selection via matrix is sufficient for Phase 9)

### Constraints
- Route path `/v/latency/:sessionId`, route name `replication-latency`
- On any Phase 9 endpoint returning 409: show `BundleModeRequiredBanner` with the detail text from the response; hide all content panels
- `selectedPair` is `null` by default; when set, all three detail composables receive `{ topic, publisherNode, subscriberNode }` from that pair
- Banner message must contain text matching "requires bundle mode" (§9.2)

### Success Conditions
1. **Test: `ReplicationLatencyView_MountsWithPairList`** — Stub API returning a session with start/end and 3 pairs; mount the view; assert `PublisherSubscriberMatrix` receives a `pairs` prop with length 3.
2. **Test: `ReplicationLatencyView_409_ShowsBanner`** — Stub pairs endpoint returning 409; mount view; assert `BundleModeRequiredBanner` is rendered and content panels are absent from the DOM.
3. **Test: `ReplicationLatencyView_SelectPair_UpdatesComposableFilter`** — Mount with 3 pairs; simulate click on the second pair row; assert `selectedPair` equals the second pair object; assert the distribution composable `filter.topic` matches that pair's topic.
4. **Test: `ReplicationLatencyView_ClearPair_ResetsFilter`** — With `selectedPair` set to a pair; click the × button; assert `selectedPair === null`.
5. **Test: E2E `replication-latency-view.spec.ts` "bundle session shows pair matrix"** — Open a bundle session; navigate to `/v/latency/{sessionId}`; assert `.pair-matrix__row` is visible; assert `h1` text is "Replication latency".
6. **Test: E2E `replication-latency-view.spec.ts` "live mode shows bundle required banner"** — Visit `/v/latency/live-session` against a live Observer instance; assert `.bundle-mode-required-banner` is visible; assert the banner contains text "requires bundle mode".

---

## TRC-P9-012 — `LatencyDistributionChart.vue` and `histogramRenderer.ts`

**Phase:** 9 — Replication Latency, Gap Detection, Network Topology  
**Design reference:** [tracer_phase9_design.md §10.3](./tracer_phase9_design.md#103-latencydistributionchartvue)

### Scope
**In scope:**
- `src/rendering/histogramRenderer.ts`: exported `renderHistogram(ctx: CanvasRenderingContext2D, input: HistogramRenderInput)` function
  - Logarithmic x-axis (log10 of latency ms); one bar per bucket using `lowMs`/`highMs` from `HistogramBucketDto`
  - Dashed vertical percentile lines: p50 (`#4ec97a` green), p99 (`#e8b048` amber), p99.9 (`#e85c5c` red); labels at top of plot area
  - Solid thicker vertical budget lines when `budget.absoluteMaxMs` or `budget.p99BudgetMs` is supplied
  - Upper-right summary text: sample count, p50, p99, max
  - Centred "No data in range" message when `sampleCount === 0` or `buckets` array is empty
  - `formatMs(ms: number): string` helper for x-axis tick labels (μs / ms / s)
- `src/components/LatencyDistributionChart.vue`: canvas wrapper that calls `renderHistogram` on prop changes and on canvas resize (ResizeObserver)

**Out of scope:**
- Time-series rendering (TRC-P9-013)
- Data-fetching composable (TRC-P9-018)

### Constraints
- Canvas-based renderer; no third-party chart library
- X-axis uses log10 scale; bucket bar widths derive from `b.lowMs` and `b.highMs`
- Component is display-only; emits no events

### Success Conditions
1. **Test: `histogramRenderer.spec.ts` "EmptyDistribution_DrawsNoDataMessage"** — Pass a distribution with `sampleCount=0`; assert canvas `fillText` was called with "No data in range".
2. **Test: `histogramRenderer.spec.ts` "SingleBucket_DrawsBar"** — Pass one bucket (`count=100, lowMs=1, highMs=2`); assert `fillRect` was called at least once.
3. **Test: `histogramRenderer.spec.ts` "P99Line_DrawnAtCorrectX"** — Pass a distribution with `p99Ms=10`; assert a vertical stroke is drawn at the x-coordinate corresponding to `log10(10)` on the plot scale.
4. **Test: `histogramRenderer.spec.ts` "BudgetLine_DrawnWhenPresent"** — Pass `budget.absoluteMaxMs=50`; assert an additional stroke at the x-coordinate corresponding to 50ms.
5. **Test: `histogramRenderer.spec.ts` "BudgetLine_AbsentWhenBudgetNull"** — Pass `budget=null`; assert no budget-coloured thick vertical stroke is drawn.
6. **Test: `LatencyDistributionChart_ResizeTriggers_Redraw`** — Mount the chart component; trigger the ResizeObserver callback; assert `renderHistogram` is called a second time.

---

## TRC-P9-013 — `LatencyTimeSeriesChart.vue`

**Phase:** 9 — Replication Latency, Gap Detection, Network Topology  
**Design reference:** [tracer_phase9_design.md §5.1](./tracer_phase9_design.md#51-what-this-returns)

### Scope
**In scope:**
- `src/rendering/latencyTimeSeriesRenderer.ts`: `renderTimeSeries(ctx, input)` — two line series over a time x-axis; p50 (dim, dashed) and p99 (bright, solid); y-axis 0 to `maxP99 * 1.1` (minimum 1ms); "No data" message when `points` is empty
- `hitTestTimeSeries(points, mouseX, canvasWidthPx)` helper returning the index of the closest time point for tooltip display
- `src/components/LatencyTimeSeriesChart.vue`: canvas wrapper
  - Props: `timeseries: LatencyTimeSeriesDto | null`, `loading: boolean`
  - Hover interaction: display tooltip overlay with bucket start, p50, p99, and sample count at the hovered point

**Out of scope:**
- Distribution histogram (TRC-P9-012)
- Data-fetching composable (TRC-P9-018)

### Constraints
- Canvas-based renderer; no third-party chart library
- p99 line must be visually thicker or brighter than the p50 line
- "No data" shown when `points` is empty or `timeseries` is null

### Success Conditions
1. **Test: `latencyTimeSeriesRenderer.spec.ts` "EmptyPoints_DrawsNoDataMessage"** — Pass `points=[]`; assert canvas `fillText` called with "No data".
2. **Test: `latencyTimeSeriesRenderer.spec.ts` "TwoLines_P99ThickerThanP50"** — Pass 5 time points; assert the `lineWidth` set immediately before the p99 path stroke is greater than the `lineWidth` set before the p50 path stroke.
3. **Test: `latencyTimeSeriesRenderer.spec.ts` "YAxis_UpperBoundCoversMaxP99"** — Pass points where max `p99Ms=80`; assert the y-axis upper bound used for scaling is ≥ 80.
4. **Test: `LatencyTimeSeriesChart_HoverShowsTooltip`** — Mount the component with a 5-point series; simulate a mouse-move event to the x-position of point 3; assert a tooltip element is visible containing that point's `p99Ms` value.
5. **Test: `LatencyTimeSeriesChart_LoadingState_ShowsIndicator`** — Pass `loading=true`; assert a loading indicator is visible (or the canvas content is hidden).

---

## TRC-P9-014 — `LatencyOutliersTable.vue` and Cross-View Pivot

**Phase:** 9 — Replication Latency, Gap Detection, Network Topology  
**Design reference:** [tracer_phase9_design.md §10.5](./tracer_phase9_design.md#105-latencyoutlierstable)

### Scope
**In scope:**
- `src/components/LatencyOutliersTable.vue`: scrollable table of `LatencyOutlierDto[]`
  - Columns: timestamp (`publishWallclockUtc`, formatted), topic, `publisherNode → subscriberNode`, `latencyMs` (ms, 2 d.p.), `thresholdMs` (ms, 2 d.p.), `budgetSource`
  - Per-row "Timeline →" button: navigates to `{ name: 'timeline', params: { sessionId }, query: { from, to, topic, node: subscriberNode } }` where `from = (publishWallclockUtc - 1s).toISOString()`, `to = (publishWallclockUtc + 1s).toISOString()`
  - "No outliers detected" empty state when `outliers` prop is `[]`
  - Renders up to 100 rows (backend already caps at 100)

**Out of scope:**
- Outlier data-fetching composable (TRC-P9-018)
- Timeline view modifications

### Constraints
- "Timeline →" pivot window: ± 1 second around `publishWallclockUtc`
- `node` query param = `subscriberNode` (the receiving end of the outlier event)
- Table is display-only; no row selection or editing

### Success Conditions
1. **Test: `LatencyOutliersTable_RendersAllRows`** — Pass 3 `LatencyOutlierDto` items; assert 3 `<tr>` elements exist inside `<tbody>`.
2. **Test: `LatencyOutliersTable_EmptyState_ShowsMessage`** — Pass `outliers=[]`; assert `<tbody>` has no rows and "No outliers detected" text is visible.
3. **Test: `LatencyOutliersTable_ShowInTimeline_NavigatesCorrectly`** — Mount with `sessionId="s1"`, one outlier (`publishWallclockUtc=T`, `topic="T1"`, `subscriberNode="node-B"`); click "Timeline →"; assert `router.push` was called with `params: { sessionId: "s1" }`, `query.from=(T-1s).toISOString()`, `query.to=(T+1s).toISOString()`, `query.topic="T1"`, `query.node="node-B"`.
4. **Test: `LatencyOutliersTable_BudgetSource_Displayed`** — Row with `budgetSource="budget"` shows the text "budget" in its cell; row with `budgetSource="top-0.1%"` shows "top-0.1%".
5. **Test: E2E `replication-latency-view.spec.ts` "outlier pivot to timeline"** — With the latency view showing at least one outlier row; click "Timeline →"; assert the URL changes to match `/v/timeline/`.

---

## TRC-P9-015 — `PublisherSubscriberMatrix.vue`

**Phase:** 9 — Replication Latency, Gap Detection, Network Topology  
**Design reference:** [tracer_phase9_design.md §10.4](./tracer_phase9_design.md#104-pair-matrix)

### Scope
**In scope:**
- `src/components/PublisherSubscriberMatrix.vue`: scrollable list of `LatencyPairSummaryDto[]`
  - Renders pairs in received order (backend sorts by p99 DESC)
  - Per row: topic (monospace small), `publisherNode → subscriberNode`, p99 (primary stat, 1 d.p. ms), sample count
  - CSS class `pair-matrix__row--over-budget` applied when `pair.p99Ms > budgetByTopic[pair.topic].p99BudgetMs`
  - CSS class `pair-matrix__row--selected` applied to the row whose object matches the `selectedPair` prop
  - Emits `select` event with the clicked `LatencyPairSummaryDto` on row click
  - Section heading: "Worst legs (by p99)"

**Out of scope:**
- Overall view layout (TRC-P9-011)
- Budget coloring based on `absoluteMaxMs` (only `p99BudgetMs` checked here)

### Constraints
- `max-height: 70vh; overflow-y: auto`
- Budget comparison: if no budget entry exists for a topic, `--over-budget` class is not applied
- `selectedPair` compared by object identity (same reference from the parent's pairs array)

### Success Conditions
1. **Test: `PublisherSubscriberMatrix_RendersAllPairs`** — Pass 5 pairs; assert 5 `li.pair-matrix__row` elements rendered.
2. **Test: `PublisherSubscriberMatrix_OverBudget_AppliesClass`** — Pass one pair with `p99Ms=100` and a budget `{ topic: "T", p99BudgetMs: 50 }`; assert that row has class `pair-matrix__row--over-budget`.
3. **Test: `PublisherSubscriberMatrix_NoBudget_NoOverBudgetClass`** — Pass a pair whose topic has no budget entry; assert the row does not have `pair-matrix__row--over-budget`.
4. **Test: `PublisherSubscriberMatrix_ClickRow_EmitsSelect`** — Click row 2; assert the `select` event was emitted with row 2's pair object.
5. **Test: `PublisherSubscriberMatrix_SelectedPair_AppliesSelectedClass`** — Pass `selectedPair` = the row-3 object; assert row 3 has `pair-matrix__row--selected`; assert rows 1, 2, 4, 5 do not.

---

## TRC-P9-016 — `GapDetectionView.vue` and `GapList.vue`

**Phase:** 9 — Replication Latency, Gap Detection, Network Topology  
**Design reference:** [tracer_phase9_design.md §12](./tracer_phase9_design.md#12-frontend-gap-detection-view)

### Scope
**In scope:**
- `src/views/GapDetectionView.vue` at route `/v/gaps/:sessionId` (name `gap-detection`)
  - On mount: load session range; fetch all gaps via `GET /api/gaps` for the full session time range
  - Tuple summary panel: group gaps by `(topic, publisherNode, subscriberNode)`; sort tuples by sum of `missingCount` DESC; display topic, pub→sub, gap count, total missing messages
  - Gap list panel: renders `<GapList>` with the full `gaps` array
  - `BundleModeRequiredBanner` rendered on 409
  - Route registered in the SPA router
- `src/components/GapList.vue`: table of `GapDto[]`
  - Columns: `resumedAtWallclockUtc` (formatted), topic, `publisherNode → subscriberNode`, `previousSequence`, last missing seq (`resumedAtSequence - 1`), `missingCount`, "Timeline →" button
  - "Timeline →" pivot: `{ name: 'timeline', params: { sessionId }, query: { from: (T-5s).toISOString(), to: (T+1s).toISOString(), topic, node: subscriberNode } }` where T = `resumedAtWallclockUtc`
  - "No gaps detected" empty state

**Out of scope:**
- Backend gap service (TRC-P9-007)
- Per-tuple filter UI (full-session query only in Phase 9)

### Constraints
- Route: `/v/gaps/:sessionId`, name `gap-detection`
- Tuple summary sorted by total `missingCount` DESC across all gaps for that tuple
- Timeline pivot window: 5 seconds before to 1 second after `resumedAtWallclockUtc` (§14.4)
- First-sample-edge-case gaps (where `previousSequence` is 0) displayed with the same visual weight as real gaps (§7.4 documented behaviour)

### Success Conditions
1. **Test: `GapDetectionView_409_ShowsBanner`** — Stub API returning 409; mount the view; assert `BundleModeRequiredBanner` is rendered.
2. **Test: `GapDetectionView_TupleSummary_SortedByMissingCount`** — Provide 3 gaps: tuple A with total missing=10, tuple B with total missing=25; assert tuple B appears before tuple A in the summary list.
3. **Test: `GapList_RendersGaps`** — Pass 3 `GapDto` items; assert 3 `<tr>` elements in `<tbody>`.
4. **Test: `GapList_EmptyState_ShowsMessage`** — Pass `gaps=[]`; assert "No gaps detected" text is visible.
5. **Test: `GapList_ShowInTimeline_NavigatesCorrectly`** — Gap with `resumedAtWallclockUtc=T`, `topic="T1"`, `subscriberNode="node-C"`; click "Timeline →"; assert `router.push` called with `query.from=(T-5s).toISOString()`, `query.to=(T+1s).toISOString()`, `query.topic="T1"`, `query.node="node-C"`.
6. **Test: E2E `gap-detection.spec.ts` "gap detection view loads"** — Open a bundle session; navigate to `/v/gaps/{sessionId}`; assert `h1` contains "Gap detection"; assert no JavaScript errors in the console.

---

## TRC-P9-017 — `NetworkTopologyView.vue` and `NetworkGraphCanvas.vue`

**Phase:** 9 — Replication Latency, Gap Detection, Network Topology  
**Design reference:** [tracer_phase9_design.md §11](./tracer_phase9_design.md#11-frontend-network-topology-view)

### Scope
**In scope:**
- `src/rendering/networkGraphLayout.ts`: exported `layoutGraph(input: GraphLayoutInput): LaidOutGraph`
  - Fruchterman-Reingold-ish force-directed layout: 200 iterations, repulsive node forces, attractive edge forces scaled by `log10(weight + 1)`, temperature decay (§11.2)
  - Initial positions arranged on a circle (deterministic given the same node ordering)
  - Node positions clamped within canvas bounds (40px margin)
- `src/rendering/networkGraphRenderer.ts`: exported `renderGraph(ctx, input: GraphRenderInput)`
  - Bezier-curve edges with arrowheads; `lineWidth = clamp(log10(weight + 1) * 1.5, 1, 8)`; selected edge highlighted in `#5b9dff`
  - Node circles (radius 14px normal, 18px hovered); node name label below the circle
- `src/components/NetworkGraphCanvas.vue`: canvas component
  - Props: `nodes: string[]`, `edges: { from: string; to: string; weight: number }[]`, `selectedEdge: { from: string; to: string } | null`
  - Runs `layoutGraph` on mount and when `nodes`/`edges` change; re-renders via `renderGraph`
  - Canvas click → proximity hit-test to edges → emits `select-edge({ from, to })`
- `src/views/NetworkTopologyView.vue` at route `/v/topology/:sessionId` (name `network-topology`)
  - On mount: fetch session + topology via `GET /api/topology/network`
  - Bundles edges by `(publisherNode, subscriberNode)` pair by default, summing `messageCount` across topics
  - `selectedEdge` ref → side panel listing per-topic breakdown for that edge pair
  - "Latency →" per topic row: `router.push({ name: 'replication-latency', params: { sessionId }, query: { publisherNode, subscriberNode, topic } })`
  - `BundleModeRequiredBanner` on 409

**Out of scope:**
- Topology data-fetching composable (TRC-P9-018)
- Multi-bundle comparison; hierarchical layout for large fleets

### Constraints
- Route: `/v/topology/:sessionId`, name `network-topology`
- No third-party graph library; plain TypeScript + Canvas API (§11.2)
- Layout must complete in < 100ms for ≤ 30 nodes (Phase 9 target scale)
- Layout is deterministic: same `nodes` array order produces the same initial circle positions and therefore the same converged layout

### Success Conditions
1. **Test: `networkGraphLayout.spec.ts` "EmptyGraph_ReturnsEmptyNodes"** — Pass empty `nodes` and `edges`; assert no error; `result.nodes.size === 0`.
2. **Test: `networkGraphLayout.spec.ts` "SingleNode_PositionedNearCanvasCenter"** — 1 node, 400×400 canvas; resulting position within 80px of (200, 200).
3. **Test: `networkGraphLayout.spec.ts` "ConnectedNodes_CloserThanDisconnected"** — 3 nodes A, B, C; edge A↔B with weight 100, no C edges; after layout, `distance(A,B) < distance(A,C)`.
4. **Test: `networkGraphLayout.spec.ts` "Layout_IsDeterministic"** — Run `layoutGraph` twice with identical input; assert all node positions are equal between the two runs.
5. **Test: `NetworkGraphCanvas_RendersCanvas`** — Mount with 3 nodes and 2 edges; assert a `<canvas>` element is present with non-zero width and height.
6. **Test: `NetworkTopologyView_DrillIntoEdge_NavigatesCorrectly`** — Mount with topology data; select an edge so the side panel appears; click "Latency →" for one topic row; assert `router.push` was called with `name: 'replication-latency'`, correct `query.publisherNode`, `query.subscriberNode`, `query.topic`.
7. **Test: `NetworkTopologyView_409_ShowsBanner`** — Stub the topology endpoint returning 409; mount the view; assert `BundleModeRequiredBanner` is rendered.
8. **Test: E2E `network-topology-view.spec.ts` "topology view renders canvas"** — Open a bundle session; navigate to `/v/topology/{sessionId}`; assert a `<canvas>` element is visible; assert no JavaScript errors.

---

## TRC-P9-018 — Composables: `useLatencyDistribution`, `useLatencyTimeSeries`, `useLatencyOutliers`, `useGapDetection`, `useTopology`

**Phase:** 9 — Replication Latency, Gap Detection, Network Topology  
**Design reference:** [tracer_phase9_design.md §2](./tracer_phase9_design.md#2-project-layout-additions) (composables listing), [§10.2](./tracer_phase9_design.md#102-replicationlatencyviewvue) (usage in ReplicationLatencyView)

### Scope
**In scope:**
- `src/composables/useLatencyDistribution.ts`: accepts a reactive `filter` ref; returns `{ distribution: Ref<LatencyDistributionDto | null>, loading: Ref<boolean>, error: Ref<{ status: number } | null> }`
- `src/composables/useLatencyTimeSeries.ts`: same pattern; data type `LatencyTimeSeriesDto | null`
- `src/composables/useLatencyOutliers.ts`: same pattern; data type `LatencyOutlierListDto | null`
- `src/composables/useGapDetection.ts`: same pattern; data type `GapResultDto | null`
- `src/composables/useTopology.ts`: same pattern; data type `TopologyDto | null`
- All five composables share the following behaviour:
  - `watch(filter, fetchFn, { immediate: true, deep: true })`
  - One `AbortController` per in-flight request; previous controller aborted when filter changes
  - `onUnmounted`: abort the current in-flight request
  - `loading` set to `true` at fetch start, `false` on completion (success or error)
  - On HTTP 409: set `error.value = { status: 409 }`; data ref stays `null`; do not throw
  - Guard: no API call when required filter fields (`from`, `to`) are null or undefined

**Out of scope:**
- API service layer implementation (assumed present)
- Cross-navigation caching (Vue lifecycle state within a single view mount is sufficient)
- Complex `latencyStore.ts` state management (minimal store for lifted cross-component state only if needed)

### Constraints
- Use `AbortController` for cancellation; not a manual boolean flag
- Named exports only; no default export from any composable file
- Do not call the API when `filter.from` or `filter.to` is nullish

### Success Conditions
1. **Test: `useLatencyDistribution.spec.ts` "FilterChange_RefetchesCalled"** — Mount a test component using the composable; change `filter.topic`; assert the API method was called a second time.
2. **Test: `useLatencyDistribution.spec.ts` "FilterChange_AbortsPreviousRequest"** — Trigger a filter change while the first request is still pending (delayed API mock); assert the first request's `AbortSignal.aborted` is `true`.
3. **Test: `useLatencyDistribution.spec.ts` "On409_ErrorStatusSet_DataNull"** — API mock returns 409; assert `error.value.status === 409` and `distribution.value === null`.
4. **Test: `useLatencyDistribution.spec.ts` "OnUnmount_RequestAborted"** — Start a fetch; unmount the component before the fetch resolves; assert the `AbortSignal` was aborted.
5. **Test: `useGapDetection.spec.ts` "Loading_TrueWhileFetching_FalseAfter"** — API mock with a delayed promise; assert `loading.value === true` during the fetch and `false` after it resolves.
6. **Test: `useTopology.spec.ts` "NoCallWhenFromIsNull"** — Mount the composable with `filter.from = null`; assert the API method was never called.

---

## TRC-P9-019 — Phase 9 Tests (Backend Unit, Integration, Frontend)

**Phase:** 9 — Replication Latency, Gap Detection, Network Topology  
**Design reference:** [tracer_phase9_design.md §14](./tracer_phase9_design.md#14-test-plan-for-phase-9) (§14.1–§14.4), [§1.3](./tracer_phase9_design.md#13-success-criteria)

### Scope
**In scope:**

**Backend unit tests** (`Tracer.Tests.Unit/`):
- `Util/QuantileSinkTests.cs` — streaming quantile computation: single value, sorted input, large uniform distribution
- `Util/HistogramSinkTests.cs` — log-bucket histogram: bucket-index formula correctness, boundary values (e.g. 2ms → bucket 4), empty input
- `WebApi/LatencyDistributionServiceTests.cs` — empty event set, single sample, uniform 1000 samples, `ExcludeSelfSubscribe` filter, histogram bucket boundaries, time-range filter, topic/pub/sub filter composition (§14.1)
- `WebApi/LatencyTimeSeriesServiceTests.cs` — bucket size auto-selection across span ranges, 12-bucket result for 1h session, per-bucket sample counts sum to total, empty bucket not emitted, per-bucket p50/p99 plausibility (§14.1)
- `WebApi/LatencyOutlierServiceTests.cs` — explicit threshold mode, top-0.1% fallback, `absoluteMaxMs` budget, per-topic budget application, `BudgetSource` values (§14.1)
- `WebApi/GapDetectionServiceTests.cs` — continuous sequence (no gaps), single gap, multiple gaps, first-sample edge case, tuple filter, time-range filter (§14.1)
- `WebApi/TopologyServiceTests.cs` — three-node graph, self-subscribe rows excluded, message count aggregated per tuple (§14.1)
- `WebApi/BudgetServiceTests.cs` — bundle with budgets, missing `latencyBudgets` section, missing `metadata.json`, live mode (§14.1)
- `WebApi/LatencyEndpointsTests.cs`, `GapEndpointsTests.cs`, `TopologyEndpointsTests.cs` — 409 in live mode, 200 in bundle mode, invalid params → 400, `from > to` → 400, all Phase 9 routes present in OpenAPI (§14.1)

**Backend integration tests** (`Tracer.Tests.Integration/`):
- `LatencyAnalysisRoundTripTests.cs` — `FakeNetworkModel` → bundle → latency endpoints; healthy-network fixture yields p99 < 5ms; degraded-network fixture yields at least one pair with p99 > 15ms (§14.2)
- `GapDetectionIntegrationTests.cs` — `LossyNetworkFixture` → bundle → `GET /api/gaps`; assert gap count > 0 (§14.2)
- `TopologyIntegrationTests.cs` — multi-node `FakeNetworkModel` fixture → bundle → `GET /api/topology/network`; assert node and edge counts match the fixture (§14.2)

**Frontend Vitest unit tests** (`tracer-viewer/tests/unit/`):
- `histogramRenderer.spec.ts` — empty distribution, single bucket, percentile line x-position, budget line presence/absence (§14.3)
- `latencyTimeSeriesRenderer.spec.ts` — two-line rendering, p99 thicker than p50, y-axis coverage (§14.3)
- `networkGraphLayout.spec.ts` — empty graph, single-node centering, connected-closer-than-disconnected, determinism (§14.3)
- `useLatencyDistribution.spec.ts` — filter reactivity, request cancellation, 409 handling, unmount abort (§14.3)

**Frontend E2E Playwright** (`tracer-viewer/tests/e2e/`):
- `replication-latency-view.spec.ts` — bundle session loads; pair matrix visible; click pair; outlier pivot to timeline; live-mode banner (§14.4)
- `gap-detection-view.spec.ts` — bundle session loads; heading visible; no JS errors (§14.4)
- `network-topology-view.spec.ts` — bundle session loads; canvas visible; edge drill navigates to latency view (§14.4)

**Out of scope:**
- Automated performance benchmarks (§14.5 targets are documented but not automated at this stage)
- Multi-bundle comparison test scenarios

### Constraints
- All Phase 1–8 tests must pass after Phase 9 implementation
- Backend unit tests use in-memory DuckDB via the `TestHarness` fixture pattern from prior phases
- Integration tests use `FakeNetworkModel` (TRC-P9-002) with four fixture profiles: Healthy, Degraded, Lossy, Spike
- Frontend unit tests use Vitest + `@vue/test-utils`; mock `useRouter` / `useRoute` from `vue-router`

### Success Conditions
1. **Test: `QuantileSinkTests.SingleValue`** — Add one value `x`; assert `p50 == x` and `p99 == x`.
2. **Test: `HistogramSinkTests.BucketIndex_2ms`** — Add value 2ms; assert the resulting bucket index equals `(long)Math.Floor(Math.Log2(2.0) * 4)` (equals 4).
3. **Test: `LatencyDistributionServiceTests.EmptyEventSet_ZeroCount`** — Empty events table; call `GetAsync`; assert `SampleCount == 0` and `Buckets.Count == 0`.
4. **Test: `LatencyDistributionServiceTests.ExcludeSelf_FiltersPublisherEqSubscriber`** — Two events: `pub=A, sub=B` and `pub=A, sub=A`; call `GetAsync` with `ExcludeSelfSubscribe=true`; assert `SampleCount == 1`.
5. **Test: `GapDetectionServiceTests.SingleGap_MissingCountCorrect`** — Events with sequence numbers [1, 2, 5]; call `FindGapsAsync`; assert one gap entry with `MissingCount == 2` (sequences 3 and 4 missing).
6. **Test: `GapDetectionServiceTests.ContinuousSequence_NoGaps`** — Events with sequence numbers [1, 2, 3, 4, 5]; call `FindGapsAsync`; assert `Gaps.Count == 0`.
7. **Test: `TopologyServiceTests.ThreeNodeGraph_CorrectEdgeCount`** — Events covering A→B, A→C, B→C (excluding self-subscribe); call `GetNetworkTopologyAsync`; assert `Nodes.Count == 3` and `Edges.Count == 3`.
8. **Test: `LatencyAnalysisRoundTripTests.DegradedNetwork_P99ExceedsThreshold`** — Build bundle with `DegradedNetworkFixture`; `GET /api/latency/pairs`; assert at least one pair has `p99Ms > 15`.
9. **Test: `GapDetectionIntegrationTests.LossyNetwork_GapsPresent`** — Build bundle with `LossyNetworkFixture`; `GET /api/gaps`; assert `gaps` array is non-empty.
10. **Test: E2E `replication-latency-view.spec.ts` "live mode shows bundle required banner"** — Visit the latency view URL against a live Observer instance; assert `.bundle-mode-required-banner` is visible; assert the banner text contains "requires bundle mode".
11. **Test: E2E `gap-detection.spec.ts` "gap detection view loads bundle session"** — Open a bundle session; navigate to `/v/gaps/{sessionId}`; assert `h1` contains "Gap detection"; assert either a gap row or "No gaps detected" is visible; assert no JavaScript errors.

<!-- PHASE 9 TASKS END -->

<!-- PHASE 10 TASKS BEGIN -->

# Phase 10 — SQL Console, Saved Queries, Bundle Library

---

## TRC-P10-001 — Read-Only SQL Executor: `SqlGuardrails` and `SqlExecutorService`

**Phase:** 10 — SQL Console, Saved Queries, Bundle Library  
**Design reference:** [tracer_phase10_design.md §3](./tracer_phase10_design.md#3-sql-executor-the-constrained-runner)

### Scope

**In scope:**
- `Tracer.WebApi/Queries/SqlGuardrails.cs` — lightweight tokenizer + AST validator that rejects mutations, DDL, ATTACH, COPY, multi-statement queries, and path-reading functions (`read_csv_auto`, `read_parquet`, etc.)
- `Tracer.WebApi/Queries/SqlExecutorService.cs` — accepts a `SqlExecutionRequest` (sql, parameters, timeoutSeconds, maxRows), runs guardrails, injects a `LIMIT` if absent, executes against the bundle/observer DuckDB via `LiveMultiIntervalReader`, applies a per-query `PRAGMA memory_limit`, cancels via `CancellationTokenSource` on timeout
- `SqlExecutorConfig` record (DefaultTimeoutSeconds=30, DefaultMaxRows=100 000, MaxMemoryMb=1024)
- `SqlExecutionRequest`, `SqlExecutionResult`, `SqlExecutionState` enum, `SqlColumnInfo`, `SqlExplainResult` record types
- `SqlSchemaService.cs` — introspects the first attached interval's `information_schema`, caches the result in memory, exposes `GetAsync` / `InvalidateAsync`; `SqlSchemaSnapshot`, `SqlTableInfo` records
- Unit tests in `Tracer.Tests.Unit/WebApi/SqlGuardrailsTests.cs` and `SqlExecutorServiceTests.cs` and `SqlSchemaServiceTests.cs`

**Out of scope:** HTTP endpoint wiring (TRC-P10-002), DI registration (TRC-P10-010), frontend (TRC-P10-011+), multi-bundle cross-join queries.

### Constraints
- The DuckDB connection accessed via `LiveMultiIntervalReader` is already opened read-only by Phase 5 — the executor does NOT open a new connection; it leases from the pool.
- Guardrails use a hand-rolled tokenizer (no third-party SQL parser library per §2.1). Forbidden keywords rejected anywhere in the token stream after comment stripping.
- `PRAGMA` keyword is forbidden in user SQL; the service itself issues `PRAGMA memory_limit` internally before user query execution.
- All `AllowedLeadingKeywords`: SELECT, WITH, EXPLAIN, DESCRIBE, SHOW, VALUES — others rejected immediately.
- Row limit injection: if the tokenized query contains no `LIMIT` keyword, append `LIMIT {maxRows}` to the trimmed SQL.
- `ExplainAsync` prefixes validated SQL with `EXPLAIN` and returns raw plan text.
- Schema cache is invalidated when `IntervalSetTracker.SetChanged` fires (Phase 5 §3); wire invalidation call inside `SqlSchemaService`.

### Success Conditions

1. **Test: `SqlGuardrailsTests.Select_Accepted`** — Input `"SELECT * FROM events"` → `Validate` returns `IsValid = true`.
2. **Test: `SqlGuardrailsTests.InsertInto_Rejected`** — Input `"INSERT INTO events VALUES (1)"` → `IsValid = false`, `RejectionReason` contains "Forbidden keyword".
3. **Test: `SqlGuardrailsTests.CreateTable_Rejected`** — Input `"CREATE TABLE foo (id INT)"` → `IsValid = false`.
4. **Test: `SqlGuardrailsTests.DropTable_Rejected`** — Input `"DROP TABLE events"` → `IsValid = false`.
5. **Test: `SqlGuardrailsTests.Attach_Rejected`** — Input `"ATTACH 'other.db'"` → `IsValid = false`.
6. **Test: `SqlGuardrailsTests.CopyTo_Rejected`** — Input `"COPY (SELECT 1) TO '/tmp/out.csv'"` → `IsValid = false`.
7. **Test: `SqlGuardrailsTests.Pragma_Rejected`** — Input `"PRAGMA threads = 4"` → `IsValid = false`.
8. **Test: `SqlGuardrailsTests.MultiStatement_Rejected`** — Input `"SELECT 1; SELECT 2"` → `IsValid = false`, reason contains "single statement".
9. **Test: `SqlGuardrailsTests.BlockCommentHidingDdl_Rejected`** — Input `"SELECT 1 /* trick */ DROP TABLE events"` → `IsValid = false` (comment stripped, DROP detected).
10. **Test: `SqlGuardrailsTests.ReadCsvAuto_Rejected`** — Input `"SELECT * FROM read_csv_auto('data.csv')"` → `IsValid = false`.
11. **Test: `SqlGuardrailsTests.ReadParquet_Rejected`** — Input `"SELECT * FROM read_parquet('data.parquet')"` → `IsValid = false`.
12. **Test: `SqlGuardrailsTests.QuotedIdentifierInsert_Accepted`** — Input `'SELECT "INSERT" FROM events'` (quoted identifier, not keyword) → `IsValid = true`.
13. **Test: `SqlGuardrailsTests.MixedCaseInsert_Rejected`** — Input `"InSeRt InTo events VALUES (1)"` → `IsValid = false` (case-insensitive match).
14. **Test: `SqlGuardrailsTests.With_Select_Accepted`** — Input `"WITH cte AS (SELECT 1) SELECT * FROM cte"` → `IsValid = true`.
15. **Test: `SqlExecutorServiceTests.SimpleSelect_ReturnsRows`** — Set up fixture DuckDB with one events row; call `ExecuteAsync`; assert `State == Succeeded`, `Rows.Count == 1`, `Columns` includes expected column names.
16. **Test: `SqlExecutorServiceTests.ParameterBinding_Honored`** — Query `"SELECT * FROM events WHERE topic = $topic"` with parameter `topic = "weapons.fire"`; assert returned rows all have `topic == "weapons.fire"`.
17. **Test: `SqlExecutorServiceTests.DefaultLimitInjected_WhenAbsent`** — Query without LIMIT; check the executed SQL has `LIMIT 100000` appended (inspect via query interceptor or fixture row count).
18. **Test: `SqlExecutorServiceTests.ExplicitLimit_NotModified`** — Query `"SELECT * FROM events LIMIT 10"` — assert no second LIMIT appended (no syntax error, rows ≤ 10).
19. **Test: `SqlExecutorServiceTests.Timeout_ReturnsTimeoutState`** — Configure `DefaultTimeoutSeconds = 1`; execute a query that sleeps for 5 seconds (DuckDB `SELECT sleep(5)`); assert `State == Timeout`.
20. **Test: `SqlExecutorServiceTests.InvalidSql_ReturnsFailedState`** — Query `"SELECT FROM"` (malformed); assert `State == Failed` and `ErrorMessage` is non-empty.
21. **Test: `SqlSchemaServiceTests.GetAsync_ReturnsTables`** — Build a fixture DuckDB with `events` table; call `GetAsync`; assert `Tables` contains an entry with `Name == "events"` and columns matching the schema.
22. **Test: `SqlSchemaServiceTests.Cache_SecondCallDoesNotRequery`** — Call `GetAsync` twice; assert the underlying connection is only acquired once (mock `LiveMultiIntervalReader`).
23. **Test: `SqlSchemaServiceTests.Invalidate_ForcesRefresh`** — Call `GetAsync`, then `InvalidateAsync`, then `GetAsync` again; assert the connection is acquired twice.

---

## TRC-P10-002 — SQL API Endpoints: `/api/sql/execute`, `/api/sql/schema`, `/api/sql/explain`

**Phase:** 10 — SQL Console, Saved Queries, Bundle Library  
**Design reference:** [tracer_phase10_design.md §4](./tracer_phase10_design.md#4-sql-api-endpoints)

### Scope

**In scope:**
- `Tracer.WebApi/Endpoints/SqlEndpoints.cs` — `Map(WebApplication app)` static method registering three routes:
  - `POST /api/sql/execute` — body `SqlExecuteRequestDto`; delegates to `SqlExecutorService`; returns `SqlExecuteResultDto`
  - `GET /api/sql/schema` — delegates to `SqlSchemaService`; returns `SqlSchemaDto`
  - `POST /api/sql/explain` — body `SqlExplainRequestDto`; delegates to `SqlExecutorService.ExplainAsync`; returns `SqlExplainResultDto`
- DTO records in `Tracer.WebApi/Contracts/Dto/`: `SqlExecuteRequestDto`, `SqlExecuteResultDto`, `SqlColumnInfoDto`, `SqlSchemaDto`, `SqlTableInfoDto`, `SqlExplainRequestDto`, `SqlExplainResultDto`
- `SqlDtoMapper` static class mapping service result types to DTOs
- Input validation: empty/whitespace `Sql` → HTTP 400 `ProblemDetails`
- Unit tests in `Tracer.Tests.Unit/WebApi/SqlEndpointsTests.cs`

**Out of scope:** `SqlExecutorService` and `SqlSchemaService` internals (TRC-P10-001), DI wiring (TRC-P10-010), frontend consumption (TRC-P10-011+).

### Constraints
- Endpoints use `TypedResults` (Minimal API pattern matching Phase 8/9 endpoints).
- `POST /api/sql/execute` accepts optional `timeoutSeconds` (min 1, max 300) and `rowLimit` (min 1, max 1 000 000) in the request body; values outside ranges are clamped to the configured defaults, not rejected.
- State `Rejected` (from guardrails) maps to HTTP 200 with `state = "Rejected"` in the body — it is not a 400 — so the frontend can display the rejection message in the result area uniformly.
- `POST /api/sql/explain`: failure (invalid/forbidden SQL) → HTTP 400 `ProblemDetails` with `detail` = rejection reason.
- All endpoints decorated with `.WithOpenApi()`.

### Success Conditions

1. **Test: `SqlEndpointsTests.Execute_ValidQuery_Returns200WithResults`** — POST `/api/sql/execute` with `{ "sql": "SELECT 1 AS n" }` → HTTP 200, body `state == "Succeeded"`, `columns[0].name == "n"`, `rows[0][0] == 1`.
2. **Test: `SqlEndpointsTests.Execute_EmptySql_Returns400`** — POST `/api/sql/execute` with `{ "sql": "" }` → HTTP 400, `ProblemDetails.title` contains "SQL required".
3. **Test: `SqlEndpointsTests.Execute_WhitespaceSql_Returns400`** — POST `/api/sql/execute` with `{ "sql": "   " }` → HTTP 400.
4. **Test: `SqlEndpointsTests.Execute_ForbiddenSql_Returns200WithRejectedState`** — POST `/api/sql/execute` with `{ "sql": "DROP TABLE events" }` → HTTP 200, body `state == "Rejected"`, `errorMessage` contains "Forbidden keyword".
5. **Test: `SqlEndpointsTests.Execute_TimeoutExceeded_Returns200WithTimeoutState`** — Mock `SqlExecutorService` returning a Timeout result → HTTP 200, `state == "Timeout"`.
6. **Test: `SqlEndpointsTests.Schema_Returns200WithTables`** — GET `/api/sql/schema` → HTTP 200, body has `tables` array with at least `"events"` entry.
7. **Test: `SqlEndpointsTests.Explain_ValidSql_Returns200WithPlanText`** — POST `/api/sql/explain` with `{ "sql": "SELECT * FROM events" }` → HTTP 200, body `planText` is non-empty string.
8. **Test: `SqlEndpointsTests.Explain_EmptySql_Returns400`** — POST `/api/sql/explain` with `{ "sql": "" }` → HTTP 400.
9. **Test: `SqlEndpointsTests.Explain_ForbiddenSql_Returns400`** — POST `/api/sql/explain` with forbidden SQL → HTTP 400 `ProblemDetails`.
10. **Test: `SqlConsoleIntegrationTests.Execute_SelectCount_CorrectCount`** — Set up a bundle with 50 known events; POST `/api/sql/execute` with `SELECT COUNT(*) AS n FROM events`; assert `rows[0][0] == 50`.
11. **Test: `SqlConsoleIntegrationTests.Execute_ParameterizedQuery_BindsCorrectly`** — POST `/api/sql/execute` with parameterized query and `parameters: { "topic": "weapons.fire" }`; assert only events with that topic returned.
12. **Test: `SqlConsoleIntegrationTests.Schema_ReturnsExpectedColumns`** — GET `/api/sql/schema` against a live bundle; assert the `events` table entry contains columns `topic`, `event_id`, `publish_wallclock`, `publisher_node`.

---

## TRC-P10-003 — Saved Queries Data Store

**Phase:** 10 — SQL Console, Saved Queries, Bundle Library  
**Design reference:** [tracer_phase10_design.md §6](./tracer_phase10_design.md#6-saved-queries)

### Scope

**In scope:**
- New project `Tracer.Storage.SavedQueries/Tracer.Storage.SavedQueries.csproj`
- `SavedQueryRecord` record with fields: `SavedQueryId` (ULID string), `Label`, `Description`, `Sql`, `Parameters` (`IReadOnlyList<SavedQueryParameter>`), `Tags`, `IsBuiltIn`, `IsFavorite`, `Author`, `CreatedAtUtc`, `LastRunAtUtc`, `RunCount`
- `SavedQueryParameter` record: `Name`, `DuckType`, `DefaultValueText`, `Description`
- `ISavedQueryStore` interface: `ListAsync(SavedQueryFilter, CancellationToken)`, `GetAsync(string id, CancellationToken)`, `CreateAsync(SavedQueryRecord, CancellationToken)`, `UpdateAsync(SavedQueryRecord, CancellationToken)`, `DeleteAsync(string id, CancellationToken)`, `IncrementRunCountAsync(string id, CancellationToken)`
- `SavedQueryFilter` record: `IsBuiltIn?`, `IsFavorite?`, `Tag?`, `Author?`
- `SqliteSavedQueryStore` implementing `ISavedQueryStore` — adds `saved_queries` table to the existing Phase 8 `annotations.db` SQLite file on construction
- `Schema/SavedQueriesSchema.cs` — DDL constants: table creation SQL, index on `label`, index on `is_favorite`
- `UpdateAsync` returns `false` (or throws) when `IsBuiltIn = true`; `DeleteAsync` same
- Unit tests in `Tracer.Tests.Unit/` (or integration tests in `SavedQueriesRoundTripTests.cs`)

**Out of scope:** API endpoints (TRC-P10-004), built-in query seeding (TRC-P10-005), DI wiring (TRC-P10-010).

### Constraints
- Uses the same SQLite connection/file as Phase 8 `AnnotationStore` (shares `annotations.db`); migration logic applies `CREATE TABLE IF NOT EXISTS` at startup.
- `parameters_json` and `tags_json` columns store JSON arrays; serialized/deserialized via `System.Text.Json`.
- ULID generation for new IDs (use `Ulid.NewUlid().ToString()`).
- `UpdateAsync` on a built-in query: throws `InvalidOperationException("Built-in queries are read-only; clone first")`.
- `DeleteAsync` on a built-in query: throws `InvalidOperationException("Built-in queries are read-only; clone first")`.
- `ListAsync` with no filter criteria returns all queries; each filter field ANDs with others when non-null.

### Success Conditions

1. **Test: `SavedQueriesRoundTripTests.Create_ThenList_Appears`** — Create a `SavedQueryRecord`; call `ListAsync(new SavedQueryFilter())`; assert record appears with correct `Label` and `Sql`.
2. **Test: `SavedQueriesRoundTripTests.Update_PersistsChanges`** — Create record; call `UpdateAsync` with changed `Label`; call `GetAsync`; assert new label returned.
3. **Test: `SavedQueriesRoundTripTests.Delete_RemovesRecord`** — Create record; call `DeleteAsync`; call `ListAsync`; assert record absent.
4. **Test: `SavedQueriesRoundTripTests.UpdateBuiltIn_Throws`** — Create record with `IsBuiltIn = true`; call `UpdateAsync`; assert `InvalidOperationException` thrown.
5. **Test: `SavedQueriesRoundTripTests.DeleteBuiltIn_Throws`** — Create built-in record; call `DeleteAsync`; assert `InvalidOperationException`.
6. **Test: `SavedQueriesRoundTripTests.FilterByFavorite_ReturnsOnlyFavorites`** — Create two records (one favorite, one not); filter `IsFavorite = true`; assert only the favorite returned.
7. **Test: `SavedQueriesRoundTripTests.FilterByTag_ReturnsMatchingOnly`** — Create records with tags `["latency"]` and `["overview"]`; filter `Tag = "latency"`; assert only the latency record returned.
8. **Test: `SavedQueriesRoundTripTests.IncrementRunCount_UpdatesCountAndTimestamp`** — Create record with `RunCount = 0`; call `IncrementRunCountAsync`; reload; assert `RunCount == 1` and `LastRunAtUtc` is non-null and recent.
9. **Test: `SavedQueriesRoundTripTests.Parameters_RoundTrip`** — Create record with two `SavedQueryParameter` entries; reload from DB; assert both parameters present with correct `Name`, `DuckType`, `DefaultValueText`.
10. **Test: `SavedQueriesRoundTripTests.Tags_RoundTrip`** — Create record with `Tags = ["a", "b", "c"]`; reload; assert tags list equals original.
11. **Test: `SavedQueriesRoundTripTests.Idempotent_SchemaCreation`** — Call store constructor twice against same DB file; no exception; list returns expected rows.

---

## TRC-P10-004 — Saved Queries API Endpoints

**Phase:** 10 — SQL Console, Saved Queries, Bundle Library  
**Design reference:** [tracer_phase10_design.md §6.4](./tracer_phase10_design.md#64-saved-query-endpoints)

### Scope

**In scope:**
- `Tracer.WebApi/Endpoints/SavedQueriesEndpoints.cs` — `Map(WebApplication app)` registering:
  - `GET /api/saved-queries` — query params: `tag`, `author`, `favorite` (bool), `builtIn` (bool); returns `IReadOnlyList<SavedQueryDto>`
  - `GET /api/saved-queries/{id}` — returns single `SavedQueryDto` or 404
  - `POST /api/saved-queries` — body `CreateSavedQueryDto`; returns 201 with created record
  - `PUT /api/saved-queries/{id}` — body `UpdateSavedQueryDto`; returns 200 or 405 for built-ins
  - `DELETE /api/saved-queries/{id}` — returns 204 or 405 for built-ins
  - `POST /api/saved-queries/{id}/favorite` — toggles `IsFavorite`; returns 200
  - `POST /api/saved-queries/{id}/clone` — clones to a new user-editable record with fresh ULID; returns 201 with clone
  - `POST /api/saved-queries/{id}/run` — calls `IncrementRunCountAsync`; returns 204
- `SavedQueryDto` and related DTOs in `Tracer.WebApi/Contracts/Dto/SavedQueryDto.cs`
- Unit tests in `Tracer.Tests.Unit/WebApi/SavedQueryEndpointsTests.cs`

**Out of scope:** `ISavedQueryStore` implementation (TRC-P10-003), built-in seeding (TRC-P10-005), DI wiring (TRC-P10-010).

### Constraints
- `PUT` and `DELETE` on a built-in query: return HTTP 405 with `ProblemDetails` detail = `"Built-in queries are read-only; clone first"`.
- `POST /api/saved-queries/{id}/clone` copies all fields (label, description, sql, parameters, tags) but sets `IsBuiltIn = false`, `IsFavorite = false`, generates a new ULID, sets `CreatedAtUtc = UtcNow`, `RunCount = 0`, `Author` from the clone request body (optional).
- Empty `label` on create/update → HTTP 400.
- Non-existent `{id}` on get/put/delete/clone → HTTP 404.
- All endpoints `.WithOpenApi()`.

### Success Conditions

1. **Test: `SavedQueryEndpointsTests.Get_All_Returns200`** — Seed two records; GET `/api/saved-queries`; assert HTTP 200, array length ≥ 2.
2. **Test: `SavedQueryEndpointsTests.Get_FilterByFavorite_ReturnsSubset`** — Seed one favorite, one not; GET `/api/saved-queries?favorite=true`; assert count == 1.
3. **Test: `SavedQueryEndpointsTests.Get_ById_Returns200`** — Seed one record; GET `/api/saved-queries/{id}`; assert 200 with correct `label`.
4. **Test: `SavedQueryEndpointsTests.Get_ById_NotFound_Returns404`** — GET `/api/saved-queries/nonexistent-id`; assert HTTP 404.
5. **Test: `SavedQueryEndpointsTests.Post_ValidRecord_Returns201`** — POST `/api/saved-queries` with valid DTO; assert HTTP 201, response `savedQueryId` is non-empty ULID.
6. **Test: `SavedQueryEndpointsTests.Post_EmptyLabel_Returns400`** — POST with `label = ""`; assert HTTP 400.
7. **Test: `SavedQueryEndpointsTests.Put_UserRecord_Returns200`** — Create user record; PUT with updated label; assert HTTP 200 and GET returns new label.
8. **Test: `SavedQueryEndpointsTests.Put_BuiltInRecord_Returns405`** — Create built-in record; PUT; assert HTTP 405, `ProblemDetails.detail` contains "Built-in".
9. **Test: `SavedQueryEndpointsTests.Delete_UserRecord_Returns204`** — Create user record; DELETE; assert HTTP 204; subsequent GET returns 404.
10. **Test: `SavedQueryEndpointsTests.Delete_BuiltInRecord_Returns405`** — Create built-in; DELETE; assert HTTP 405.
11. **Test: `SavedQueryEndpointsTests.Clone_BuiltIn_Returns201EditableCopy`** — Seed built-in; POST `/api/saved-queries/{id}/clone`; assert HTTP 201; new record `isBuiltIn == false`, `savedQueryId` differs from original; GET original built-in still present.
12. **Test: `SavedQueryEndpointsTests.Run_IncrementRunCount`** — Seed record with `runCount = 0`; POST `/api/saved-queries/{id}/run`; GET record; assert `runCount == 1`.

---

## TRC-P10-005 — Built-In Saved Queries Seeding

**Phase:** 10 — SQL Console, Saved Queries, Bundle Library  
**Design reference:** [tracer_phase10_design.md §6.3](./tracer_phase10_design.md#63-built-in-queries)

### Scope

**In scope:**
- `Tracer.Storage.SavedQueries/BuiltIn/builtin-queries.json` embedded resource — exactly 5 built-in queries:
  1. `builtin-top-topics-by-volume` — top topics by event count (`$from`, `$to` parameters, tags: `["overview", "topics"]`)
  2. `builtin-events-by-trace` — events on a trace (`$trace_id` parameter, tags: `["traces", "lineage"]`)
  3. `builtin-event-counts-per-node` — volume per publisher node (`$from`, `$to`, tags: `["overview", "nodes"]`)
  4. `builtin-latency-distribution-by-topic` — per-topic p50/p99 latency, bundle-only (`$from`, `$to`, tags: `["latency", "performance"]`)
  5. `builtin-entity-events` — all events for an entity (`$entity_id`, `$from`, `$to`, tags: `["entities"]`)
- `Tracer.Storage.SavedQueries/BuiltIn/BuiltInLoader.cs` — `EnsureLoadedAsync(ISavedQueryStore, CancellationToken)`: reads the embedded JSON, skips IDs already in the store, inserts missing ones as `IsBuiltIn = true`; idempotent on repeated calls
- The JSON is embedded as a manifest resource via the `.csproj`
- Unit tests via `BuiltInQueriesServiceTests.cs`

**Out of scope:** API endpoints (TRC-P10-004), DI call site for `EnsureLoadedAsync` (TRC-P10-010).

### Constraints
- `EnsureLoadedAsync` must be idempotent: calling it twice inserts each built-in at most once (checks by ID, not label).
- All built-in query SQL templates must pass `SqlGuardrails.Validate` (only SELECT/WITH allowed).
- Each built-in has at least one parameter with `DefaultValueText` set; `session_start` and `session_end` are valid defaults resolved frontend-side.
- `builtin-latency-distribution-by-topic` uses `APPROX_QUANTILE` and `EXTRACT(EPOCH ...)` — valid DuckDB SELECT syntax.

### Success Conditions

1. **Test: `BuiltInQueriesServiceTests.FirstLoad_InsertsAllFiveBuiltIns`** — Empty store; call `EnsureLoadedAsync`; call `ListAsync(IsBuiltIn = true)`; assert count == 5.
2. **Test: `BuiltInQueriesServiceTests.SecondLoad_DoesNotDuplicate`** — Call `EnsureLoadedAsync` twice; list built-ins; assert count still == 5.
3. **Test: `BuiltInQueriesServiceTests.AllBuiltIns_MarkedIsBuiltInTrue`** — After seeding, every record from `ListAsync(IsBuiltIn = true)` has `IsBuiltIn = true`.
4. **Test: `BuiltInQueriesServiceTests.AllBuiltInSql_PassesGuardrails`** — For each loaded built-in, call `SqlGuardrails.Validate(record.Sql)`; assert all return `IsValid = true`.
5. **Test: `BuiltInQueriesServiceTests.BuiltInTopTopics_HasExpectedParams`** — Load built-ins; find `builtin-top-topics-by-volume`; assert `Parameters` contains entries with `Name == "from"` and `Name == "to"`.
6. **Test: `BuiltInQueriesServiceTests.BuiltInEventsByTrace_HasTraceIdParam`** — Find `builtin-events-by-trace`; assert single parameter with `Name == "trace_id"` and `DuckType == "UBIGINT"`.
7. **Test: `BuiltInQueriesServiceTests.BuiltInLatency_HasLatencyTag`** — Find `builtin-latency-distribution-by-topic`; assert `Tags` contains `"latency"`.
8. **Test: `BuiltInQueriesServiceTests.PartialExistingLoad_OnlyInsertsNewOnes`** — Pre-seed 2 of the 5 built-in IDs manually; call `EnsureLoadedAsync`; assert total built-ins == 5 (not 7).

---

## TRC-P10-006 — Bundle Library Metadata Store: `BundleLibraryService`

**Phase:** 10 — SQL Console, Saved Queries, Bundle Library  
**Design reference:** [tracer_phase10_design.md §7.3](./tracer_phase10_design.md#73-bundlelibraryservice)

### Scope

**In scope:**
- `Tracer.WebApi/Queries/BundleLibraryService.cs` — file-system-backed metadata service:
  - `ListAsync(CancellationToken)` — enumerates `_bundlesRoot` directories; for each, reads immutable `metadata.json` (aggregator-written, Phase 4) and `bundle-metadata.json` (user-editable); combines into `BundleLibraryEntry` list; skips directories missing `metadata.json`
  - `UpdateMetadataAsync(string bundleId, BundleMetadataUpdate, CancellationToken)` — writes/updates `bundle-metadata.json` in the bundle directory; never touches `metadata.json`
  - `RecordOpenedAsync(string bundleId, CancellationToken)` — convenience: sets `LastOpenedAtUtc = UtcNow`
  - `DeleteAsync(string bundleId, CancellationToken)` — removes the bundle directory recursively
- Record types: `BundleLibraryEntry`, `BundleUserMetadata`, `BundleMetadataUpdate`
- `ComputeDirectorySize` — sums all file lengths recursively
- Unit tests in `Tracer.Tests.Unit/WebApi/BundleLibraryServiceTests.cs`; integration tests in `BundleLibraryRoundTripTests.cs`

**Out of scope:** HTTP endpoints (TRC-P10-007), import/export (TRC-P10-008), DI wiring (TRC-P10-010), frontend (TRC-P10-011+).

### Constraints
- `bundle-metadata.json` and `metadata.json` are separate files. `UpdateMetadataAsync` MUST NOT write to `metadata.json`.
- `bundle-metadata.json` is a JSON-serialized `BundleUserMetadata`; missing file is treated as empty/default metadata.
- `ListAsync` on a non-existent `_bundlesRoot` returns an empty list (no exception).
- `DeleteAsync` returns `false` if the bundle directory does not exist.
- `SizeBytes` includes all files under the bundle directory, including both metadata files.
- Partial `BundleMetadataUpdate` (null fields) preserves existing values — a `null` Tags does not overwrite the existing tags array.

### Success Conditions

1. **Test: `BundleLibraryServiceTests.List_NoBundlesRoot_ReturnsEmpty`** — Configure service with a non-existent path; `ListAsync` returns empty list without exception.
2. **Test: `BundleLibraryServiceTests.List_BundleWithoutUserMetadata_ReturnsNullLabelAndEmptyTags`** — Create bundle dir with only `metadata.json`; assert returned entry has `Label == null`, `Tags.Count == 0`, `IsArchived == false`.
3. **Test: `BundleLibraryServiceTests.List_BundleWithUserMetadata_ReturnsMergedEntry`** — Create bundle dir with `metadata.json` and `bundle-metadata.json` (label="My Bundle", tags=["test"]); assert returned entry has `Label == "My Bundle"` and tags contains "test".
4. **Test: `BundleLibraryServiceTests.Update_WritesToBundleMetadataJson`** — `UpdateMetadataAsync` with label="Renamed"; reload `bundle-metadata.json`; assert label matches.
5. **Test: `BundleLibraryServiceTests.Update_DoesNotTouchAggregatorMetadata`** — `UpdateMetadataAsync`; assert `metadata.json` byte-for-byte unchanged.
6. **Test: `BundleLibraryServiceTests.Update_PartialUpdate_PreservesExistingFields`** — Set label="Existing", tags=["a"]; call `UpdateMetadataAsync` with only `Description = "desc"` (other fields null); reload; assert label still "Existing", tags still ["a"], description now "desc".
7. **Test: `BundleLibraryServiceTests.Delete_RemovesBundleDirectory`** — Create bundle dir; `DeleteAsync`; assert directory no longer exists; returns `true`.
8. **Test: `BundleLibraryServiceTests.Delete_NonExistent_ReturnsFalse`** — `DeleteAsync("no-such-bundle")`; assert returns `false`.
9. **Test: `BundleLibraryServiceTests.SizeBytes_IncludesNestedFiles`** — Create bundle dir with two files totalling 1000 bytes; assert returned `SizeBytes == 1000`.
10. **Test: `BundleLibraryRoundTripTests.CreateBundle_ListUpdate_Archive_Persist`** — Build a bundle via test harness; list; update label and tags; reload; assert persisted; archive (set `IsArchived = true`); list again; assert entry present with `IsArchived = true`.

---

## TRC-P10-007 — Bundle Library API Endpoints

**Phase:** 10 — SQL Console, Saved Queries, Bundle Library  
**Design reference:** [tracer_phase10_design.md §7.4](./tracer_phase10_design.md#74-endpoint-extension)

### Scope

**In scope:**
- `Tracer.WebApi/Endpoints/BundleLibraryEndpoints.cs` — extends Phase 4 bundle endpoints by registering:
  - `GET /api/bundles/library` — returns all `BundleLibraryEntry` records as `BundleLibraryEntryDto[]`; optional query params `archived` (bool, default false), `tag`, `sortBy` (`builtAt|sessionStart|size|label`), `desc` (bool)
  - `PUT /api/bundles/{id}/metadata` — body `{ label?, description?, tags?, archived? }`; 200 or 404
  - `POST /api/bundles/{id}/opened` — records `LastOpenedAtUtc`; 204 or 404
  - `DELETE /api/bundles/{id}` — deletes bundle directory; 204 or 404
  - `POST /api/bundles/import` — multipart upload of `.bundle.zip`; delegates to `BundleImportService` (TRC-P10-008); 201 with created bundle entry, or 409 Conflict if bundle already exists, or 400 if invalid zip
- `BundleLibraryEntryDto` in `Tracer.WebApi/Contracts/Dto/BundleLibraryEntryDto.cs`
- Unit tests in `Tracer.Tests.Unit/WebApi/BundleLibraryEndpointsTests.cs` (via mock `BundleLibraryService` and `BundleImportService`)

**Out of scope:** `BundleLibraryService` internals (TRC-P10-006), export endpoint (lives in TRC-P10-008 alongside `BundleExportService`), DI wiring (TRC-P10-010).

### Constraints
- `GET /api/bundles/library` filters `isArchived` server-side when `archived=false` (default) — archived bundles excluded from default listing.
- `GET /api/bundles/library?archived=true` returns ALL bundles including archived.
- `DELETE /api/bundles/{id}` is destructive and irreversible — no soft-delete.
- `POST /api/bundles/import` stream is extracted by `BundleImportService`; this endpoint only handles HTTP binding and delegates.
- All endpoints `.WithOpenApi()`.

### Success Conditions

1. **Test: `BundleLibraryEndpointsTests.GetLibrary_DefaultExcludesArchived`** — Two bundles: one archived, one not; GET `/api/bundles/library`; assert only non-archived returned.
2. **Test: `BundleLibraryEndpointsTests.GetLibrary_WithArchivedTrue_ReturnsBoth`** — GET `/api/bundles/library?archived=true`; assert both returned.
3. **Test: `BundleLibraryEndpointsTests.GetLibrary_FilterByTag_ReturnsSubset`** — Bundle A tags=["prod"], Bundle B tags=["dev"]; GET `/api/bundles/library?tag=prod`; assert only A returned.
4. **Test: `BundleLibraryEndpointsTests.GetLibrary_SortBySize_Descending`** — Two bundles with different sizes; GET `/api/bundles/library?sortBy=size&desc=true`; assert larger bundle first.
5. **Test: `BundleLibraryEndpointsTests.PutMetadata_Returns200`** — PUT `/api/bundles/{id}/metadata` with `{ "label": "New" }`; assert HTTP 200; reload; assert label updated.
6. **Test: `BundleLibraryEndpointsTests.PutMetadata_NotFound_Returns404`** — PUT `/api/bundles/nonexistent/metadata`; assert HTTP 404.
7. **Test: `BundleLibraryEndpointsTests.PostOpened_Returns204`** — POST `/api/bundles/{id}/opened`; assert HTTP 204; reload entry; assert `lastOpenedAtUtc` is recent.
8. **Test: `BundleLibraryEndpointsTests.Delete_Returns204`** — DELETE `/api/bundles/{id}`; assert HTTP 204; GET library; assert bundle absent.
9. **Test: `BundleLibraryEndpointsTests.Delete_NotFound_Returns404`** — DELETE `/api/bundles/nonexistent`; assert HTTP 404.
10. **Test: `BundleLibraryEndpointsTests.Import_ValidZip_Returns201`** — POST `/api/bundles/import` with a valid bundle zip; assert HTTP 201, response contains `bundleId`.
11. **Test: `BundleLibraryEndpointsTests.Import_DuplicateBundle_Returns409`** — Import same zip twice; assert second import returns HTTP 409.
12. **Test: `BundleLibraryEndpointsTests.Import_InvalidZip_Returns400`** — POST with a corrupt/empty zip; assert HTTP 400.

---

## TRC-P10-008 — Bundle Import/Export Service

**Phase:** 10 — SQL Console, Saved Queries, Bundle Library  
**Design reference:** [tracer_phase10_design.md §7.4](./tracer_phase10_design.md#74-endpoint-extension); [§10 risks (zip-slip)](./tracer_phase10_design.md#10-phase-10-risks-and-mitigations)

### Scope

**In scope:**
- `Tracer.WebApi/Queries/BundleExportService.cs`:
  - `ExportAsync(string bundleId, Stream destination, CancellationToken ct)` — streams a zip archive of the entire bundle directory to `destination`; uses `System.IO.Compression.ZipArchive`; entries use relative paths (no leading slash); returns `false` if bundle not found
  - `GET /api/bundles/{id}/download` endpoint registered in `BundleLibraryEndpoints.cs` (or its own endpoints file) — sets `Content-Type: application/zip`, `Content-Disposition: attachment; filename="{bundleId}.bundle.zip"`, streams via `ExportAsync`
- `Tracer.WebApi/Queries/BundleImportService.cs`:
  - `ImportAsync(Stream zipStream, CancellationToken ct)` — extracts the zip to `_bundlesRoot/{bundleId}/`, validates each entry (no `..` path components, no absolute paths, only expected file types), calls Phase 4's bundle validator to check `metadata.json` integrity; returns `BundleImportResult` with the new `bundleId` or rejection reason
  - Zip-slip defense: reject any `ZipArchiveEntry.FullName` containing `..` or starting with `/` or `\`
  - Duplicate detection: if a directory with the extracted `bundleId` already exists in `_bundlesRoot`, return `AlreadyExists` result
- `BundleImportResult` record: `Success`, `BundleId?`, `AlreadyExists`, `InvalidFormat`, `ErrorMessage?`
- Unit tests in `Tracer.Tests.Unit/WebApi/BundleLibraryServiceTests.cs` (export + import)

**Out of scope:** HTTP endpoint binding (TRC-P10-007), `BundleLibraryService` listing (TRC-P10-006), DI wiring (TRC-P10-010).

### Constraints
- Export streams directly to the HTTP response body — no temp file on disk during export.
- Import writes to a temp subdirectory first; only renames to the final bundle directory after successful validation (atomic move).
- The Phase 4 bundle validator (`BundleValidator`) is called on the extracted `metadata.json`; if validation fails, the temp directory is deleted and `InvalidFormat` is returned.
- Allowed zip entry extensions: `.parquet`, `.json`, `.db` — any other extension causes `InvalidFormat`.
- Maximum import zip size: 10 GB (configurable); exceeding it returns `InvalidFormat` immediately.

### Success Conditions

1. **Test: `BundleExportServiceTests.Export_ProducesReadableZip`** — Build a bundle with known files; call `ExportAsync` to a `MemoryStream`; open resulting zip; assert all bundle files present with relative paths.
2. **Test: `BundleExportServiceTests.Export_NotFound_ReturnsFalse`** — Call `ExportAsync` with non-existent bundleId; assert returns `false`.
3. **Test: `BundleExportServiceTests.Export_NoAbsolutePaths`** — Inspect all zip entry names; assert none start with `/`, `\`, or a drive letter (`C:`).
4. **Test: `BundleImportServiceTests.Import_ValidZip_Succeeds`** — Export a bundle, then import the zip; assert `Success = true`, `BundleId` matches original; directory exists under `_bundlesRoot`.
5. **Test: `BundleImportServiceTests.Import_Duplicate_ReturnsAlreadyExists`** — Import the same zip twice; assert second call returns `AlreadyExists = true`.
6. **Test: `BundleImportServiceTests.Import_ZipSlash_Rejected`** — Craft a zip with an entry named `../escape/evil.json`; assert `InvalidFormat = true`, temp dir cleaned up.
7. **Test: `BundleImportServiceTests.Import_UnexpectedExtension_Rejected`** — Craft a zip with a `.exe` entry; assert `InvalidFormat = true`.
8. **Test: `BundleImportServiceTests.Import_InvalidMetadata_Rejected`** — Craft a zip with a corrupt `metadata.json`; assert `InvalidFormat = true` (Phase 4 validator rejects), temp dir cleaned up.
9. **Test: `BundleImportServiceTests.Import_AtomicWrite_TempCleanedOnFailure`** — Simulate a validator failure mid-import; assert no partial directory remains under `_bundlesRoot`.
10. **Test: `BundleLibraryRoundTripTests.ExportThenImport_RoundTrip`** — Build bundle, export to zip, delete bundle directory, import zip; list library; assert bundle reappears with correct session metadata.

---

## TRC-P10-009 — "Show SQL for This View" Backend Template Endpoint

**Phase:** 10 — SQL Console, Saved Queries, Bundle Library  
**Design reference:** [tracer_phase10_design.md §8](./tracer_phase10_design.md#8-cross-view-show-sql-for-this-view-affordance)

### Scope

**In scope:**
- `GET /api/sql/view-template` — query params: `view` (enum: `timeline`, `entity-history`, `causal`, `latency`, `gaps`, `topology`), plus view-specific filter params matching each view's existing query model
- `SqlEndpoints.cs` extended with `HandleViewTemplateAsync` handler (or a new `ViewTemplateEndpoints.cs` if cleaner)
- Per-view SQL template generators as a backend parallel to the frontend `showSqlGenerators.ts` — a `ViewSqlTemplateService` that maps `(view, filter params)` → parameterized SQL string. Uses the same service methods already implemented (Phase 5–9) to derive filter clauses:
  - `timeline` → `SELECT publish_wallclock, publisher_node, topic, event_id FROM events WHERE [time range] [AND topic=?] [AND publisher_node=?] ... ORDER BY publish_wallclock LIMIT 1000`
  - `entity-history` → `SELECT event_id, topic, publish_wallclock FROM events WHERE entity_id = $entity_id AND [time range] ORDER BY publish_wallclock`
  - `causal` → `SELECT event_id, publisher_node, topic, publish_wallclock FROM events WHERE trace_id = $trace_id ORDER BY publish_wallclock`
  - `latency` → mirrors the `LatencyDistributionService` query pattern (APPROX_QUANTILE grouping)
  - `gaps` → mirrors `GapDetectionService` query pattern
  - `topology` → mirrors `TopologyService` query pattern
- Response: `{ sql: string, description: string }` where `description` is a human-readable explanation of what the SQL computes
- Unknown `view` value → HTTP 400

**Out of scope:** Frontend `ShowSqlButton.vue` (TRC-P10-011+), SQL execution (TRC-P10-001/002), DI wiring (TRC-P10-010). Note: the frontend generates SQL client-side from §8.1 generators; this backend endpoint is the authoritative version for cross-view pivots and testing.

### Constraints
- The endpoint is purely a template generator — it does NOT execute the SQL.
- Generated SQL must pass `SqlGuardrails.Validate` (it is SELECT-only by definition).
- Parameter values from the query string are used to construct literal SQL clauses (with proper escaping via `sqlEscape` — replace `'` with `''` in string values) — NOT bound as DuckDB parameters, since this is returning SQL text.
- Time range params (`from`, `to`) parsed as ISO 8601; invalid format → 400.

### Success Conditions

1. **Test: `ViewTemplateEndpointsTests.Timeline_Returns200WithSelectStatement`** — GET `/api/sql/view-template?view=timeline&from=2026-01-01T00:00:00Z&to=2026-01-01T01:00:00Z`; assert HTTP 200, `sql` starts with `SELECT`, contains `FROM events`, contains the time range.
2. **Test: `ViewTemplateEndpointsTests.Timeline_WithTopic_IncludesTopicClause`** — Add `&topic=weapons.fire`; assert returned SQL contains `topic = 'weapons.fire'`.
3. **Test: `ViewTemplateEndpointsTests.EntityHistory_Returns200`** — GET with `view=entity-history&entityId=some-entity`; assert `sql` contains `entity_id`.
4. **Test: `ViewTemplateEndpointsTests.Causal_Returns200`** — GET with `view=causal&traceId=0xABCDEF`; assert `sql` contains `trace_id`.
5. **Test: `ViewTemplateEndpointsTests.Latency_Returns200`** — GET with `view=latency&from=...&to=...`; assert `sql` contains `APPROX_QUANTILE`.
6. **Test: `ViewTemplateEndpointsTests.UnknownView_Returns400`** — GET with `view=nonexistent`; assert HTTP 400.
7. **Test: `ViewTemplateEndpointsTests.GeneratedSql_PassesGuardrails`** — For all six view types, call the endpoint and pass the returned SQL to `SqlGuardrails.Validate`; assert all return `IsValid = true`.
8. **Test: `ViewTemplateEndpointsTests.SqlInjection_InTopic_IsEscaped`** — GET with `topic='; DROP TABLE events; --`; assert returned SQL contains `''` (escaped single quote), does NOT contain `DROP`.
9. **Test: `ViewTemplateEndpointsTests.InvalidTimeRange_Returns400`** — GET with `from=not-a-date`; assert HTTP 400.

---

## TRC-P10-010 — Phase 10 Wiring and DI

**Phase:** 10 — SQL Console, Saved Queries, Bundle Library  
**Design reference:** [tracer_phase10_design.md §2](./tracer_phase10_design.md#2-project-layout-additions); [§4.4](./tracer_phase10_design.md#44-wiring)

### Scope

**In scope:**
- Update `Tracer.WebApi` project file to reference `Tracer.Storage.SavedQueries`
- `ObserverHostBuilder.cs` DI registrations:
  - `AddSingleton<SqlExecutorService>()`
  - `AddSingleton<SqlSchemaService>()`
  - `AddSingleton(new SqlExecutorConfig { DefaultTimeoutSeconds = 30, DefaultMaxRows = 100_000, MaxMemoryMb = 1024 })`
  - `AddSingleton<ISavedQueryStore, SqliteSavedQueryStore>()` (reuses existing `annotations.db` path)
  - `AddSingleton<BundleLibraryService>()` (bound to configured bundles root path)
  - `AddSingleton<BundleExportService>()`
  - `AddSingleton<BundleImportService>()`
  - `AddSingleton<ViewSqlTemplateService>()`
- `OfflineViewerHostBuilder.cs` — same registrations (shared code path or duplication as appropriate per existing pattern)
- `ConfigureMiddleware` (or app build step):
  - `SqlEndpoints.Map(app)` including `/api/sql/view-template`
  - `SavedQueriesEndpoints.Map(app)`
  - `BundleLibraryEndpoints.Map(app)`
- `BuiltInLoader.EnsureLoadedAsync(store, ct)` called during `IHostedService` startup (or `app.Lifetime.ApplicationStarted`)
- `SqlSchemaService.InvalidateAsync()` wired to `IntervalSetTracker.SetChanged` event (Phase 5)
- Smoke test: `dotnet build Tracer.sln` succeeds; `GET /api/sql/schema` reachable; `GET /api/bundles/library` reachable; `GET /api/saved-queries` returns built-in queries on fresh start

**Out of scope:** All service/endpoint implementations (TRC-P10-001 through TRC-P10-009), frontend routing (TRC-P10-011+).

### Constraints
- `SqlExecutorConfig` values must be overridable via `appsettings.json` (bind from `"SqlExecutor"` config section; DI registers as `IOptions<SqlExecutorConfig>` or direct singleton with bound values).
- `BuiltInLoader.EnsureLoadedAsync` must run before the first HTTP request is served — use `IHostedService` or `app.MapGet(...).WithOrder(-1)` startup hook.
- If `ISavedQueryStore` fails to initialize (e.g., DB file locked), the application must log the error and fail fast rather than continue with a null store.
- `BundleLibraryService` is configured with the same bundles root used by the aggregator (Phase 4); this path comes from `appsettings.json` `"Bundles:RootPath"`.

### Success Conditions

1. **Test: `WiringTests.ObserverBuild_AllPhase10ServicesResolvable`** — Build the Observer DI container in a test; resolve `SqlExecutorService`, `SqlSchemaService`, `ISavedQueryStore`, `BundleLibraryService`, `BundleExportService`, `BundleImportService`; assert no `InvalidOperationException`.
2. **Test: `WiringTests.OfflineViewerBuild_AllPhase10ServicesResolvable`** — Same as above for the OfflineViewer DI container.
3. **Test: `WiringTests.BuiltInLoader_RunsOnStartup_QueriesPresent`** — Start the Observer (in-process test server); GET `/api/saved-queries?builtIn=true`; assert at least 5 results.
4. **Test: `WiringTests.SqlSchema_InvalidatedOnIntervalChange`** — Get schema (populate cache); fire `IntervalSetTracker.SetChanged`; get schema again; assert second call re-queries (cache miss — verify via call count mock or log message).
5. **Test: `WiringTests.SqlExecutorConfig_OverridableFromConfig`** — Start with `appsettings.json` containing `"SqlExecutor": { "DefaultTimeoutSeconds": 60 }`; resolve `SqlExecutorConfig`; assert `DefaultTimeoutSeconds == 60`.
6. **Test: `WiringTests.OpenApi_ContainsNewEndpoints`** — GET `/openapi/v1.json` (or equivalent); assert paths include `/api/sql/execute`, `/api/sql/schema`, `/api/sql/explain`, `/api/saved-queries`, `/api/bundles/library`.
7. **Test: `WiringTests.GetBundleLibrary_EmptyBundlesRoot_Returns200EmptyArray`** — Start server with empty bundles root; GET `/api/bundles/library`; assert HTTP 200 with empty `entries` array.
8. **Test: `WiringTests.GetSavedQueries_FreshStart_ReturnsBuiltIns`** — Fresh `annotations.db`; GET `/api/saved-queries`; assert response contains `label == "Top topics by event count"`.

---

*(frontend tasks TRC-P10-011+ to be appended)*

---

## TRC-P10-011 — `SqlConsoleView.vue` — Editor and Result Table

**Phase:** 10 — SQL Console, Saved Queries, Bundle Library  
**Design reference:** [tracer_phase10_design.md §5](./tracer_phase10_design.md#5-sql-console-frontend) — §5.1 SqlConsoleView Layout, §5.2 SqlEditor.vue, §5.3 SqlConsoleView.vue, §5.4 SqlResultTable

### Scope
**In scope:**
- `SqlConsoleView.vue` registered at route `/v/sql/:sessionId`
- `SqlEditor.vue` wrapping CodeMirror 6 (`@codemirror/lang-sql`, `@codemirror/autocomplete`) with SQL syntax highlighting, schema-aware autocomplete (tables and columns from `/api/sql/schema`), and `Cmd+Enter` / `Ctrl+Enter` execution shortcut
- `SchemaPanel.vue` — collapsible left sidebar listing queryable tables and their columns; clicking a table name or column name inserts text at the current editor cursor
- History sidebar — the last 50 queries run in the session, persisted in `localStorage` under key `tracer:sqlHistory`; clicking a history entry loads it into the editor
- Execute button in the toolbar; loading/cancellation state shown
- `useSqlExecution` composable wrapping `POST /api/sql/execute` with per-request cancellation via `AbortController`
- `useSqlSchema` composable wrapping `GET /api/sql/schema` (called once on mount, cached in Pinia `sqlConsoleStore`)
- `SqlResultTable.vue` — paginated (virtual scroll or page navigation), column headers show DuckDB type in parentheses (e.g., `topic (VARCHAR)`), client-side column sort
- "Truncated to N rows" banner when `result.truncated === true`
- Export buttons: **CSV** (browser download via `Blob`), **JSON** (browser download), **Copy to clipboard** (`navigator.clipboard`)
- URL query parameter `?sql=` pre-populates the editor on mount (used by "Show SQL for this view" in TRC-P10-016)
- Error state rendering for all four result states: `Succeeded` (table), `Failed` (DuckDB error message), `Timeout` (timeout message), `Rejected` (guardrails rejection reason) — each displayed in a styled `.sql-console__error` panel

**Out of scope:**
- Chart view (TRC-P10-012)
- Pivot affordances in result rows (TRC-P10-017)
- Save-query dialog (TRC-P10-014)
- Saved-query picker/browser (TRC-P10-013/014)
- SQL Explain UI beyond a plain placeholder alert (can be improved post-phase)

### Constraints
- CodeMirror 6 packages (`@codemirror/lang-sql`, `@codemirror/state`, `@codemirror/view`, `@codemirror/autocomplete`, `@codemirror/commands`, `@codemirror/theme-one-dark`) must be added to `package.json`; frontend bundle size target remains under 3 MB gzipped after addition (per §11 definition of done)
- The editor must destroy its `EditorView` instance in `onBeforeUnmount` to avoid memory leaks
- The SQL console must not allow mutations silently — if the backend returns `state === 'Rejected'` the error message is displayed verbatim; no retry omitting the rejection
- History is session-local (localStorage); no backend persistence for history in this task
- Export CSV must correctly escape values containing commas, newlines, or double-quotes per RFC 4180

### Success Conditions
1. **Test: `SqlConsoleView.spec.ts` — editor renders** — Mount `SqlConsoleView` with mocked `useSqlSchema` and `useSqlExecution`; assert the `div.sql-editor` element is present in the DOM.
2. **Test: `SqlConsoleView.spec.ts` — Cmd+Enter dispatches run** — Mount with mocks; simulate `Mod+Enter` keydown inside the editor; assert the `run` composable function was called.
3. **Test: `SqlConsoleView.spec.ts` — ?sql= param pre-populates** — Provide route mock with `query.sql = 'SELECT 1'`; mount; assert editor content equals `'SELECT 1'`.
4. **Test: `SqlConsoleView.spec.ts` — Rejected state shows guardrail error** — Mock `run` to resolve with `{ state: 'Rejected', errorMessage: 'Forbidden keyword: DROP' }`; call execute; assert `.sql-console__error` text contains `'Forbidden keyword: DROP'`.
5. **Test: `SqlConsoleView.spec.ts` — Timeout state shows timeout message** — Mock `run` to resolve with `{ state: 'Timeout', errorMessage: 'Query exceeded the 30-second budget' }`; assert `.sql-console__error` is visible and contains the timeout text.
6. **Test: `SqlConsoleView.spec.ts` — Truncated banner shown** — Mock result with `state: 'Succeeded', truncated: true, rows: [...]`; assert the truncation banner element is visible.
7. **Test: `SqlConsoleView.spec.ts` — Export CSV produces RFC 4180 content** — Mount with a result of 2 columns and 2 rows, one cell value containing a comma; click Export CSV; assert the Blob content wraps the comma-containing value in double-quotes.
8. **Test: `SqlConsoleView.spec.ts` — history persists to localStorage** — Run a query successfully; assert `localStorage.getItem('tracer:sqlHistory')` is non-null and contains the executed SQL string.
9. **Test: `SqlResultTable.spec.ts` — column type shown in header** — Render `SqlResultTable` with a column `{ name: 'topic', duckType: 'VARCHAR' }`; assert the `<th>` text contains `"VARCHAR"`.
10. **Test: `SqlResultTable.spec.ts` — client-side sort ascending then descending** — Render with 3 rows having distinct numeric values in a column; click the column header once; assert rows are in ascending order; click again; assert descending order.
11. **E2E: `sql-console.spec.ts` — execute and see result** — Navigate to `/v/sql/test-session`; type `SELECT topic, COUNT(*) FROM events GROUP BY topic LIMIT 5`; press `Control+Enter`; wait for `.sql-result-table`; assert the table is visible and contains at least one `<tr>` in `<tbody>`.

---

## TRC-P10-012 — SQL Console Chart View

**Phase:** 10 — SQL Console, Saved Queries, Bundle Library  
**Design reference:** [tracer_phase10_design.md §5.1](./tracer_phase10_design.md#51-sqlconsoleview-layout) — result tabs; [§5.3](./tracer_phase10_design.md#53-sqlconsoleviewvue) — `isChartable()` logic and `SqlResultChart` rendering

### Scope
**In scope:**
- `SqlResultChart.vue` component, conditionally rendered when the user selects the "Chart" tab in `SqlConsoleView`
- Two supported chart shapes detected by `isChartable()` in `SqlConsoleView`:
  - **Bar chart**: result has exactly 1 categorical (non-numeric) column + 1 numeric column (e.g., `topic, COUNT(*)`)
  - **Line chart**: result has 1 TIMESTAMP/DATE column + 1 or more numeric columns (e.g., time-bucketed event counts)
- Chart rendering via the existing shared chart library already used by Phase 5/9 views; no new charting dependency unless the existing one is insufficient
- `isChartable()` returns `true` when result has ≥ 2 columns and at least one column whose DuckDB type contains `INT`, `FLOAT`, `DOUBLE`, `DECIMAL`, `HUGEINT`, `BIGINT`, or `NUMERIC`
- "Chart" tab button is **disabled** (not hidden) when `isChartable()` returns false
- When the result has a plottable shape but > 10,000 data points, show an informational "Too many points to chart; use table view" message instead of a broken chart

**Out of scope:**
- Pie charts, scatter plots, or other chart types beyond bar and line
- Chart export (CSV/JSON export remains on the table tab only)
- Chart configuration (axis labels, custom colors, legend customisation)

### Constraints
- Must use the same charting library as Phase 5/9 views to avoid duplicate bundle weight
- Chart and table views share the same result data; no duplicate API call
- The 10,000-point cap is a client-side check on `result.rows.length`; it does not affect the row-limit shown in the backend

### Success Conditions
1. **Test: `SqlConsoleView.spec.ts` — chart tab disabled when no numeric column** — Provide a result with two VARCHAR columns; assert the "Chart" tab button has the `disabled` attribute.
2. **Test: `SqlConsoleView.spec.ts` — chart tab enabled with a numeric column** — Provide a result with a VARCHAR column and a BIGINT column; assert the "Chart" tab button does NOT have `disabled`.
3. **Test: `SqlConsoleView.spec.ts` — switching to chart tab renders SqlResultChart** — Enable condition met; click "Chart" tab; assert `SqlResultChart` is mounted; click "Table" tab; assert `SqlResultTable` is mounted and `SqlResultChart` is unmounted.
4. **Test: `SqlResultChart.spec.ts` — bar chart for 1 label + 1 numeric column** — Mount `SqlResultChart` with `columns: [{name:'topic',duckType:'VARCHAR'},{name:'count',duckType:'BIGINT'}]` and 3 rows; assert a bar chart element (canvas or SVG) is present.
5. **Test: `SqlResultChart.spec.ts` — line chart for timestamp + numeric** — Mount with `columns: [{name:'bucket',duckType:'TIMESTAMP'},{name:'count',duckType:'BIGINT'}]` and 5 rows; assert a line chart element is present.
6. **Test: `SqlResultChart.spec.ts` — too-many-points fallback shown** — Mount with a result having 10,001 rows; assert the "Too many points" message is shown and no chart element is rendered.

---

## TRC-P10-013 — `SavedQueriesView.vue`

**Phase:** 10 — SQL Console, Saved Queries, Bundle Library  
**Design reference:** [tracer_phase10_design.md §6](./tracer_phase10_design.md#6-saved-queries) — §6.1 Data Model, §6.3 Built-in Queries, §6.4 Saved Query Endpoints, §6.5 Parameter Default Resolution

### Scope
**In scope:**
- `SavedQueriesView.vue` registered at route `/v/saved-queries`
- `useSavedQueries` composable wrapping `GET /api/saved-queries`, `POST /api/saved-queries/{id}/favorite`, and `POST /api/saved-queries/{id}/clone`
- List rendered as cards/rows; each entry shows: label, description excerpt, tags (chips), author, favorite star, run count, last-run date
- Filter bar: free-text search (label + description), filter by tag (multi-select chips), filter by author, "Favorites only" toggle
- Built-in queries displayed with a `[Built-in]` badge; Edit and Delete buttons are absent for them
- **Star toggle** — clicking calls `POST /api/saved-queries/{id}/favorite`; optimistically updates UI before API confirms
- **Clone button** (built-in queries only) — calls `POST /api/saved-queries/{id}/clone`; on success reloads the list and scrolls to the new entry
- **Run button** — executes the query inline using `useSqlExecution`; results appear in a collapsible panel below the card
- **Parameter prompting dialog** — when a query has `parameters.length > 0`, a modal is shown before execution with one input per parameter (pre-filled with default values resolved client-side per §6.5); user confirms or cancels; on confirm the parameters dict is passed to `POST /api/sql/execute`
- Loading, empty, and error states handled throughout

**Out of scope:**
- Creating a new saved query from this view (done in `SqlConsoleView` via TRC-P10-014)
- Editing a saved query's SQL text (redirect to `SqlConsoleView`)
- "Open in SQL Console" button (TRC-P10-014)
- Deleting user-created queries from this view (future affordance; Phase 10 focuses on reading and running)

### Constraints
- Built-in queries are identified by `isBuiltIn === true` in the API response; the frontend must not render delete/edit actions for them regardless of any API permissiveness
- The parameter prompting dialog must validate that numeric-typed parameters (`INT`, `BIGINT`, `DOUBLE`, etc.) are parseable before enabling the Run button
- Special default-value tokens (`session_start`, `session_end`, `now`) are displayed as-is in the dialog input; the API resolves them at execution time

### Success Conditions
1. **Test: `SavedQueriesView.spec.ts` — list renders N cards** — Mock `useSavedQueries` to return 3 queries; mount; assert 3 query card elements are present.
2. **Test: `SavedQueriesView.spec.ts` — built-in badge shown; Delete absent** — Include one query with `isBuiltIn: true`; assert `[Built-in]` badge is visible; assert no Delete button exists for that card.
3. **Test: `SavedQueriesView.spec.ts` — text filter narrows list** — 3 queries with distinct labels; type one label into the search box; assert only 1 card is visible.
4. **Test: `SavedQueriesView.spec.ts` — star toggle calls API and updates UI** — Click the star on a non-favorite query; assert `POST /api/saved-queries/:id/favorite` was called; assert the star icon reflects the updated state before the API confirms.
5. **Test: `SavedQueriesView.spec.ts` — parameter dialog shown for parameterised query** — Query with 1 parameter (`name: 'topic', duckType: 'VARCHAR', defaultValueText: 'weapons.fire'`); click Run; assert the parameter modal is visible with an input pre-filled with `'weapons.fire'`.
6. **Test: `SavedQueriesView.spec.ts` — cancel parameter dialog aborts run** — Show parameter dialog; click Cancel; assert `useSqlExecution.run` was NOT called.
7. **Test: `SavedQueriesView.spec.ts` — invalid numeric parameter disables Run** — Parameter with `duckType: 'BIGINT'`; clear the default and type `'not-a-number'`; assert the Run confirm button is disabled.
8. **Test: `SavedQueriesView.spec.ts` — clone built-in reloads list** — Click Clone on a built-in query; mock clone API to return a new query; assert the list reloads with one additional entry.
9. **E2E: `saved-queries.spec.ts` — built-ins visible and star toggles** — Navigate to `/v/saved-queries`; assert at least 5 rows visible (built-in queries seeded on startup); click the star on the first non-starred entry; assert the star icon changes state.

---

## TRC-P10-014 — "Save Query" and "Open in SQL Console" Affordances

**Phase:** 10 — SQL Console, Saved Queries, Bundle Library  
**Design reference:** [tracer_phase10_design.md §6.4](./tracer_phase10_design.md#64-saved-query-endpoints) — `POST /api/saved-queries`; [§5.3](./tracer_phase10_design.md#53-sqlconsoleviewvue) — `loadSavedQuery`, `showSavedQueries` toggle; [§6.3](./tracer_phase10_design.md#63-built-in-queries) — clone semantics

### Scope
**In scope:**
- **"Save" button in `SqlConsoleView`** toolbar: opens `SaveQueryDialog.vue` — a modal prompting for label (required), description (optional), tags (optional, chip input); on submit calls `POST /api/saved-queries`; on success shows a brief confirmation message and closes the modal
- `SaveQueryDialog.vue` component — modal with label text input, description textarea, tag chip editor; Save button is disabled while label is empty
- **"Saved queries…" button in `SqlConsoleView`** toolbar: opens `SavedQueryPicker.vue` — a compact modal listing saved queries with a search box; selecting an entry closes the modal and loads its `sql` into the editor via `loadSavedQuery()`
- **"Open in SQL Console" action in `SavedQueriesView`** — per-query button that pushes `router.push({ name: 'sql-console', params: { sessionId: currentSessionId }, query: { sql: query.sql } })`; for queries with parameters the SQL is loaded with placeholders un-substituted so the user can adjust in the console; disabled (with tooltip) when no bundle/session is currently active
- Current session ID is sourced from the Pinia store or an active-session route param

**Out of scope:**
- Editing the SQL or parameters of an existing saved query (future affordance)
- Parameter authoring UI (frontend sends `parameters: []` when saving from `SqlConsoleView`)

### Constraints
- The Save button in `SqlConsoleView` must be disabled while a query is actively running (loading state)
- `SavedQueryPicker` modal must be keyboard-navigable, focus-trapped while open, and closeable with Escape
- `POST /api/saved-queries` payload must include `{ label, description, sql, tags, parameters: [] }`; omitting `sql` or sending an empty string must not be possible through the UI

### Success Conditions
1. **Test: `SaveQueryDialog.spec.ts` — Save button disabled with empty label** — Mount `SaveQueryDialog` with `sql='SELECT 1'`; leave label input empty; assert Save button has `disabled` attribute.
2. **Test: `SaveQueryDialog.spec.ts` — submits correct payload** — Fill label `'My query'`, description `'desc'`, add tag `'perf'`; click Save; assert `POST /api/saved-queries` called with `{ label: 'My query', description: 'desc', tags: ['perf'], sql: 'SELECT 1', parameters: [] }`.
3. **Test: `SqlConsoleView.spec.ts` — Save button opens dialog** — Click the Save toolbar button; assert `SaveQueryDialog` is rendered.
4. **Test: `SqlConsoleView.spec.ts` — Save button disabled while running** — Set `loading = true` in `useSqlExecution` mock; assert the Save button is disabled.
5. **Test: `SqlConsoleView.spec.ts` — Saved queries picker opens on button click** — Click the "Saved queries…" toolbar button; assert `SavedQueryPicker` is rendered.
6. **Test: `SavedQueryPicker.spec.ts` — selecting a query loads SQL and closes picker** — Mock list with 2 queries; click the first entry; assert `loadSavedQuery` was called with that query's SQL and the picker is no longer rendered.
7. **Test: `SavedQueriesView.spec.ts` — Open in console disabled without active session** — Render with no active session in store; assert the "Open in console" button is disabled or absent.
8. **Test: `SavedQueriesView.spec.ts` — Open in console pushes correct route** — Mock active session `'sess-abc'`; click "Open in console" on a query with `sql: 'SELECT 1'`; assert `router.push` called with `{ name: 'sql-console', params: { sessionId: 'sess-abc' }, query: { sql: 'SELECT 1' } }`.
9. **E2E: `sql-console.spec.ts` — save query and find in SavedQueriesView** — Execute a query; click Save; fill label `'E2E Saved Query'`; confirm; navigate to `/v/saved-queries`; assert `'E2E Saved Query'` appears in the list.

---

## TRC-P10-015 — `BundleLibraryView.vue` — Full Bundle Library

**Phase:** 10 — SQL Console, Saved Queries, Bundle Library  
**Design reference:** [tracer_phase10_design.md §7](./tracer_phase10_design.md#7-bundle-library-enhancements) — §7.1 BundleCard.vue, §7.2 BundleLibraryView.vue, §7.3 BundleLibraryService, §7.4 Endpoint Extension

### Scope
**In scope:**
- `BundleLibraryView.vue` registered at route `/v/bundles`, replacing Phase 5's basic bundle listing
- `BundleCard.vue` component — displays per-bundle: label (shows `"(unlabeled)"` placeholder when absent), description, tags as chips, session time range, built date (relative), file size (human-readable bytes), last-opened date with a stale CSS class when > 30 days ago; actions: Open, Edit, Export, Archive, Delete
- `BundleFilterPanel.vue` — tag multi-select checkboxes, date-range pickers for session start, "Show archived" toggle, free-text search (label + description + tags)
- `BundleMetadataEditor.vue` — modal dialog for editing label, description, and tags; calls `PUT /api/bundles/{id}/metadata` on save
- Sort controls: sort by built date, session start, size, or label; ascending/descending toggle
- **Open** action: calls `POST /api/bundles/{id}/opened` to record last-opened timestamp, then navigates to `/v/scenario/{sessionId}`
- **Export** action: triggers download via `window.location.href = /api/bundles/{id}/download`
- **Import** button (in header): hidden `<input type="file" accept=".zip">` triggered programmatically; on file selection POSTs to `POST /api/bundles/import` as `multipart/form-data`; shows uploading state; reloads list on success; resets the file input after each attempt so the same file can be re-imported
- **Archive** action: calls `PUT /api/bundles/{id}/metadata` with `{ isArchived: true }`; bundle disappears from default list; revealed by "Show archived" toggle
- **Delete** action: shows `confirm()` dialog with the bundle label; on confirmation calls `DELETE /api/bundles/{id}`; reloads list
- Empty states: `"No bundles yet."` (empty list) and `"No bundles match the filter."` (filtered empty)
- `useBundleLibrary` composable wrapping `GET /api/bundles/library`

**Out of scope:**
- Bundle rebuild or re-aggregation from this view
- Bulk operations (multi-select delete or archive)
- Bundle versioning or diff

### Constraints
- Zip-slip protection on import is enforced **backend** (TRC-P10-008); the frontend only validates MIME type (`.zip`) in the file picker accept attribute
- Delete confirmation uses `confirm()` (browser native) for Phase 10; a custom modal can replace it post-phase
- The file input element must be reset after each import attempt (set `inputEl.value = ''`) so the same file can be re-selected

### Success Conditions
1. **Test: `BundleLibraryView.spec.ts` — renders one card per bundle** — Mock `useBundleLibrary` returning 3 bundles; assert 3 `BundleCard` instances rendered.
2. **Test: `BundleLibraryView.spec.ts` — filter by tag** — 3 bundles; two tagged `'production'`, one tagged `'debug'`; check `'production'` filter; assert 2 cards visible.
3. **Test: `BundleLibraryView.spec.ts` — archived bundles hidden by default** — 1 archived bundle; default state: 0 visible; enable "Show archived"; assert 1 card visible.
4. **Test: `BundleLibraryView.spec.ts` — sort by size descending** — 3 bundles with `sizeBytes` 100, 200, 50; sort descending by size; assert order is 200 → 100 → 50.
5. **Test: `BundleMetadataEditor.spec.ts` — save calls PUT with correct payload** — Mount with a bundle; change label to `'Sprint 42'`; click Save; assert `PUT /api/bundles/:id/metadata` called with `{ label: 'Sprint 42' }`.
6. **Test: `BundleCard.spec.ts` — unlabeled placeholder shown** — Mount with `bundle.label = null`; assert displayed text is `'(unlabeled)'`.
7. **Test: `BundleCard.spec.ts` — stale CSS class applied for last-opened > 30 days ago** — Bundle with `lastOpenedAtUtc` 40 days in the past; assert the last-opened element has the stale CSS class.
8. **Test: `BundleLibraryView.spec.ts` — delete calls API after confirm** — Mock `window.confirm` to return `true`; click Delete; assert `DELETE /api/bundles/:id` was called and `useBundleLibrary.load()` re-invoked.
9. **Test: `BundleLibraryView.spec.ts` — delete cancelled stops API call** — Mock `window.confirm` to return `false`; click Delete; assert no DELETE request was made.
10. **Test: `BundleLibraryView.spec.ts` — import file input posts multipart** — Simulate file input `change` event with a `.zip` File object; assert `POST /api/bundles/import` was called with `multipart/form-data`; assert file input value reset to `''` after call.
11. **E2E: `bundle-library.spec.ts` — edit label persists** — Navigate to `/v/bundles`; click Edit on first bundle; fill label `'E2E Label'`; save; assert the bundle card updates to show `'E2E Label'`.
12. **E2E: `bundle-library.spec.ts` — archive hides then show-archived reveals** — Click Archive on a bundle; assert it disappears from the list; enable "Show archived"; assert it reappears with the Archived badge.

---

## TRC-P10-016 — "Show SQL for This View" Affordance

**Phase:** 10 — SQL Console, Saved Queries, Bundle Library  
**Design reference:** [tracer_phase10_design.md §8](./tracer_phase10_design.md#8-cross-view-show-sql-for-this-view-affordance) — §8.1 SQL Generation Per View, §8.2 Educational Value; [§1.1](./tracer_phase10_design.md#11-what-phase-10-delivers) — Cross-View Polish; [§1.3](./tracer_phase10_design.md#13-success-criteria) — success criterion #1

### Scope
**In scope:**
- `ShowSqlButton.vue` — reusable button accepting `:sql` and `:session-id` props; on click pushes `{ name: 'sql-console', params: { sessionId }, query: { sql } }` to the router; has a `title="Open the current filter as SQL"` attribute for accessibility
- `src/utils/showSqlGenerators.ts` — pure functions that convert each view's current filter state to an equivalent SQL string:
  - `timelineFilterToSql(filter)` — `SELECT` from `events` with the current time range, topic, publisher node, subscriber node, trace ID, and entity ID as `WHERE` clauses
  - `causalTreeFilterToSql(traceId)` — `SELECT event_id, publisher_node, topic, publish_wallclock FROM events WHERE trace_id = <decimal>`
  - `entityHistoryFilterToSql(entityId, from, to)` — `SELECT ... FROM events WHERE entity_id = '...' AND publish_wallclock >= ... AND publish_wallclock < ...`
  - `replicationLatencyFilterToSql(filter)` — `APPROX_QUANTILE` aggregate matching Phase 9's latency view filter shape
  - `gapDetectionFilterToSql(filter)` — gap-detection logic matching Phase 9's filter parameters
- Add `<ShowSqlButton>` to the toolbar of: `TimelineView.vue`, `CausalTreeView.vue` (Phase 6), `EntityHistoryView.vue` (Phase 7), `ReplicationLatencyView.vue` (Phase 9), `GapDetectionView.vue` (Phase 9)
- Each view computes the SQL string reactively from its current filter state and passes it to `ShowSqlButton`; the SQL Console reads `?sql=` on mount (TRC-P10-011)

**Out of scope:**
- Backend `/api/sql/view-template` endpoint (covered by TRC-P10-009); TRC-P10-016 uses the frontend-side generators
- Exact semantic equivalence with the view's internal multi-interval union query — the generated SQL is "shape-equivalent, not literal" per §8.2
- `NetworkTopologyView` — the graph view is not reducible to a simple tabular query in Phase 10

### Constraints
- SQL string values embedded in generated `WHERE` clauses must be escaped with single-quote doubling (`'` → `''`) to prevent inadvertent syntax errors when pasted into the editor
- `ShowSqlButton` must be a `<button>` element (not a `<div>`) and must be keyboard-accessible
- Hex trace IDs must be converted to their decimal `UBIGINT` equivalent in the generated SQL (DuckDB's `trace_id` column stores the value as an integer)

### Success Conditions
1. **Test: `showSqlGenerators.spec.ts` — timelineFilterToSql with full filter** — Call `timelineFilterToSql({ from, to, topic: 'weapons.fire', publisherNode: 'node1' })`; assert result contains `topic = 'weapons.fire'` and `publisher_node = 'node1'`.
2. **Test: `showSqlGenerators.spec.ts` — single-quote escaping in generated SQL** — Topic = `"O'Brien"`; assert result contains `'O''Brien'`.
3. **Test: `showSqlGenerators.spec.ts` — causalTreeFilterToSql hex-to-decimal** — Call `causalTreeFilterToSql('DEADBEEF01234567')`; assert result contains the decimal equivalent of the hex value (not the hex string itself).
4. **Test: `showSqlGenerators.spec.ts` — entityHistoryFilterToSql produces entity_id clause** — Call with `entityId = 'my-entity'` and a time range; assert result contains `entity_id = 'my-entity'` and both time-bound clauses.
5. **Test: `ShowSqlButton.spec.ts` — click pushes correct route** — Mount with `sql="SELECT 1"` and `sessionId="abc"`; click; assert `router.push` called with `{ name: 'sql-console', params: { sessionId: 'abc' }, query: { sql: 'SELECT 1' } }`.
6. **Test: `TimelineView.spec.ts` — ShowSqlButton present in toolbar** — Mount `TimelineView` with a mock session and filter; assert a `ShowSqlButton` element is present in the toolbar area.
7. **E2E: `sql-console.spec.ts` — Show SQL from timeline pre-populates editor** — Navigate to `/v/timeline/test-session?topic=weapons.fire`; click "Show SQL"; assert URL changes to `/v/sql/test-session?sql=...`; assert editor content contains `weapons.fire`.

---

## TRC-P10-017 — Run-and-Pivot from SQL Results

**Phase:** 10 — SQL Console, Saved Queries, Bundle Library  
**Design reference:** [tracer_phase10_design.md §5.4](./tracer_phase10_design.md#54-sqlresulttable) — SqlResultTable pivot logic; [§1.3](./tracer_phase10_design.md#13-success-criteria) — success criterion #7; [§11](./tracer_phase10_design.md#11-definition-of-done-for-phase-10) — pivot checklist

### Scope
**In scope:**
- `SqlResultTable.vue` detects "pivotable" columns: any column whose name (case-insensitive) is one of `event_id`, `entity_id`, `trace_id`, `publish_wallclock`
- When at least one pivotable column exists, an extra `⛏` header column is appended; each data row in that column contains inline button(s) with destination-labelled text (`event →`, `entity →`, `trace →`, `time →`)
- Pivot routing:
  - `event_id` → `router.push({ name: 'timeline', params: { sessionId }, query: { select: String(value) } })`
  - `entity_id` → `router.push({ name: 'entity-history', params: { entityId: String(value) }, query: { session: sessionId } })`
  - `trace_id` → `router.push({ name: 'causal-by-trace', params: { traceId: String(value) } })`
  - `publish_wallclock` → `router.push({ name: 'timeline', params: { sessionId }, query: { from: (t−2s).toISOString(), to: (t+2s).toISOString() } })`
- If `publish_wallclock` value produces `NaN` from `new Date(String(value)).getTime()`, the `time →` button for that row is **disabled** with a `title` attribute explaining the problem
- The pivot `⛏` column is omitted entirely when no result column is pivotable

**Out of scope:**
- Right-click context menu (inline buttons are sufficient for Phase 10)
- Pivoting `publisher_node` or `subscriber_node` to a topology view
- Multi-row bulk pivot

### Constraints
- Each pivot button must have an accessible `title` attribute naming the destination view
- `sessionId` is passed to `SqlResultTable` as a prop (already required by TRC-P10-011); pivot routes that need it use this prop
- The `⛏` column is always the last column; it does not participate in client-side sort

### Success Conditions
1. **Test: `SqlResultTable.spec.ts` — no pivot column for unrecognised columns** — Render with columns `['topic', 'count']`; assert no `⛏` column header present.
2. **Test: `SqlResultTable.spec.ts` — pivot column added for event_id** — Render with columns `['event_id', 'topic']`; assert `⛏` column header is present and each data row contains a pivot button.
3. **Test: `SqlResultTable.spec.ts` — event_id pivot calls correct route** — Click the `event →` button in row 0 (value `42`); assert `router.push` called with `{ name: 'timeline', query: { select: '42' } }`.
4. **Test: `SqlResultTable.spec.ts` — entity_id pivot calls correct route** — Click `entity →`; assert `router.push` called with `{ name: 'entity-history', params: { entityId: '...' } }`.
5. **Test: `SqlResultTable.spec.ts` — trace_id pivot calls correct route** — Click `trace →`; assert `router.push` called with `{ name: 'causal-by-trace', params: { traceId: '...' } }`.
6. **Test: `SqlResultTable.spec.ts` — publish_wallclock pivot uses ±2-second window** — Row value `'2026-05-01T12:00:00.000Z'`; click `time →`; assert `router.push` called with `from = '2026-05-01T11:59:58.000Z'` and `to = '2026-05-01T12:00:02.000Z'`.
7. **Test: `SqlResultTable.spec.ts` — invalid timestamp disables pivot button** — Row `publish_wallclock` value is `'not-a-date'`; assert the `time →` button for that row has the `disabled` attribute.
8. **E2E: `sql-console.spec.ts` — pivot to entity history** — Execute `SELECT entity_id, topic FROM events LIMIT 1`; assert `⛏` column is visible; click `entity →`; assert the URL navigates to the entity-history view.

---

## TRC-P10-018 — Phase 10 Tests

**Phase:** 10 — SQL Console, Saved Queries, Bundle Library  
**Design reference:** [tracer_phase10_design.md §9](./tracer_phase10_design.md#9-test-plan-for-phase-10) — §9.1 Backend Unit, §9.2 Integration, §9.3 Frontend Unit, §9.4 E2E, §9.5 Security Tests; [§1.3](./tracer_phase10_design.md#13-success-criteria); [§11](./tracer_phase10_design.md#11-definition-of-done-for-phase-10)

### Scope
**In scope:**
- **Backend unit tests** (`Tracer.Tests.Unit/WebApi/` and `Tracer.Tests.Unit/Storage/`):
  - `SqlGuardrailsTests.cs` — all forbidden constructs rejected; all permitted constructs accepted; security edge cases: comment injection hiding DDL, mixed-case forbidden keywords (`InSeRt InTo`), multi-statement (`SELECT 1; SELECT 2`), quoted-identifier evasion (`"INSERT"`), WITH clause hiding DDL, `read_csv_auto`, `read_parquet` (per §9.1 and §9.5)
  - `SqlExecutorServiceTests.cs` — happy path SELECT, parameter binding, default row-limit injected when LIMIT absent, explicit LIMIT not modified, timeout returns `Timeout` state, outer cancellation token respected, invalid DuckDB SQL returns `Failed`, memory limit applied via PRAGMA (per §9.1)
  - `SavedQueryStoreTests.cs` — CRUD, built-in immutability (update/delete rejected at store layer), clone produces editable copy with new ID, run-count increment, last-run-at set on run (per §9.1)
  - `SavedQueriesEndpointsTests.cs` — all 8 HTTP endpoints return correct status codes and response DTOs; built-in guard enforced at HTTP layer (per §9.1)
  - `BundleLibraryEndpointsTests.cs` — library list, metadata update, record-opened, delete; import (valid zip accepted, malformed zip rejected) (per §9.1)
  - `BundleExportImportTests.cs` — export produces a valid zip; import round-trips the bundle; zip-slip attempts (entries with `../` paths) rejected with HTTP 400 and no file written outside bundles root (per §10 risk mitigation)
  - `ViewTemplateSqlTests.cs` — each supported view name (`timeline`, `causal-tree`, `entity-history`, `replication-latency`, `gap-detection`) produces a non-empty, read-only SQL string (passes `SqlGuardrails.Validate`); unsupported view name returns 404
- **Backend integration tests** (`Tracer.Tests.Integration/`):
  - `SqlConsoleRoundTripTests.cs` — start in-process server with a bundle seeded with a known event count; POST `/api/sql/execute` with `SELECT COUNT(*) FROM events`; assert `state = "Succeeded"` and row value equals the expected count; execute a parameterised query; execute a query that exceeds a 1-second timeout; execute invalid DuckDB SQL
  - `SavedQuerySeederTests.cs` — fresh SQLite DB after startup: GET `/api/saved-queries?builtIn=true` returns ≥ 5 results all with `isBuiltIn: true`; start again with the same DB: count does not increase (no duplication)
- **Frontend Vitest tests** (`tracer-viewer/tests/unit/`):
  - `SqlConsoleView.spec.ts` — consolidates coverage from TRC-P10-011/012/014/016/017: editor renders, Cmd+Enter dispatches, `?sql=` pre-populates, all four error states shown, truncated banner, CSV export, history persistence, chart tab toggle, Save button opens dialog, picker loads query, pivot column present, Show SQL button navigates
  - `SavedQueriesView.spec.ts` — consolidates coverage from TRC-P10-013/014: list renders, built-in badge, text filter, tag filter, star toggle, parameter dialog shown/cancelled/validated, clone reloads, Open in console disabled without session, Open in console pushes route
  - `BundleLibraryView.spec.ts` — consolidates coverage from TRC-P10-015: cards render, filter by tag, show-archived toggle, sort by size, metadata save calls API, unlabeled placeholder, stale badge, delete confirm/cancel, import posts multipart
- **E2E Playwright tests** (`tracer-viewer/tests/e2e/`):
  - `sql-console.spec.ts` — open bundle; navigate to SQL console; execute `SELECT COUNT(*) FROM events`; verify result table visible with numeric value; attempt `DROP TABLE events`; verify guardrail error message; Show SQL from timeline pre-populates editor; save query; pivot from result row to entity-history view
  - `bundle-library.spec.ts` — navigate to `/v/bundles`; add tag and filter by it; archive a bundle and verify it hides; enable Show archived and verify it reappears; edit label and verify card updates

**Out of scope:**
- Frontend snapshot / visual regression tests
- Performance benchmarking (§9.6) — these are manual checks or separate scripts, not in the automated CI suite
- Phase 1–9 tests (must continue to pass but are not authored in this task)

### Constraints
- `SqlGuardrailsTests` must use a parameterised test (xUnit `[Theory]` / `[InlineData]`) for the forbidden-keyword set so every variant is a distinct test case — no single test checking all variants at once
- Integration tests use `Tracer.TestHarness` for in-process server setup; they must not rely on external processes or network connections
- Playwright E2E tests must be idempotent (safe to re-run against the same database state); use unique labels/tags per run if necessary to avoid pollution
- Frontend Vitest tests must mock all HTTP calls (`vi.fn()` or `msw` interceptors); no real backend required

### Success Conditions
1. **Test: `SqlGuardrailsTests.cs` — all forbidden keywords individually rejected** — `[Theory]` with `[InlineData]` rows for: `INSERT INTO t VALUES (1)`, `UPDATE t SET x=1`, `DELETE FROM t`, `CREATE TABLE t(x INT)`, `DROP TABLE t`, `ALTER TABLE t RENAME TO s`, `ATTACH 'file.db'`, `COPY (SELECT 1) TO 'out.csv'`, `PRAGMA threads=4`; each asserts `IsValid == false`.
2. **Test: `SqlGuardrailsTests.cs` — permitted constructs accepted** — `[Theory]` rows for `SELECT * FROM events`, `WITH cte AS (SELECT 1) SELECT * FROM cte`, `EXPLAIN SELECT 1`, `DESCRIBE events`; each asserts `IsValid == true`.
3. **Test: `SqlGuardrailsTests.cs` — comment injection rejected** — Input `/* */ DROP TABLE events`; after comment-stripping the leading keyword is `DROP`; assert `IsValid == false`.
4. **Test: `SqlGuardrailsTests.cs` — multi-statement rejected** — Input `SELECT 1; SELECT 2`; assert `IsValid == false` and `RejectionReason` contains `"single statement"`.
5. **Test: `SqlGuardrailsTests.cs` — mixed-case forbidden keyword rejected** — Input `InSeRt Into t VALUES (1)`; assert `IsValid == false`.
6. **Test: `SqlExecutorServiceTests.cs` — timeout returns Timeout state** — Execute a query designed to run longer than a 1-second configured timeout (e.g., a cross-join generating ≥ 1M rows on fixture data); assert `State == SqlExecutionState.Timeout`.
7. **Test: `SqlConsoleRoundTripTests.cs` — execute SELECT COUNT(*) returns expected count** — Setup: start in-process server with a bundle seeded with exactly 42 events; POST `/api/sql/execute` with `{ "sql": "SELECT COUNT(*) FROM events" }`; assert HTTP 200, `state = "Succeeded"`, `rows[0][0] == 42`.
8. **Test: `SavedQuerySeederTests.cs` — built-ins present after first startup** — Fresh server start; GET `/api/saved-queries?builtIn=true`; assert `count >= 5` and every entry has `isBuiltIn: true`.
9. **Test: `SavedQuerySeederTests.cs` — no duplication on second startup** — Start server a second time with the same SQLite DB; GET `/api/saved-queries?builtIn=true`; assert count equals the count from first startup.
10. **Test: `BundleExportImportTests.cs` — zip-slip rejected** — Construct a zip archive containing an entry with path `../../evil.txt`; POST to `/api/bundles/import`; assert HTTP 400 response; assert no file exists outside the configured bundles root directory.
11. **E2E: `sql-console.spec.ts` — full query round trip** — Start app with test bundle; navigate to `/v/sql/test-session`; execute `SELECT COUNT(*) FROM events`; assert `.sql-result-table` is visible and first data cell contains a number.
12. **E2E: `bundle-library.spec.ts` — filter by tag then archive** — Navigate to `/v/bundles`; add tag `'e2e-test'` to first bundle via Edit; enable `'e2e-test'` filter; assert only 1 card visible; click Archive; assert that card disappears from the default (non-archived) list.

<!-- PHASE 10 TASKS END -->

<!-- PHASE 11 TASKS BEGIN -->

# Phase 11 — Real Adapter Integration: DDS, Sync, Shared Memory, NAS

---

## TRC-P11-001 — `Tracer.Adapters.DDS` Assembly — DDS Diagnostic Data Source

**Phase:** 11 — Real Adapter Integration  
**Design reference:** [tracer_phase11_design.md §3](./tracer_phase11_design.md#3-the-dds-adapter)  
**Architecture reference:** [tracer_architecture_v1.md §6](./tracer_architecture_v1.md#6-component-responsibilities) *(adapter layer contracts)*

### Scope

**In scope:**
- New assembly `Tracer.Adapters.DDS` with all types listed in §2 project layout.
- `DdsDiagnosticDataSource` implementing `IDiagnosticDataSource` (§3.3): bounded ingest `Channel<DiagnosticRecord>` with `DropOldest` back-pressure; one subscriber per configured topic; async-enumerable over the channel.
- `DdsSampleTranslator` (§3.4): per-topic translation to `EventRecord` / `StateSampleRecord`; uses `DdsTopicRegistry` for kind-dispatch; stamps `receive_wallclock` at translation time; reads `SourceTimestamp` for `publish_wallclock`.
- `DdsTraceContextExtractor` (§3.6): reflection-compiled `Expression`-based accessors for `trace_id`, `event_id`, `parent_event_id` fields; accessor cache keyed by sample type; returns `TraceContext.Empty` for non-Event topics.
- `DdsTopicRegistry` (§3.5): dictionary-backed catalog of `DdsTopicMetadata`; populated from `DdsAdapterConfig.Topics` at startup.
- `DdsSubscriberFactory` (§3.7): wraps the Cyclone DDS C# binding API (see [CycloneDDS.NET.README.md](./CycloneDDS.NET.README.md)) behind `IDdsSample`; returns an `IDisposable` subscriber handle per topic.
- `IDdsSample` abstraction isolating Tracer.Core from Cyclone DDS types.
- `DdsAdapterConfig` / `DdsTopicSubscription` / `CycloneDdsParticipantConfig` (§3.8).
- `DdsTopicKind` enum (`Event`, `SlowState`, `FastState`).
- Drop-count structured log warning when ingest channel is full (§3.3 `OnSampleReceived`).
- The assembly loads **into the simulation process**, not the TracerAgent process (§3.1 architectural note).
- Cyclone DDS C# bindings taken as a NuGet dependency (`CycloneDDS.NET`); not reimplemented here.

**Out of scope:**
- SharedMemory transport (TRC-P11-002).
- Adapter selection / DI wiring (TRC-P11-005).
- Any simulation-side changes; those are the integration project's responsibility (§1.2).
- Reimplementing or forking the Cyclone DDS binding.

### Constraints

- `Tracer.Core` must not reference any Cyclone DDS types directly; only `Tracer.Adapters.DDS` references the binding.
- Must never block a DDS callback thread (§3.3 — `DropOldest` not block on channel full).
- Unit tests must not require a real DDS participant — mock `IDdsSample` implementations stand in.
- Compiled expression accessors must be cached per sample type; not recompiled per sample.
- `LangVersion`, nullable, warnings-as-errors apply per `Directory.Build.props`.

### Success Conditions

1. **Test: `DdsSampleTranslatorTests` — Event-kind translation** — Construct a mock `IDdsSample` with known `SourceTimestamp`, `SequenceNumber`, `traceId`, `eventId`, `parentEventId` fields and a registered Event-kind topic; call `Translate`; assert returned `EventRecord` has matching `TraceId`, `EventId`, `ParentEventId`, `PublishWallclock`, `ReceiveWallclock`, `SequenceNumber`, and `Topic`.
2. **Test: `DdsSampleTranslatorTests` — SlowState-kind translation** — Mock `IDdsSample` for a SlowState topic; assert `StateSampleRecord` with `Kind = StateSampleKind.Slow` and no trace context fields set.
3. **Test: `DdsSampleTranslatorTests` — FastState-kind translation** — Mock `IDdsSample` for a FastState topic with known typed values; assert `StateSampleRecord` with `Kind = StateSampleKind.Fast` and `TypedValues` populated.
4. **Test: `DdsSampleTranslatorTests` — Unknown topic returns null** — Pass a topic name not in `DdsTopicRegistry`; assert `Translate` returns `null` and does not throw; assert a warning is logged.
5. **Test: `DdsTraceContextExtractorTests` — camelCase field names resolved** — Sample type with properties `traceId`, `eventId`, `parentEventId`; assert `Extract` returns the correct `TraceContext` values.
6. **Test: `DdsTraceContextExtractorTests` — PascalCase field names resolved** — Sample type with `TraceId`, `EventId`, `ParentEventId`; assert correct extraction.
7. **Test: `DdsTraceContextExtractorTests` — Missing field throws on first use** — Sample type missing `traceId`/`TraceId`; assert `InvalidOperationException` is thrown with the type name in the message.
8. **Test: `DdsTraceContextExtractorTests` — Accessor cache hit** — Call `Extract` twice with the same type; verify (via counter or mock) that `BuildAccessors` is called exactly once.
9. **Test: `DdsDiagnosticDataSourceTests` — Drop-oldest on full channel** — Configure `IngestBufferSize = 5`; simulate 10 rapid `OnSampleReceived` calls via reflection or a test hook; assert that at most 5 records are yielded and at least one `LogWarning` is emitted mentioning the topic name.
10. **Test: `DdsDiagnosticDataSourceTests` — CancellationToken stops enumeration** — Start `GenerateAsync` with a `CancellationToken`; cancel it; assert the `IAsyncEnumerable` completes without exception.

---

## TRC-P11-002 — `Tracer.Adapters.SharedMemory` Assembly — Ring Buffer IPC Transport

**Phase:** 11 — Real Adapter Integration  
**Design reference:** [tracer_phase11_design.md §4](./tracer_phase11_design.md#4-the-shared-memory-transport)  
**Architecture reference:** [tracer_architecture_v1.md §6](./tracer_architecture_v1.md#6-component-responsibilities) *(IAgentTransport)*

### Scope

**In scope:**
- New assembly `Tracer.Adapters.SharedMemory` — BCL only, no new NuGet dependencies.
- `SharedMemoryRingBuffer` (§4.3): SPSC ring buffer over `MemoryMappedFile`; fixed-size header (4096 bytes) with magic, version, capacity, `write_offset`, `read_offset`, `producer_pid`, `consumer_pid`, `producer_heartbeat_ticks`, `consumer_heartbeat_ticks`, `dropped_count`; `Volatile.Read`/`Volatile.Write` for cross-process atomic access; wraparound-via-padding-marker discipline; `TryWrite` with drop-oldest when full; `TryRead` with wrap handling; `Create(name, capacity)` and `Open(name)` factory methods.
- `SharedMemoryDiagnosticRecordCodec` (§4.4): source-generated `System.Text.Json` serializer (`DiagnosticRecordSerializerContext`); `SerializedRecord` wrapper carrying `Kind` discriminator; `Encode` and `Decode` methods.
- `SharedMemoryWriter` — thin helper for the producer side; exposes `Write(DiagnosticRecord)` delegating to `SharedMemoryRingBuffer.TryWrite` + semaphore signal.
- `SharedMemoryReader` — thin helper for the consumer side; exposes `ReadAvailable()` draining the buffer.
- `SharedMemoryTransport` (§4.5): implements `IAgentTransport`; `CreateProducer` and `CreateConsumer` factory methods; `EnqueueAsync` (producer, non-blocking); `ConsumeAsync` (consumer, `IAsyncEnumerable`, 100 ms semaphore wait then drain); cancellation-aware; drop-count monitoring via `GetDroppedCount()`.
- `SharedMemoryConfig`: `SharedMemoryName`, `SemaphoreName`, `CapacityBytes` (default 64 MB).
- Drop telemetry: `GetDroppedCount()` read from header; consumer-side `MonitorTransportAsync`-compatible API (§4.6 pattern; actual periodic polling lives in TracerAgent, not in this assembly).

**Out of scope:**
- The periodic monitor loop in TracerAgent (TRC-P11-007).
- Adapter selection / DI wiring (TRC-P11-005).
- Cross-machine transport; this is single-machine only (§4.1 requirements table).

### Constraints

- No new NuGet packages — `System.IO.MemoryMappedFiles` and `System.Threading.Semaphore` are BCL.
- `TryWrite` must never block; it advances the read pointer (drop-oldest) if needed.
- `TryRead` must be callable from a tight consumer loop with no allocations beyond the returned byte array.
- Tests that exercise cross-process behavior must use a subprocess (or in-process simulation with two `SharedMemoryRingBuffer` instances sharing the same named mapping in one process for unit coverage).

### Success Conditions

1. **Test: `SharedMemoryRingBufferTests` — sequential write/read** — Create buffer; write 3 known records; read them back in order; assert byte-for-byte equality.
2. **Test: `SharedMemoryRingBufferTests` — wraparound** — Write records until the ring wraps (write_offset wraps past capacity); assert subsequent reads return all written records in order.
3. **Test: `SharedMemoryRingBufferTests` — drop-oldest on fill** — Fill the ring to capacity then write one more record; assert `dropped_count` incremented; assert consumer reads the most-recent data (not the oldest).
4. **Test: `SharedMemoryRingBufferTests` — padding-marker handling** — Write a record that would straddle the capacity boundary; assert producer inserts padding and starts at offset 0; consumer skips padding and reads correctly.
5. **Test: `SharedMemoryTransportTests` — round-trip** — Create producer transport and consumer transport sharing the same named mapping in-process; enqueue 100 `DiagnosticRecord` instances; consume them; assert all arrive in order with matching field values.
6. **Test: `SharedMemoryTransportTests` — CancellationToken stops ConsumeAsync** — Start consuming; cancel the token; assert `IAsyncEnumerable` terminates without exception within 200 ms.
7. **Test: `SharedMemoryTransportTests` — producer does not block when consumer is slow** — Measure `EnqueueAsync` wall-time with a paused consumer; assert it completes in < 1 ms per call at all fill levels.
8. **Test: `SharedMemoryDiagnosticRecordCodecTests` — EventRecord round-trip** — Encode an `EventRecord` with all fields set (including Unicode payload); decode; assert all fields equal.
9. **Test: `SharedMemoryDiagnosticRecordCodecTests` — StateSampleRecord round-trip** — Same pattern for `StateSampleRecord`.
10. **Test: `SharedMemoryDiagnosticRecordCodecTests` — source-gen path used** — Assert that `DiagnosticRecordSerializerContext.Default` is the context used; no reflection-fallback warnings in output.

---

## TRC-P11-003 — `Tracer.Adapters.Sync` Assembly — Telemetry Upload via Sync System

**Phase:** 11 — Real Adapter Integration  
**Design reference:** [tracer_phase11_design.md §5](./tracer_phase11_design.md#5-the-sync-adapter)  
**Sync contract reference:** [sync_addendum_telemetry.md §A4](./sync_addendum_telemetry.md#a4-rest-api-additions) *(REST endpoints)*

### Scope

**In scope:**
- New assembly `Tracer.Adapters.Sync`.
- `SyncSystemUploadService` (§5.3) implementing `ITelemetryUploadService`: `SubmitAsync` calling `POST /api/telemetry` (per `sync_addendum_telemetry.md §A4.1`) with `nodeId`, `intervalTimestamp`, `intervalStartUtc`, `intervalEndUtc`, `files[]`; returns `UploadIntentId`; logs `Information` on success. `GetStatusAsync` calling `GET /api/telemetry/{nodeId}/{intervalTimestamp}` and mapping status. `WaitForCompletionAsync` polling with exponential backoff (start 2 s, cap 60 s) until `Completed` or `Failed`.
- `SyncMasterRestClient` (§5.4): thin `HttpClient` wrapper; `RegisterUploadIntentAsync` (`POST /api/telemetry`); `GetIntentStatusAsync` (`GET /api/telemetry/{nodeId}/{intervalTimestamp}`); throws on non-success status codes.
- `SyncAdapterConfig` (§5.5): `SyncMasterBaseUrl`, `RequestTimeout` (default 30 s), `RetryAttempts` (default 3).
- Retry with backoff on transient HTTP failures (5xx, timeout); after `RetryAttempts` exhausted, log warning and return so the caller can decide (§5.6).
- `SyncMasterRestClient` registered via `IHttpClientFactory` (named client) for proper socket lifecycle.

**Out of scope:**
- Implementing sync system server-side endpoints — those are the sync team's responsibility (§1.2, `sync_addendum_telemetry.md`).
- Zip creation — the sync system's agent handles that via the `UploadTelemetry` SignalR command (`sync_addendum_telemetry.md §A5.1`).
- Adapter selection / DI wiring (TRC-P11-005).

### Constraints

- `HttpClient` must be obtained via `IHttpClientFactory`; no `new HttpClient()` calls.
- All REST requests include a `CancellationToken`; no fire-and-forget.
- `WaitForCompletionAsync` must respect cancellation during the delay between polls.
- Tests mock `HttpMessageHandler`; no real HTTP calls in unit tests.
- Idempotency: if `POST /api/telemetry` returns an existing `intentId` for the same `(nodeId, intervalTimestamp)`, `SubmitAsync` must return it without error (per `sync_addendum_telemetry.md §A4.1` idempotency note).

### Success Conditions

1. **Test: `SyncSystemUploadServiceTests` — SubmitAsync sends correct body** — Mock handler captures the request body; call `SubmitAsync` with known `IntervalUploadRequest`; assert `POST /api/telemetry` was called with the correct `nodeId`, `intervalTimestamp`, and `files` array.
2. **Test: `SyncSystemUploadServiceTests` — SubmitAsync returns intentId** — Mock handler returns `{ "intentId": "abc-123" }`; assert `SubmitAsync` returns `UploadIntentId("abc-123")`.
3. **Test: `SyncSystemUploadServiceTests` — WaitForCompletionAsync polls until Completed** — Mock handler returns `"Pending"` twice then `"Complete"`; assert `WaitForCompletionAsync` returns `UploadResult { Status = Completed }`; assert `GetIntentStatus` was called exactly 3 times.
4. **Test: `SyncSystemUploadServiceTests` — WaitForCompletionAsync surfaces Failed** — Mock handler returns `"Failed"` with `errorMessage`; assert result has `Status = Failed` and `ErrorMessage` set.
5. **Test: `SyncSystemUploadServiceTests` — WaitForCompletionAsync respects cancellation** — Cancel the token during a poll delay; assert `OperationCanceledException` is thrown without further HTTP calls.
6. **Test: `SyncSystemUploadServiceTests` — retry on 503** — Mock handler returns 503 twice then 201; assert `SubmitAsync` eventually succeeds and the handler was called 3 times.
7. **Test: `SyncSystemUploadServiceTests` — exhausted retries logs warning** — Mock handler always returns 503; assert after `RetryAttempts` a `LogWarning` is emitted and the exception propagates.
8. **Test: `SyncMasterRestClientTests` — non-success status code throws** — Mock returns 404; assert `HttpRequestException` propagates from `RegisterUploadIntentAsync`.

---

## TRC-P11-004 — `Tracer.Adapters.Nas` Assembly — NAS Storage Reader

**Phase:** 11 — Real Adapter Integration  
**Design reference:** [tracer_phase11_design.md §6](./tracer_phase11_design.md#6-the-nas-adapter)  
**Sync contract reference:** [sync_addendum_telemetry.md §A3](./sync_addendum_telemetry.md#a3-nas-layout) *(NAS directory layout)*

### Scope

**In scope:**
- New assembly `Tracer.Adapters.Nas` — BCL (`System.IO`) only; no new NuGet dependencies.
- `NasStorageReader` (§6.3) implementing `ITelemetryStorageReader`: `ListIntervalsAsync` enumerating `{NasRoot}/telemetry/{nodeId}/{intervalTimestamp}.zip` entries (per `sync_addendum_telemetry.md §A3.1`); skips zip files not yet fully present; returns `NodeIntervalDescriptor` list. `StageAsync` returning the zip path directly (Windows SMB transparent via UNC) or copying to a temp directory if `PreferLocalStaging = true`; cleanup action on `StagedInterval.Dispose()`.
- `SmbPathResolver`: maps `(nodeId, intervalTimestamp)` to UNC path `\\{NasRoot}\telemetry\{nodeId}\{intervalTimestamp}.zip`; validates path components to prevent directory traversal.
- Interval completeness check: interval zip is considered ready when it exists and the zip contains the `_ready` sentinel entry (per `sync_addendum_telemetry.md §A3.3`). Incomplete intervals are logged and skipped.
- `NasAdapterConfig` (§6.5): `NasRoot` (UNC path or local path), `PreferLocalStaging` (default `false`), `FileOperationTimeout` (default 30 s), `RetryOnTransientError` (default 3 attempts), `CircuitBreakerThreshold` (consecutive failures before tripping, default 5).
- Transient SMB error retry (§8 hardening, TRC-P11-007 will add circuit breaker; placeholder retry loop here).

**Out of scope:**
- Provisioning, mounting, or replicating the NAS — operations concern (§1.2).
- The aggregator's bundle-building logic — Phase 4 owns that (consumes `ITelemetryStorageReader`).
- Adapter selection / DI wiring (TRC-P11-005).

### Constraints

- `NasRoot` may be a UNC path (`\\server\share`) or a local path for dev/test; both must work via `System.IO`.
- `SmbPathResolver` must reject path components containing `..`, `/`, or null bytes (directory traversal prevention).
- Tests run against a temp directory on the local filesystem simulating NAS layout; no real SMB required.
- `StagedInterval` with `PreferLocalStaging = true` must delete the temp directory on `Dispose()` even if an exception occurs during use.

### Success Conditions

1. **Test: `NasStorageReaderTests` — ListIntervalsAsync discovers complete intervals** — Create temp dir with `telemetry/{nodeId}/{ts}.zip` containing a `_ready` entry; assert `ListIntervalsAsync` returns one descriptor with correct `NodeId` and `IntervalId`.
2. **Test: `NasStorageReaderTests` — skips interval zip without _ready sentinel** — Same layout but zip missing `_ready` entry; assert `ListIntervalsAsync` returns empty list and logs a warning.
3. **Test: `NasStorageReaderTests` — skips non-existent directory** — `NasRoot` points to a session that has no directory; assert `ListIntervalsAsync` returns empty without throwing.
4. **Test: `NasStorageReaderTests` — StageAsync without local staging returns source path** — `PreferLocalStaging = false`; call `StageAsync`; assert `StagedInterval.LocalPath` equals the source zip path; assert `Dispose()` does not delete it.
5. **Test: `NasStorageReaderTests` — StageAsync with local staging copies and cleans up** — `PreferLocalStaging = true`; call `StageAsync`; assert `StagedInterval.LocalPath` is different from source; assert file exists during use; assert temp dir is deleted on `Dispose()`.
6. **Test: `SmbPathResolverTests` — valid path resolves correctly** — `NasRoot = "\\\\nas\\tracer"`, `nodeId = "blue-cmd-01"`, `intervalTimestamp = "20260519T140000Z"`; assert result equals `"\\\\nas\\tracer\\telemetry\\blue-cmd-01\\20260519T140000Z.zip"`.
7. **Test: `SmbPathResolverTests` — directory traversal rejected** — `nodeId = "..\\evil"`; assert `ArgumentException` thrown.
8. **Test: `NasStorageReaderTests` — multiple nodes discovered** — Three node subdirectories each with one interval zip with `_ready`; assert `ListIntervalsAsync` returns 3 descriptors.

---

## TRC-P11-005 — `Tracer.AdapterSelection` Assembly — Adapter Registry and DI

**Phase:** 11 — Real Adapter Integration  
**Design reference:** [tracer_phase11_design.md §7](./tracer_phase11_design.md#7-adapter-selection-configuration-driven-di)

### Scope

**In scope:**
- New assembly `Tracer.AdapterSelection`.
- `AdapterRegistry` (§7.2): reads `adapters:dataSource`, `adapters:transport`, `adapters:upload`, `adapters:storageReader`, `adapters:clock` from `IConfiguration`; dispatches to the correct registration path for each adapter slot; throws `InvalidOperationException` with a clear message for unknown values.
- `AdapterRegistrationExtensions`: `AddTracerAdapters(this IServiceCollection, IConfiguration)` extension method that constructs and calls `AdapterRegistry.RegisterAdapters`.
- Supported values per slot (§7.1):
  - `dataSource`: `"mock"` → `MockDataSource`; `"dds"` → `DdsDiagnosticDataSource` (binds `DdsAdapterConfig` from `IConfiguration.GetSection("dds")`).
  - `transport`: `"in-process"` → mock in-process channel transport; `"shared-memory"` → `SharedMemoryTransport` (binds `SharedMemoryConfig` from `"sharedMemory"` section).
  - `upload`: `"local-file-system"` → mock local upload; `"sync"` → `SyncSystemUploadService` (binds `SyncAdapterConfig` from `"sync"` section; registers named `HttpClient`).
  - `storageReader`: `"local-file-system"` → mock local reader; `"nas"` → `NasStorageReader` (binds `NasAdapterConfig` from `"nas"` section).
  - `clock`: `"system"` → `SystemClock`; `"simulated"` → `SimulatedClock`.
- Mixed configurations (e.g., `"dds"` data source + `"local-file-system"` upload) must work correctly.

**Out of scope:**
- Implementing any of the adapters themselves (covered by TRC-P11-001 through TRC-P11-004).
- Host builder modifications in `Tracer.Agent` / `Tracer.Aggregator` (TRC-P11-006 covers configuration; the hosts call `AddTracerAdapters` which is part of this task).

### Constraints

- `Tracer.AdapterSelection` references all adapter assemblies (`Tracer.Adapters.Mock`, `Tracer.Adapters.DDS`, `Tracer.Adapters.SharedMemory`, `Tracer.Adapters.Sync`, `Tracer.Adapters.Nas`).
- `Tracer.Core` is not modified; all wiring is in this assembly.
- Default values when a key is absent: `dataSource` defaults to `"mock"`, others similarly default to mock/simulated equivalents — safe for a fresh `dotnet run` checkout.

### Success Conditions

1. **Test: `AdapterRegistryTests` — `dataSource: "mock"` registers MockDataSource** — Build `IServiceCollection`, call `AddTracerAdapters` with config `adapters:dataSource = "mock"`; resolve `IDiagnosticDataSource`; assert it is `MockDataSource`.
2. **Test: `AdapterRegistryTests` — `dataSource: "dds"` registers DdsDiagnosticDataSource** — Config `adapters:dataSource = "dds"` with minimal `dds` section; resolve `IDiagnosticDataSource`; assert it is `DdsDiagnosticDataSource`.
3. **Test: `AdapterRegistryTests` — `transport: "shared-memory"` registers SharedMemoryTransport** — Config `adapters:transport = "shared-memory"` with `sharedMemory` section; resolve `IAgentTransport`; assert it is `SharedMemoryTransport`.
4. **Test: `AdapterRegistryTests` — `upload: "sync"` registers SyncSystemUploadService** — Config `adapters:upload = "sync"` with `sync` section; resolve `ITelemetryUploadService`; assert it is `SyncSystemUploadService`.
5. **Test: `AdapterRegistryTests` — `storageReader: "nas"` registers NasStorageReader** — Config `adapters:storageReader = "nas"` with `nas` section; resolve `ITelemetryStorageReader`; assert it is `NasStorageReader`.
6. **Test: `AdapterRegistryTests` — unknown value throws** — Config `adapters:dataSource = "foobar"`; assert `InvalidOperationException` is thrown with `"foobar"` in the message.
7. **Test: `AdapterRegistryTests` — mixed config (dds + local-file-system upload)** — `dataSource = "dds"`, `upload = "local-file-system"`; resolve both interfaces; assert correct types; no registration errors.
8. **Test: `AdapterRegistryTests` — default values (no adapters section)** — Empty config; call `AddTracerAdapters`; all interfaces resolve to their mock equivalents; no exception.
9. **Test: Phase 1–10 integration suite still passes** — Run the existing `Tracer.Tests.Integration` suite with the default mock configuration; all tests pass.

---

## TRC-P11-006 — Configuration Additions — `appsettings.json` Adapter Sections

**Phase:** 11 — Real Adapter Integration  
**Design reference:** [tracer_phase11_design.md §7.1](./tracer_phase11_design.md#71-the-configuration-section) and [§7.4](./tracer_phase11_design.md#74-defaults-per-deployment)

### Scope

**In scope:**
- `Tracer.Agent/appsettings.json`: add `adapters`, `dds`, `sharedMemory`, `sync` sections. Default `adapters` block uses all mock/simulated values so `dotnet run` on a clean checkout works.
- `Tracer.Aggregator/appsettings.json` (and `Tracer.Aggregator.Cli`): add `adapters` and `nas` sections; default `storageReader` to `"local-file-system"`.
- Per-environment override files added to `Tracer.Agent` and `Tracer.Aggregator`:
  - `appsettings.Development.json` — mock adapters (same as defaults; explicit for clarity).
  - `appsettings.IntegrationReal.json` — all real adapters; `dds`, `sharedMemory`, `sync`, `nas` sections contain placeholder values (filled by the integration-real test fixture).
  - `appsettings.Production.json` — real adapters; settings documented with comments (actual values are environment-specific; shipped as templates).
- `TracerAgentHostBuilder` and `AggregatorHostBuilder` (and `Tracer.Aggregator.Cli/Program.cs`) call `services.AddTracerAdapters(configuration)` during DI setup.
- Schema documentation via XML doc comment on each config class (already required by `Directory.Build.props` analyzer settings).

**Out of scope:**
- Implementing the adapters themselves (TRC-P11-001 through TRC-P11-004).
- Deployment automation or CI secrets management.
- `Tracer.FakeNode` and `Tracer.Observer` configuration (observer is derived; FakeNode uses mock data directly).

### Constraints

- The JSON in `appsettings.json` must be valid and loadable via `Microsoft.Extensions.Configuration.Json`.
- Default config must not reference any infrastructure (no UNC paths, no real URLs, no DDS domain IDs that conflict with customer environments).
- The `appsettings.Production.json` template must include comments (using `//` style via JSON5-aware convention or a README note) documenting each required field.

### Success Conditions

1. **Test: Agent starts with default config** — `dotnet run --project Tracer.Agent` (or equivalent integration test startup) with only `appsettings.json` present; assert the agent starts without `InvalidOperationException` from adapter selection.
2. **Test: Aggregator starts with default config** — Same for `Tracer.Aggregator.Cli`; assert clean startup.
3. **Test: `IntegrationReal` environment resolves real adapters** — Set `DOTNET_ENVIRONMENT=IntegrationReal`; resolve `IDiagnosticDataSource`; assert it is `DdsDiagnosticDataSource` (type check only; no DDS participant started).
4. **Test: All Phase 1–10 integration tests still pass** — Run `Tracer.Tests.Integration` with `DOTNET_ENVIRONMENT=Development`; assert all tests pass.
5. **Test: Per-environment override merges correctly** — `appsettings.IntegrationReal.json` overrides `adapters:dataSource` to `"dds"`; assert that the merged config has `"dds"` not `"mock"`.

---

## TRC-P11-007 — Hardening — Resource Limits, Back-Pressure, and Error Recovery

**Phase:** 11 — Real Adapter Integration  
**Design reference:** [tracer_phase11_design.md §9](./tracer_phase11_design.md#9-hardening-items)

### Scope

**In scope:**
- **DDS adapter hardening** (§9.1, §9.2): `IngestBufferSize` read from `DdsAdapterConfig`; enforce the `DropOldest` mode already in place; emit a single structured log event per drop-burst (not per-sample) to avoid log flooding.
- **SharedMemory hardening** (§4.6, §9.1): `CapacityBytes` read from `SharedMemoryConfig`; `MonitorTransportAsync` periodic task in `TracerAgent` reading `GetDroppedCount()` every 5 s; emits `LogWarning` when `dropped_count` increases (pattern from §4.6 code snippet).
- **Sync upload hardening** (§5.6, §9.3): backlog tracking — TracerAgent maintains a counter of intervals awaiting upload; logs `LogWarning` when backlog exceeds a configurable threshold (default 3); on graceful shutdown, waits up to a configurable `ShutdownUploadFlushTimeout` (default 60 s) for in-flight uploads to complete before exiting.
- **NAS reader hardening** (§9.3): file operation timeout (configurable via `NasAdapterConfig.FileOperationTimeout`); retry on `IOException` with SMB error codes (up to `RetryOnTransientError` attempts, 2 s base delay); circuit breaker: after `CircuitBreakerThreshold` consecutive failures, `NasStorageReader` throws `CircuitBreakerOpenException` and logs `LogError`; circuit resets after a configurable `CircuitBreakerResetInterval` (default 60 s).
- **`/api/health` additions** (§9.4): add `sharedMemoryDropped`, `ingestChannelDepth`, `intervalsAwaitingUpload`, `lastIntervalCompletedAtUtc` to the agent health response; `sseConnectionsActive` to the observer health response.
- **Structured log schema discipline**: all adapter log events include `topicName`, `adapterId`, or equivalent correlation fields so operators can group by adapter in log aggregation.

**Out of scope:**
- Windows Job Object RSS limits (operations concern per §9.1); Tracer documents the recommended limits but does not set them.
- Alerting integrations (§1.2).
- Any new UI views.

### Constraints

- Hardening additions must not alter any existing interface contracts.
- Circuit breaker state must be per-`NasStorageReader` instance; not a global static.
- Monitoring loop in TracerAgent must not throw; swallow internal exceptions and log.
- All new structured log events must use `LoggerMessage.Define` (or `[LoggerMessage]` source-gen) per Phase 1 coding standards.

### Success Conditions

1. **Test: `SharedMemoryMonitorTests` — dropped count increase logs warning** — Fake a `SharedMemoryTransport` whose `GetDroppedCount()` returns 0 then 5 on successive calls; run `MonitorTransportAsync` for two cycles; assert one `LogWarning` emitted with `NewDrops = 5`.
2. **Test: `SharedMemoryMonitorTests` — no warning when count stable** — `GetDroppedCount()` always returns 0; run for 3 cycles; assert no warning.
3. **Test: `SyncUploadHardeningTests` — backlog threshold warning** — Enqueue 4 intervals (threshold = 3); assert `LogWarning` referencing backlog count.
4. **Test: `SyncUploadHardeningTests` — graceful shutdown waits for in-flight** — Signal shutdown while one upload is in-flight (mocked async); assert shutdown waits up to `ShutdownUploadFlushTimeout`; assert upload is awaited before process exits.
5. **Test: `NasReaderHardeningTests` — transient IOException retried** — Mock `System.IO.File` access throws `IOException` twice then succeeds; assert the read succeeds on the third attempt.
6. **Test: `NasReaderHardeningTests` — circuit breaker trips after threshold** — Mock always throws `IOException`; after `CircuitBreakerThreshold` calls, assert `CircuitBreakerOpenException` is thrown and `LogError` is emitted.
7. **Test: `NasReaderHardeningTests` — circuit breaker resets after interval** — Trip the circuit breaker; advance `IClock` past `CircuitBreakerResetInterval`; assert next call attempts the real operation.
8. **Test: `HealthEndpointTests` — new fields present in health response** — Start agent with mocked adapters; GET `/api/health`; assert response JSON contains `sharedMemoryDropped`, `ingestChannelDepth`, `intervalsAwaitingUpload`, `lastIntervalCompletedAtUtc`.

---

## TRC-P11-008 — Integration Test Infrastructure — `Tracer.Tests.Integration.Real`

**Phase:** 11 — Real Adapter Integration  
**Design reference:** [tracer_phase11_design.md §8](./tracer_phase11_design.md#8-the-integration-real-test-suite)

### Scope

**In scope:**
- New test project `Tracer.Tests.Integration.Real.csproj` added to the solution.
- `[RealIntegrationTest]` category attribute and `[SkipIfNoSimulationHarness]` custom skip attribute so tests are skipped (not failed) when the customer's simulation harness is unavailable.
- `SimulationHarnessFixture`: starts/stops the customer's simulation harness process (executable path from environment variable `TRACER_HARNESS_PATH`); exposes `EmitKnownTraceAsync(traceId, depth)` to inject deterministic trace chains; exposes `EmitEventBurstAsync(count, ratePerSec)` for throughput tests.
- `DdsRoundTripTests` (§8.2 — trace context): start harness; emit 1000 events with known `trace_id` chain; capture via DDS adapter; rotate interval; build bundle; assert all events present in bundle with correct `TraceId`, `EventId`, `ParentEventId`.
- `SharedMemoryThroughputTests` (§8.2 — throughput): 5000 events/sec for 60 s; assert < 0.1% drop rate and agent CPU below 50%.
- `SharedMemoryLossTests` (§8.2 — drop under stall): pause consumer; saturate ring; resume; assert `dropped_count` matches observed deficit; assert producer never blocked.
- `SyncUploadTests` (§8.2 — upload happy path and retry): complete an interval; call `SubmitAsync`; poll until `Completed`; assert NAS zip exists and contains `_ready`.
- `TraceContextPropagationTests` (§8.3): known parent-child-grandchild trace chain; assert Phase 6 causal tree endpoint returns the expected tree shape (see §8.3 code sample for assertion detail).
- `EndToEndSessionTests` (§8.2 — full pipeline): 5-minute simulated session across multiple agent processes; assert bundle contains events from all agents; assert cross-node receive times present; assert Phase 9 Replication Latency view query returns non-trivial p99 values.
- CI lane documentation: `README-integration-real.md` in the test project explaining how to run the suite, required environment variables, and the fact that failures block releases but not PR merges (§8.4).

**Out of scope:**
- Soak tests (TRC-P11-009).
- Modifying the simulation harness or sync master — those belong to the respective teams.
- Running these tests on every PR (they run nightly or on demand per §8.4).

### Constraints

- The test project must compile without the simulation harness being present.
- All test methods that require external infrastructure must be decorated with `[SkipIfNoSimulationHarness]` (or equivalent) so `dotnet test` on a standard dev machine does not fail.
- The existing `Tracer.Tests.Integration` test project is not modified.
- `SimulationHarnessFixture` must implement `IAsyncLifetime` (xUnit pattern) or equivalent so setup/teardown is async.

### Success Conditions

1. **Test: `Tracer.Tests.Integration.Real` compiles on a machine without the simulation harness** — `dotnet build` succeeds; `dotnet test` shows all real-integration tests as skipped, not failed.
2. **Test: `[SkipIfNoSimulationHarness]` skips when env var absent** — `TRACER_HARNESS_PATH` not set; assert all decorated tests show as `Skipped`.
3. **Test: `DdsRoundTripTests.KnownTraceChainArrivesInBundle`** — (Integration-real lane) Requires harness: emit 1000-event chain; assert bundle events ≥ 1000; assert all have non-zero `TraceId`; assert `EventId` values match emitted values exactly.
4. **Test: `TraceContextPropagationTests.ParentChildRelationshipsPreserved`** — (Integration-real lane) Emit depth-3 chain; assert Phase 6 causal tree API returns 3 nodes and 2 edges; assert root node `EventId` = 100 (hex `0x64`).
5. **Test: `SharedMemoryThroughputTests.SustainedThroughput`** — (Integration-real lane) 5000 events/sec × 60 s; assert `dropped_count / total_published < 0.001`.
6. **Test: `SyncUploadTests.HappyPathUploadCompletes`** — (Integration-real lane) NAS zip exists at expected path after `WaitForCompletionAsync` returns `Completed`.
7. **Test: `EndToEndSessionTests.BundleContainsAllAgentData`** — (Integration-real lane) Bundle `events` count ≥ total events emitted across all agents; all agents' `nodeId` present in bundle metadata.

---

## TRC-P11-009 — Soak Test and Final Validation

**Phase:** 11 — Real Adapter Integration  
**Design reference:** [tracer_phase11_design.md §8.3](./tracer_phase11_design.md#83-soak-tests) and [§1.3](./tracer_phase11_design.md#13-success-criteria) (success criteria) and [§11](./tracer_phase11_design.md#11-phase-11-risks-and-mitigations) (risks)

### Scope

**In scope:**
- `SoakTests.cs` in `Tracer.Tests.Integration.Real`: 48-hour continuous run test decorated with `[SoakTest]` category attribute (separate from `[RealIntegrationTest]` so the nightly CI lane can skip soak unless explicitly scheduled).
- Soak run validates (§8.3, §1.3 criteria 8 and 10):
  - No monotonic RSS growth in agent process over 48 h (sampled every 5 min; slope test via linear regression over the last 12 h of samples).
  - No monotonic file-handle growth (sampled every 5 min; same slope test).
  - SharedMemory `dropped_count` per-hour average stays bounded (does not grow run-over-run by more than 5%).
  - Agent throughput (events/s) stable within 10% of the first-hour baseline for all subsequent hours.
  - Agent crash-and-restart (induced mid-run at hour 24): restart completes within 30 s; new interval starts cleanly; no bundle corruption.
  - Bundle build succeeds at any time during the run (triggered at hours 12, 24, 36, and at end of run).
- **Handoff notes** (`docs/phase11-handoff-notes.md`): document what Tracer requires from the simulation team (trace context propagation discipline: `dds_write_ts()` called on every publish; all event IDL types carry `traceId`/`eventId`/`parentEventId`; DDS domain ID agreed before deployment) and from the sync team (Telemetry category REST endpoints match `sync_addendum_telemetry.md §A4` contract; `_ready` entry written last in each zip by the sync agent).
- **Phase 11 completion checklist**: all 10 success criteria from [tracer_phase11_design.md §1.3](./tracer_phase11_design.md#13-success-criteria) verified and signed off; all Phase 1–10 tests still passing.

**Out of scope:**
- Operational deployment automation.
- Alerting integration (§1.2).
- Multi-NAS or multi-master topologies.

### Constraints

- `SoakTests.cs` must be runnable via `dotnet test --filter Category=SoakTest` in a dedicated environment; it must not run accidentally on a dev machine.
- Resource measurements use .NET `Process.GetCurrentProcess()` or OS APIs; not platform-specific native calls (for portability).
- The handoff notes document is in Markdown and committed to the `docs/` folder.

### Success Conditions

1. **Test: `SoakTests.cs` compiles and is correctly categorized** — `dotnet test --filter "Category!=SoakTest"` on the integration-real project runs without the soak test; `dotnet test --filter "Category=SoakTest"` shows it.
2. **Soak run criterion — no RSS growth** — Linear regression slope of agent RSS samples over the final 12 h of the 48-h run is < 1 MB/h.
3. **Soak run criterion — no file-handle growth** — Linear regression slope of agent file handles over final 12 h is < 1 handle/h.
4. **Soak run criterion — drop rate stable** — Per-hour `dropped_count` delta does not exceed first-hour delta by more than 5%.
5. **Soak run criterion — throughput stable** — Events/s in each hour is within ±10% of the first-hour baseline.
6. **Soak run criterion — crash recovery** — After induced crash at hour 24, agent restarts within 30 s; subsequent intervals recorded cleanly; no bundle corruption in the post-crash bundle build.
7. **Soak run criterion — bundle builds succeed** — All four mid-run bundle builds complete without error and produce valid bundles (bundle `Validate()` passes per Phase 5 contract).
8. **Handoff notes completeness** — `docs/phase11-handoff-notes.md` exists and covers: simulation team requirements (trace context discipline, DDS timestamp, domain ID), sync team requirements (contract endpoint stability, `_ready` sentinel discipline).
9. **All Phase 1–10 integration tests pass** — `dotnet test Tracer.Tests.Integration` with `DOTNET_ENVIRONMENT=Development` shows all previously-passing tests green.
10. **Phase 11 success criteria signed off** — All 10 criteria from [tracer_phase11_design.md §1.3](./tracer_phase11_design.md#13-success-criteria) are verifiable by running the integration-real suite plus reviewing the soak results.

<!-- PHASE 11 TASKS END -->
