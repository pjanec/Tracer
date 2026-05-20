# BATCH-06: Phase 3 Foundation — Observer Assembly + WebApi Infrastructure

**Batch Number:** BATCH-06  
**Tasks:** TRC-P3-001, TRC-P3-002  
**Phase:** Phase 3 — TracerObserver, Web API, Vue SPA, Session Browser & Scenario View  
**Estimated Effort:** 16–18 hours  
**Priority:** HIGH  
**Dependencies:** BATCH-05 (all Phase 2 complete)

---

## 📋 Onboarding & Workflow

### Developer Instructions

This batch opens Phase 3 by creating the `Tracer.Observer` and `Tracer.WebApi` assemblies — the server-side backbone for the first user-facing features. You are building the central observer process (ingestion pipeline, read-only connection pool, state reporting) and the ASP.NET Core web application infrastructure (exception middleware, health endpoint, OpenAPI configuration, CORS).

No endpoints beyond `/api/health` are implemented in this batch. Endpoints are added in BATCH-07 (TRC-P3-003, TRC-P3-004, TRC-P3-005). The integration test classes created here will be **skeleton stubs only** — full implementations are deferred to BATCH-09 once endpoints exist.

### Required Reading (IN ORDER)

1. **Workflow:** `.github/skills/developer/SKILL.md` — batch workflow rules
2. **Task Definitions:** `docs/TASK-DETAIL.md` — see `TRC-P3-001` and `TRC-P3-002`
3. **Phase 3 Design:** `docs/tracer_phase3_design.md` — read the entire document before starting
4. **Phase 2 Precedents:** `docs/tracer_phase2_design.md §5` and `§6` — Observer reuses Phase 2 `IntervalRotator`, `ManifestWriter`, `RetentionManager`
5. **Previous Review:** `.dev/tracer/reviews/BATCH-05-REVIEW.md`

### Source Code Location

- **New assemblies:** `src/Tracer.Observer/`, `src/Tracer.WebApi/`
- **TestHarness additions:** `src/Tracer.TestHarness/`
- **Unit tests:** `tests/Tracer.Tests.Unit/Observer/`
- **Integration stubs:** `tests/Tracer.Tests.Integration/`

### Run Tests

```powershell
dotnet test Tracer.sln --configuration Release
```

### Report Submission

`.dev/tracer/reports/BATCH-06-REPORT.md`

Questions: `.dev/tracer/questions/BATCH-06-QUESTIONS.md`

---

## ⚡ MANDATORY: No Stopping

You MUST complete all tasks, run all tests, and fix every failure before writing the report. Do not ask for permission to run tests, fix build errors, or address failing assertions — just do it. The batch is done only when `dotnet test Tracer.sln --configuration Release` exits with code 0 and all new tests pass. Then write the report.

---

## 🔄 MANDATORY WORKFLOW

1. Add new packages to `Directory.Packages.props` first (build infrastructure)
2. Create `Tracer.WebApi.csproj` + skeleton (it has no dependencies other than Core/DuckDB)
3. Create `Tracer.Observer.csproj` + all types (depends on WebApi)
4. Add to `Tracer.sln` — both new projects
5. Add `ObserverFixture` + `WebApiFixture` to `Tracer.TestHarness`
6. Write unit tests → run → fix until green
7. Add skeleton integration test stubs → run → confirm all pass
8. Final `dotnet test Tracer.sln --configuration Release` → 0 failures → write report

---

## Context

Phase 2 built the `TracerAgent`: a per-node process that writes diagnostic data to DuckDB intervals and uploads them. Phase 3 adds the **central observer** — a single process that subscribes to live data sources, ingests records into its own DuckDB intervals, and serves an HTTP API + Vue SPA to humans. The Observer is architecturally symmetric to the Agent: it reuses `IntervalRotator`, `ManifestWriter`, `RetentionManager`, and `StartupRecoveryService` from Phase 2. The differences are (1) it reads from `IDiagnosticDataSource` directly (not `IAgentTransport`), (2) every written `EventRecord` is broadcast to live SSE subscribers, and (3) it hosts an ASP.NET Core web server.

