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