Design rationale: **architecture §12** and **design §3.1**.

---

## ✅ Task 1: TRC-P3-001 — `Tracer.Observer` Assembly

**Full task definition:** `docs/TASK-DETAIL.md#trc-p3-001--tracerobserver-assembly`  
**Design reference:** `docs/tracer_phase3_design.md §2` (project layout), `§3` (process lifecycle, §3.1–§3.11)

### 1.1 NuGet Package Additions

Add to `Directory.Packages.props`:

```xml
<PackageVersion Include="Microsoft.AspNetCore.OpenApi" Version="8.0.0" />
<PackageVersion Include="NSwag.AspNetCore" Version="14.0.7" />
<PackageVersion Include="NSwag.MSBuild" Version="14.0.7" />
<PackageVersion Include="Serilog.AspNetCore" Version="8.0.2" />
<PackageVersion Include="Microsoft.AspNetCore.Mvc.Testing" Version="8.0.0" />
```

### 1.2 `src/Tracer.Observer/Tracer.Observer.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <AssemblyName>tracer-observer</AssemblyName>
    <TargetFramework>net8.0</TargetFramework>
    <RootNamespace>Tracer.Observer</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Tracer.Core\Tracer.Core.csproj" />
    <ProjectReference Include="..\Tracer.Storage.DuckDB\Tracer.Storage.DuckDB.csproj" />
    <ProjectReference Include="..\Tracer.Adapters.Mock\Tracer.Adapters.Mock.csproj" />
    <ProjectReference Include="..\Tracer.Agent\Tracer.Agent.csproj" />
    <ProjectReference Include="..\Tracer.WebApi\Tracer.WebApi.csproj" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Hosting.WindowsServices" />
    <PackageReference Include="Serilog.AspNetCore" />
    <PackageReference Include="Serilog.Sinks.File" />
    <PackageReference Include="Serilog.Sinks.Console" />
    <PackageReference Include="Serilog.Formatting.Compact" />
  </ItemGroup>
</Project>
```

### 1.3 Types to Implement

Follow the design in `docs/tracer_phase3_design.md` precisely for each type. Key sections:

**`Configuration/ObserverConfig.cs`** — `§3.5`. All fields, nested config classes (`DataSourcesConfig`, `MockSourcesConfig`, `MockSourceEntry`, `LiveStreamingConfig`). `ConfigValidation.Validate(ObserverConfig)` per same pattern as `AgentConfig` validation in Phase 2: throws `InvalidOperationException` for null/whitespace `DataRoot`/`LogsRoot`, non-absolute paths, `HttpPort` out of range [1024–65535].

**`Sources/NamedDataSource.cs`** — `§3.8`. The `record NamedDataSource(string Name, IDiagnosticDataSource Source)`. Trivial.

**`Sources/DataSourceComposition.cs`** — `§3.8`. `Build(ObserverConfig)`: throws `InvalidOperationException` for unknown `Kind` or empty Mock sources list. In Phase 3 only `"Mock"` kind is supported.

**`Lifecycle/ReadOnlyConnectionPool.cs`** — `§3.9` (full code listing in design). Implement exactly as shown. Key behavior:
- `InitializeAsync(string dbPath, CancellationToken)` — opens 8 read-only DuckDB connections
- `AcquireAsync(CancellationToken)` — returns `PooledConnection`; throws `ObjectDisposedException` after dispose
- `OnIntervalRotatedAsync(string newPath, CancellationToken)` — drains old pool, opens new pool
- `PooledConnection.DisposeAsync` — returns to pool if pool hasn't rotated; disposes otherwise
- `DisposeAsync` — drains and disposes all connections

**`Lifecycle/ObserverStateReporter.cs`** — `§3.10`. Thread-safe counters. `RollingCounter` uses sliding-window time buckets (e.g., a circular array of `(bucket_index, count)` bucketed by second within the window). `IncrementIngested` updates `_ingestedTotal`, `_ingestedLastMinute` rolling counter, and `_lastEventAt`. `Snapshot()` returns a value-type snapshot of all counters.

**`Lifecycle/ObserverIngestionPipeline.cs`** — `§3.7` (full code listing in design). Implement exactly as shown. `RunAsync` fans out to one `Task` per source via `Task.WhenAll`. `ProcessOneAsync` routes `EventRecord` to writer + broadcasts, `StateSampleRecord.Slow` to state writer (no broadcast), `StateSampleRecord.Fast` to fast-state writer. Write failures increment dropped counter and continue (no rethrow).

**`Lifecycle/ObserverHostedService.cs`** — `§3.11`. Sequence:
1. `StartupRecoveryService.RecoverAsync`
2. `IntervalRotator.OpenCurrentAsync`
3. `ReadOnlyConnectionPool.InitializeAsync` (pass `IntervalRotator.CurrentDirectory.EventsDbPath`)
4. Fire-and-forget: ingestion loop, rotation loop, retention loop
5. On cancellation: await loops, call `IntervalRotator.RotateAsync(GracefulShutdown, CancellationToken.None)` before returning

The rotation loop: periodically (when `TimeUntilNextBoundary` elapses) calls `IntervalRotator.RotateAsync(ScheduledRotation, ct)`, then notifies pool via `ReadOnlyConnectionPool.OnIntervalRotatedAsync(newDbPath, ct)`.

**`Program.cs`** — mirrors `Tracer.Agent/Program.cs`:
- First stdout line: `LOG_FILE=<path>`
- `ObserverHostBuilder.Build(args)` → `await app.RunAsync()`
- Exit code 0 on clean shutdown, 1 on unhandled exception

**`ObserverHostBuilder.cs`** — `§3.4`. The complete WebApplication configuration. See the detailed code listing in `§3.4` of the design. Key registrations: all singletons listed (IClock, ObserverConfig, sources, IntervalScheduler, IntervalRotator, StartupRecoveryService, ManifestWriter, RetentionManager, ObserverStateReporter, ObserverIngestionPipeline, ReadOnlyConnectionPool, LiveEventBroadcaster, SseConnectionManager, query services). `LiveEventBroadcaster` registered as both singleton and hosted service. `ObserverHostedService` registered as hosted service. CORS: `AllowAnyOrigin`/`AllowAnyMethod`/`AllowAnyHeader`.

> **Note on LiveEventBroadcaster, SseConnectionManager, and query services:** These live in `Tracer.WebApi` (added in Task 2 below). Register them from `ObserverHostBuilder` via the `Tracer.WebApi` types. In this batch they are stubs — `LiveEventBroadcaster` and `SseConnectionManager` must exist as compilable types even if not fully implemented; query services can be empty-body stubs returning empty results.

### 1.4 TestHarness Additions

**`src/Tracer.TestHarness/Observer/ObserverFixture.cs`**

`sealed`, implements `IAsyncDisposable`. This fixture is the counterpart to `TracerAgentFixture` for the Observer.

```csharp
public sealed class ObserverFixture : IAsyncDisposable
{
    // Private constructor; use CreateAsync factory
    public static Task<ObserverFixture> CreateAsync(ObserverFixtureOptions? options = null, CancellationToken ct = default);
    
    // Exposes the WebApplication host for WebApplicationFactory-style HTTP testing
    public WebApplication App { get; }
    public string BaseUrl { get; }  // e.g. "http://localhost:{port}"
    
    // Push records directly to the observer's IntervalRotator.CurrentWriter (bypasses SSE broadcast)
    // Use for integration tests that need known data in DuckDB without SSE overhead
    public Task PushAsync(IEnumerable<EventRecord> records, CancellationToken ct = default);
    public Task PushAsync(DiagnosticRecord record, CancellationToken ct = default);
    
    // Force a rotation (for rotation integration tests)
    public Task ForceRotationAsync(CancellationToken ct = default);
    
    public SimulatedClock? SimulatedClock { get; }
    public string DataRoot { get; }
    
    public ValueTask DisposeAsync();
}

public sealed record ObserverFixtureOptions
{
    public bool UseSimulatedClock { get; init; } = false;
    public TimeSpan IntervalDuration { get; init; } = TimeSpan.FromMinutes(1);
    public int HttpPort { get; init; } = 0;  // 0 = auto-assign
}
```

**`src/Tracer.TestHarness/Observer/WebApiFixture.cs`**

A lightweight fixture that hosts only `Tracer.WebApi` via `WebApplicationFactory<Program>` for unit-level API tests (without the full observer host). Since `Tracer.WebApi` may not have its own `Program.cs` entry point in Phase 3 (it's hosted by Observer), this fixture can use `WebApplicationFactory` with the Observer's `Program` type. Implement as a simple wrapper that creates a `WebApplicationFactory`-based `HttpClient`:

```csharp
public sealed class WebApiFixture : IAsyncDisposable
{
    public static Task<WebApiFixture> CreateAsync(CancellationToken ct = default);
    public HttpClient CreateClient();  // Returns client pointed at the test server
    public ValueTask DisposeAsync();
}
```

**`src/Tracer.TestHarness/Tracer.TestHarness.csproj`** — Add:
```xml
<ProjectReference Include="..\Tracer.Observer\Tracer.Observer.csproj" />
<PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" />
```

### 1.5 Unit Tests

Create these test classes in `tests/Tracer.Tests.Unit/Observer/`. Each test class tests one component in isolation using real types where feasible and minimal fakes.

**`ObserverIngestionTests.cs`** — Test `ObserverIngestionPipeline` behavior. Use a fake `IDiagnosticDataSource` that returns a known sequence of records. Use a real `IntervalRotator` instance backed by a temp directory. Required test methods (per TRC-P3-001 SC10):
- `Records_WrittenToCurrentWriter` — produces 10 `EventRecord`s from the source; after `RunAsync` completes, reads the DuckDB and asserts count == 10
- `Events_PublishedToLiveBroadcaster` — produces 3 `EventRecord`s; `LiveEventBroadcaster.LastPublishedCount` (or a counting wrapper) is 3
- `SlowState_WrittenButNotBroadcast` — produces 2 `StateSampleRecord(Slow)` items; count in slow_state DuckDB == 2; broadcast count == 0
- `FastState_WrittenViaAppendFastStateAsync` — produces 1 fast-state `StateSampleRecord`; pipeline routes it to `AppendFastStateAsync` without throwing
- `Cancellation_PropagatesCleanly` — cancel via token during iteration; `RunAsync` returns without throwing `OperationCanceledException` (it catches it)
- `WriteFailure_IncrementsDropCounter_PipelineContinues` — source produces 3 records; first write throws; pipeline processes remaining 2; `StateReporter.Snapshot().DroppedTotal == 1`

**`ObserverStateReporterTests.cs`** — Test `ObserverStateReporter` and `RollingCounter`. Required test methods (per TRC-P3-001 SC11):
- `IncrementIngested_UpdatesAllCounters` — `IncrementIngested()` × 5; `Snapshot().IngestedTotal == 5`, `IngestedLastMinute == 5`, `LastEventUtc` is recent
- `IncrementDropped_UpdatesDroppedOnly` — `IncrementDropped()` × 2; `Snapshot().DroppedTotal == 2`; `IngestedTotal == 0`
- `Snapshot_ReflectsCurrentState` — interleaved increments; snapshot contains correct totals
- `RollingCounter_ReturnsZeroAfterWindowElapsed` — create `RollingCounter(TimeSpan.FromMilliseconds(100))`; increment once; advance `SimulatedClock` by 200ms; `Count == 0`
- `RollingCounter_SumsMultipleBucketsWithinWindow` — increment at T=0, T=50ms, T=80ms (all within a 200ms window); `Count == 3`

**`ReadOnlyConnectionPoolTests.cs`** — Required test methods (per TRC-P3-001 SC12). Use a real DuckDB file (temp dir) for the `InitializeAsync` tests:
- `InitializeAsync_OpensConfiguredPoolSize` — initialize with a known DuckDB path; `poolSize` connections are available (verify by acquiring all 8 consecutively)
- `AcquireAsync_ReturnsConnection` — after init, `AcquireAsync` returns a non-null `PooledConnection` with a non-null `Connection`
- `PooledConnection_DisposeAsync_ReturnsToPool` — acquire one connection, dispose it; can acquire again immediately (pool restored)
- `OnIntervalRotated_BorrowedConnectionDisposesOnReturn` — acquire a connection (hold it), rotate pool to a new path, return (dispose) the held connection; it disposes rather than returns to pool (pool remains at full size for new path)
- `DisposeAsync_ClosesAllConnections` — dispose pool; subsequent `AcquireAsync` throws `ObjectDisposedException`
- `AcquireAsync_AfterDispose_ThrowsObjectDisposedException` — same as above but isolated assertion

**`ObserverHostedServiceTests.cs`** — Test the orchestration logic of `ObserverHostedService`. Use fakes/mocks for `StartupRecoveryService`, `IntervalRotator`, `ReadOnlyConnectionPool`. Required test methods (per TRC-P3-001 SC13):
- `OnStart_RecoveryRunsBeforeIntervalOpen` — verify `RecoverAsync` is called before `OpenCurrentAsync` by tracking call order via recording fakes
- `OnStart_PoolInitializedAfterIntervalOpen` — verify `ReadOnlyConnectionPool.InitializeAsync` called after `OpenCurrentAsync`
- `OnRotation_PoolRefreshedToNewDbPath` — simulate a rotation event; `pool.OnIntervalRotatedAsync` called with the new DB path
- `OnGracefulShutdown_FinalRotationHasGracefulReason` — cancel the host; the final `RotateAsync` call has `ManifestFinalizationReason.GracefulShutdown`
- `PoolRefreshFailure_Logged_HostNotCrashed` — pool refresh throws; host logs the error and does not propagate; hosted service continues

### 1.6 Integration Test Stubs

Create these files with **all test methods as skipped stubs** using `[Fact(Skip = "Deferred to TRC-P3-009 — requires endpoints not yet implemented")]`:

**`tests/Tracer.Tests.Integration/ObserverFakeNodeEndToEndTests.cs`**
```csharp
public class ObserverFakeNodeEndToEndTests : IAsyncLifetime
{
    // IAsyncLifetime stubs — just Task.CompletedTask
    [Fact(Skip = "Deferred to TRC-P3-009")] public Task GetSessions_ReturnsActiveSession() => Task.CompletedTask;
    [Fact(Skip = "Deferred to TRC-P3-009")] public Task GetScenarioNotables_ReturnsNotablesFromScenario() => Task.CompletedTask;
    [Fact(Skip = "Deferred to TRC-P3-009")] public Task GetScenarioPhases_ReturnsActivePhaseName() => Task.CompletedTask;
}
```

**`tests/Tracer.Tests.Integration/ObserverRotationIntegrationTests.cs`**
```csharp
public class ObserverRotationIntegrationTests
{
    [Fact(Skip = "Deferred to TRC-P3-009")] public Task FirstInterval_FinalizedWithReady_AfterRotation() => Task.CompletedTask;
    [Fact(Skip = "Deferred to TRC-P3-009")] public Task SecondInterval_QueriesReturnCurrentIntervalEvents() => Task.CompletedTask;
    [Fact(Skip = "Deferred to TRC-P3-009")] public Task Queries_DuringRotation_SucceedAfterBriefBlock() => Task.CompletedTask;
}
```

---

## ✅ Task 2: TRC-P3-002 — `Tracer.WebApi` Project Setup and Cross-Cutting Middleware

**Full task definition:** `docs/TASK-DETAIL.md#trc-p3-002--tracerwebapi-project-setup-and-cross-cutting-middleware`  
**Design reference:** `docs/tracer_phase3_design.md §2` (project layout), `§4.1` (endpoint set), `§4.5` (error handling)

### 2.1 `src/Tracer.WebApi/Tracer.WebApi.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <RootNamespace>Tracer.WebApi</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
    <ProjectReference Include="..\Tracer.Core\Tracer.Core.csproj" />
    <ProjectReference Include="..\Tracer.Storage.DuckDB\Tracer.Storage.DuckDB.csproj" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="NSwag.AspNetCore" />
    <PackageReference Include="Microsoft.AspNetCore.OpenApi" />
  </ItemGroup>
  <!-- NSwag TypeScript client generation — Debug mode only -->
  <Target Name="GenerateTypeScriptClient" AfterTargets="Build" Condition="'$(Configuration)' == 'Debug'">
    <Exec Command="dotnet tool run nswag run nswag.json /runtime:Net80"
          WorkingDirectory="$(MSBuildProjectDirectory)"
          ContinueOnError="true" />
  </Target>
</Project>
```

The `nswag.json` file in `src/Tracer.WebApi/` should target output `$(MSBuildProjectDirectory)/../../tracer-viewer/src/api/tracerApiClient.ts` (the frontend doesn't exist yet, so `ContinueOnError="true"` prevents build failure).

### 2.2 Types to Implement

**`Errors/ApiExceptionMiddleware.cs`**

Implements `IMiddleware` (or use a request delegate). On exception:
- `ArgumentException` → HTTP 400, `application/problem+json`, `detail` = exception message
- Anything else → HTTP 500, `application/problem+json`, `detail` = `"An unexpected error occurred"`
- **Never include stack trace text in the response body**

```csharp
public static class ApiExceptionMiddleware
{
    public static async Task HandleAsync(HttpContext context)
    {
        var ex = context.Features.Get<IExceptionHandlerFeature>()?.Error;
        if (ex is null) return;
        
        var (status, detail) = ex switch
        {
            ArgumentException ae => (400, ae.Message),
            _ => (500, "An unexpected error occurred")
        };
        
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(new { status, detail, title = status == 400 ? "Bad Request" : "Internal Server Error" });
    }
}
```

**`Errors/ProblemDetailsFactory.cs`**

```csharp
public static class ProblemDetailsFactory
{
    public static ProblemDetails From(Exception? ex) => ex switch
    {
        ArgumentException ae => new ProblemDetails { Status = 400, Detail = ae.Message },
        TracerStorageException tse => new ProblemDetails { Status = 500, Detail = tse.Message },
        _ => new ProblemDetails { Status = 500, Detail = "An unexpected error occurred" }
    };
}
```

**`Endpoints/HealthEndpoints.cs`**

```csharp
public static class HealthEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }))
           .WithName("GetHealth")
           .WithOpenApi();
    }
}
```

**`OpenApi/OpenApiConfiguration.cs`**

```csharp
public static class OpenApiConfiguration
{
    public static void Configure(WebApplicationBuilder builder)
    {
        builder.Services.AddOpenApi(options =>
        {
            options.AddDocumentTransformer((doc, ctx, ct) =>
            {
                doc.Info.Title = "Tracer Observer API";
                doc.Info.Version = "v1";
                return Task.CompletedTask;
            });
        });
        builder.Services.AddEndpointsApiExplorer();
    }
}
```

**Stub types required by `ObserverHostBuilder` (Phase 3 stubs — will be implemented in BATCH-07):**

Create these in `Tracer.WebApi` as compilation stubs with empty/minimal bodies. They must compile and be registerable in DI; their behavior will be completed in BATCH-07.

- `Streaming/LiveEventBroadcaster.cs` — `sealed class LiveEventBroadcaster : BackgroundService` with `Publish(EventRecord ev)` method (no-op in stub), `protected override Task ExecuteAsync(CancellationToken ct) => Task.CompletedTask`
- `Streaming/SseConnectionManager.cs` — `sealed class SseConnectionManager` with `int ActiveCount => 0`
- `Streaming/SseFilter.cs` — `sealed record SseFilter(bool NotablesOnly = false, string? SessionId = null)`
- `Queries/SessionQueryService.cs` — `sealed class SessionQueryService(ReadOnlyConnectionPool pool)` — stub, returns empty collections
- `Queries/ScenarioQueryService.cs` — same pattern
- `Queries/TopologyQueryService.cs` — same pattern
- `Queries/EventLookupService.cs` — same pattern
- `Contracts/Dto/` — create all DTO classes as empty shells: `SessionDto.cs`, `EventDto.cs`, `NotableEventDto.cs`, `ScenarioPhaseDto.cs`, `TopologyDto.cs`, `LiveStatusDto.cs`, `NodeInfoDto.cs`, `ScenarioStateDto.cs`
- `Contracts/Mapping/DtoMappers.cs` — `public static class DtoMappers` with no methods (to be filled in BATCH-07)
- `Endpoints/SessionEndpoints.cs`, `EventEndpoints.cs`, `ScenarioEndpoints.cs`, `TopologyEndpoints.cs`, `SseEndpoints.cs` — each with a `public static void Map(WebApplication app) { }` no-op stub

> **Important:** These stubs must compile cleanly and be registerable by `ObserverHostBuilder`. The only active endpoint in this batch is `GET /api/health`.

### 2.3 Unit Tests

Add `Tracer.Tests.Unit.csproj` reference to `Tracer.WebApi` if not already present.

**`tests/Tracer.Tests.Unit/WebApi/` — create this directory.**

Since no substantive behavior is implemented in BATCH-06's WebApi beyond the error middleware and health endpoint, only the following unit tests are needed for TRC-P3-002:

**`tests/Tracer.Tests.Unit/WebApi/HealthEndpointTests.cs`**

Using `WebApplicationFactory` or `Microsoft.AspNetCore.TestHost` to host the observer:
- `GetHealth_Returns200_WithOkStatus` — `GET /api/health` → 200 + `{"status":"ok"}`
- `GetHealth_DoesNotRequireDuckDb` — health endpoint responds even when no DuckDB file is present

**`tests/Tracer.Tests.Unit/WebApi/ProblemDetailsFactoryTests.cs`**

Pure unit tests, no HTTP:
- `ArgumentException_Returns400` — `ProblemDetailsFactory.From(new ArgumentException("x")).Status == 400`
- `TracerStorageException_Returns500` — status 500
- `NullException_Returns500` — `From(null).Status == 500`
- `ArgumentException_DetailContainsMessage` — `detail` field contains the exception message

---

## 🧪 Testing Requirements

**Minimum test additions:**
- 4 unit test classes in `Tracer.Tests.Unit/Observer/` with total ≥ 22 test methods
- 2 unit test classes in `Tracer.Tests.Unit/WebApi/` with total ≥ 6 test methods
- 2 skeleton integration test classes with 3 skipped stubs each

**Quality standards:**
- Each Observer unit test must assert on **actual behavior**: records written to DuckDB, pool connections acquired/returned, call ordering in hosted service. No assertion-free or trivially-passing tests.
- `ReadOnlyConnectionPool` tests must use a real (temp) DuckDB file — a mock connection would not test the pooling semantics.
- `ObserverIngestionPipeline.WriteFailure_IncrementsDropCounter_PipelineContinues` must verify that the pipeline processes the subsequent records (not just that no exception was thrown).
- All Phase 1 and Phase 2 tests must still pass.

---

## ⚠️ Important Notes

### Observer Depends on Agent Types
`Tracer.Observer.csproj` references `Tracer.Agent` (for `IntervalRotator`, `ManifestWriter`, `StartupRecoveryService`, `RetentionManager`, `UploadIntentDispatcher`). This is correct per the design dependency graph (`docs/tracer_phase3_design.md §2.1`).

### RollingCounter Implementation
A simple but correct `RollingCounter` uses an array of `(int bucketIndex, int count)` structures, one bucket per time quantum (e.g., per second). On each `Increment`, compute `currentBucket = (now.Ticks / bucketTicks) % bucketCount`, clear buckets older than the window, and increment the current bucket. `Count` sums all non-expired buckets. The tests using `SimulatedClock` require the `RollingCounter` to accept an `IClock` for testability.

### `ObserverFixture` Port Selection
When `ObserverFixtureOptions.HttpPort = 0`, use `0` in Kestrel (`options.ListenAnyIP(0)`) which assigns a random available port. After `StartAsync`, read the actual port via `app.Urls`. This avoids port conflicts in parallel test runs.

### Solution File
Add both new projects to `Tracer.sln`:
```powershell
dotnet sln Tracer.sln add src/Tracer.Observer/Tracer.Observer.csproj
dotnet sln Tracer.sln add src/Tracer.WebApi/Tracer.WebApi.csproj
```

### Known Technical Debt (Do Not Fix — Log If You See Issues)
- DT-009: `StartupRecoveryService.TryFinalizeAsync` records `SlowStateCount = 0` regardless of actual count — inherited P2 bug, not in scope here.

---

## 📊 Report Requirements

Submit `.dev/tracer/reports/BATCH-06-REPORT.md` with:

**Q1:** What issues did you encounter during implementation? How did you resolve them?

**Q2:** Were there any design ambiguities or gaps in `tracer_phase3_design.md`? What decisions did you make to fill them?

**Q3:** What design decisions did you make beyond the spec? What alternatives did you consider?

**Q4:** What edge cases did you discover that weren't mentioned in the instructions?

**Q5:** Are there any performance concerns, architectural weak points, or improvement opportunities you noticed?

**Q6:** Suggested commit message for this batch's work.

---

## 🎯 Success Criteria

This batch is DONE when:
- [ ] `Tracer.Observer.csproj` exists and `dotnet build --configuration Release` exits 0 with zero warnings
- [ ] `Tracer.WebApi.csproj` exists and builds cleanly
- [ ] `ObserverFixture` and `WebApiFixture` added to `Tracer.TestHarness`
- [ ] 4 Observer unit test classes with ≥ 22 total tests, all passing
- [ ] 2 WebApi unit test classes with ≥ 6 tests, all passing
- [ ] 2 skeleton integration test classes created with skipped stubs
- [ ] All previous Phase 1 and Phase 2 tests still pass
- [ ] `dotnet test Tracer.sln --configuration Release` exits code 0
- [ ] Report submitted

---

## 📚 Reference Materials

- **Task Defs:** `docs/TASK-DETAIL.md` — see `TRC-P3-001`, `TRC-P3-002`
- **Phase 3 Design:** `docs/tracer_phase3_design.md` — §2 (layout), §3 (Observer), §4 (WebApi), §3.4 (ObserverHostBuilder), §3.5 (ObserverConfig), §3.7 (IngestionPipeline), §3.8 (DataSourceComposition), §3.9 (ReadOnlyConnectionPool), §3.10 (StateReporter), §3.11 (ObserverHostedService)
- **Phase 2 Precedents:** `docs/tracer_phase2_design.md §5–§6` — IntervalRotator, ManifestWriter patterns to reuse
- **Architecture:** `docs/tracer_architecture_v1.md §12` (TracerObserver role), `§14` (WebApi surface)
- **Existing agent for pattern reference:** `src/Tracer.Agent/`
