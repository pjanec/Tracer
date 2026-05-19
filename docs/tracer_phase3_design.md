# Tracer Phase 3 — Detailed Design
## TracerObserver, Web API, Vue SPA Scaffold, Scenario View

*Companion to `tracer_architecture_v1.md`, `tracer_phase1_design.md`, `tracer_phase2_design.md`*
*Phase 3 of the build sequence (architecture §18)*
*C# / .NET 8 backend · Vue 3 / TypeScript frontend · May 2026*

*Phase 3 makes Tracer's data visible to humans for the first time. It introduces the TracerObserver process (subscribing to live data sources during sessions), the ASP.NET Core Web API serving queries and live updates, the Vue 3 SPA scaffold, and the first user-facing view: the Scenario View. The view is designed for the easiest audience first — instructors and non-technical bystanders — to validate that the architecture works end-to-end before engineer-focused complexity is added.*

---

## 1. Phase 3 Scope and Goals

### 1.1 What Phase 3 Delivers

- **`Tracer.Observer`** assembly and **`tracer-observer.exe`** runnable ASP.NET Core host (Windows service or console)
- **`Tracer.WebApi`** assembly providing REST endpoints and SSE streaming
- **`tracer-viewer/`** Vue 3 / TypeScript SPA project
- **Scenario View** — the first user-facing view, instructor-focused dashboard
- **Session Browser** — the entry point view that lists available sessions to pick from
- **Live update streaming via SSE** for both Scenario View and Session Browser
- **End-to-end demo path**: FakeNode produces data → Observer captures it → Web API serves it → Viewer displays scenario flow live
- **Generated TypeScript client** from .NET DTOs (via NSwag or equivalent)
- **Cross-cutting plumbing for the first ASP.NET Core process**: CORS, error handling, OpenAPI documentation, health endpoints
- Tests: API endpoint tests, observer-ingestion tests, frontend component tests, end-to-end browser smoke tests via Playwright

### 1.2 What Phase 3 Does NOT Deliver

- No engineer timeline view (Phase 5)
- No causal tree view (Phase 6)
- No entity history view (Phase 7)
- No bundle export/import (Phase 4)
- No real DDS adapter — observer subscribes to mock data sources only
- No authentication or authorization
- No editing or annotation features (deferred to Phase 8)
- No client-side persistence (bookmarks, saved views) — deferred to Phase 8
- No HTTPS / TLS — runs HTTP only in Phase 3 (security wave is later, out of scope of the platform)

### 1.3 Success Criteria

Phase 3 is complete when all of the following are true:

1. **`tracer-observer.exe` runs** as console or Windows service, subscribes to a mock data source, ingests records into a local DuckDB, serves a web API on a configured port.
2. **`tracer-fakenode.exe` and `tracer-observer.exe` run together** for a full simulated session. Records produced by FakeNode flow into the Observer's DuckDB and become queryable.
3. **The Scenario View renders in a browser** showing the active scenario's phase, notable events stream, and current state. The page loads in under 2 seconds with cold cache; subsequent interactions are sub-second.
4. **Live updates work**: new notable events emitted by FakeNode appear in the Scenario View within 500ms via SSE without manual refresh.
5. **Session Browser shows a list of available sessions** from session-start/session-end events captured. Clicking a session opens the Scenario View for it.
6. **Multiple FakeNode instances feeding one Observer** are handled correctly: events from all nodes appear in the unified view; the Scenario View shows scenario-level events regardless of which node emitted them.
7. **Tests pass**: REST endpoints have correct status codes and response shapes; SSE delivers events in order; Vue components render with mocked API responses; Playwright smoke test loads the Scenario View end-to-end.
8. **Performance targets met** (architecture §17, the ones applicable to Phase 3):
   - Open a session: < 2 seconds to first usable view
   - Apply a filter: < 300ms
   - Click event → show details: < 100ms
   - Sustained ingest while serving: zero drops at FakeNode's default rates
9. **All Phase 1 and Phase 2 tests still pass.**
10. **Browser support**: latest Edge and Chrome on Windows. Firefox compatibility is verified manually but not gated. No IE.

### 1.4 Estimated Duration

Three calendar weeks for one developer with .NET 8 and Vue 3 experience. Add a week if the developer is unfamiliar with one of the two stacks. The work splits roughly:
- Week 1: Observer + Web API skeleton + first endpoints + ingestion
- Week 2: Vue scaffold + Session Browser + initial Scenario View
- Week 3: Live updates + polish + tests + demo workflow

---

## 2. Project Layout Additions

Building on Phase 2:

```
tracer/
  src/
    Tracer.Core/                                  (unchanged)
    Tracer.Storage.DuckDB/                        (unchanged from Phase 2)
    Tracer.Adapters.Mock/                         (unchanged)
    Tracer.Agent/                                 (unchanged)
    Tracer.FakeNode/                              (unchanged)
    Tracer.Observer/                              NEW assembly
      Tracer.Observer.csproj
      Program.cs
      ObserverHostBuilder.cs
      Configuration/
        ObserverConfig.cs
      Lifecycle/
        ObserverHostedService.cs
        ObserverIngestionPipeline.cs              reuses Phase 2 IntervalRotator + writers
        ObserverStateReporter.cs                  observer-side health/stats
      Sources/
        NamedDataSource.cs                        wraps IDiagnosticDataSource with a name (for multi-source observers)
        DataSourceComposition.cs                  builds list of named sources from ObserverConfig
    Tracer.WebApi/                                NEW assembly
      Tracer.WebApi.csproj
      Program.cs                                  optional standalone entrypoint; usually hosted by Observer
      WebApiBuilder.cs                            DI + middleware setup
      Endpoints/
        SessionEndpoints.cs                       /api/sessions/*
        EventEndpoints.cs                         /api/events/{eventId}  (lookup only in Phase 3)
        ScenarioEndpoints.cs                      /api/scenario/*
        TopologyEndpoints.cs                      /api/topology
        HealthEndpoints.cs                        /api/health, /api/live/status
        SseEndpoints.cs                           /api/live/*
      Contracts/
        Dto/
          SessionDto.cs
          EventDto.cs
          NotableEventDto.cs
          ScenarioPhaseDto.cs
          TopologyDto.cs
          LiveStatusDto.cs
        Mapping/
          DtoMappers.cs
      Queries/
        SessionQueryService.cs
        ScenarioQueryService.cs
        TopologyQueryService.cs
        EventLookupService.cs                     single-event-by-id lookups
      Streaming/
        LiveEventBroadcaster.cs                   SSE fanout
        SseConnectionManager.cs
        SseFilter.cs
      OpenApi/
        OpenApiConfiguration.cs
      Errors/
        ApiExceptionMiddleware.cs
        ProblemDetailsFactory.cs
    Tracer.TestHarness/                           (additions)
      ObserverFixture.cs
      WebApiFixture.cs
  tests/
    Tracer.Tests.Unit/
      Observer/
        ObserverIngestionTests.cs
        ObserverStateReporterTests.cs
        ReadOnlyConnectionPoolTests.cs
        ObserverHostedServiceTests.cs
      WebApi/
        EventEndpointTests.cs
        SessionEndpointTests.cs
        ScenarioEndpointTests.cs
        SseEndpointTests.cs
        LiveStatusTests.cs
        DtoMappingTests.cs
    Tracer.Tests.Integration/
      ObserverFakeNodeEndToEndTests.cs
      ObserverRotationIntegrationTests.cs
      WebApiQueryRoundTripTests.cs
      LiveStreamingTests.cs
  tracer-viewer/                                  NEW directory (frontend root)
    package.json
    pnpm-lock.yaml (or package-lock.json)
    vite.config.ts
    tsconfig.json
    tsconfig.app.json
    tsconfig.node.json
    .eslintrc.cjs
    .prettierrc
    playwright.config.ts
    index.html
    src/
      main.ts
      App.vue
      router/
        index.ts
      stores/
        sessionStore.ts
        liveStore.ts
        topologyStore.ts
      views/
        SessionBrowserView.vue
        ScenarioView.vue
      components/
        SessionCard.vue
        ScenarioStatePanel.vue
        PhaseTimeline.vue
        NotableEventsList.vue
        NotableEventCard.vue
        LiveIndicator.vue
        AppHeader.vue
        AppShell.vue
        LoadingSpinner.vue
        ErrorMessage.vue
      api/
        tracerApiClient.ts                        generated from OpenAPI
        sseClient.ts
      composables/
        useLiveSse.ts
        usePagedQuery.ts
        useScenarioState.ts
      types/
        appDomain.ts                              hand-authored types layered on generated DTOs
      styles/
        base.scss
        tokens.scss
      utils/
        time.ts                                   wallclock formatting
        colors.ts                                 per-node color assignment
    public/
      favicon.ico
    tests/
      unit/
        ScenarioStatePanel.spec.ts
        NotableEventsList.spec.ts
        useLiveSse.spec.ts
      e2e/
        scenario-view.spec.ts
        session-browser.spec.ts
```

### 2.1 Updated Dependency Graph

```
Tracer.Core                        (unchanged)
    ↑
Tracer.Storage.DuckDB              (unchanged)
    ↑
Tracer.Adapters.Mock               (unchanged)
    ↑
Tracer.Agent                       (unchanged)
    ↑
Tracer.WebApi                      (deps: Tracer.Core, Tracer.Storage.DuckDB, ASP.NET Core, Swashbuckle/NSwag)
    ↑
Tracer.Observer                    (deps: Tracer.Core, Tracer.Storage.DuckDB, Tracer.WebApi, Tracer.Adapters.Mock, M.E.Hosting)
    ↑
Tracer.FakeNode                    (unchanged dependencies — but now can optionally also host the Observer-equivalent in-process for demos; see §10)

tracer-viewer/  (frontend; no .NET deps; consumes Web API over HTTP/SSE)
```

**New NuGet packages** (added to `Directory.Packages.props`):

```xml
<PackageVersion Include="Microsoft.AspNetCore.OpenApi" Version="8.0.0" />
<PackageVersion Include="NSwag.AspNetCore" Version="14.0.7" />
<PackageVersion Include="NSwag.MSBuild" Version="14.0.7" />
<PackageVersion Include="Microsoft.AspNetCore.Mvc.NewtonsoftJson" Version="8.0.0" />
```

**Note on OpenAPI tooling**: I'm recommending **NSwag** over Swashbuckle for one specific reason: NSwag's MSBuild integration cleanly generates a TypeScript client at build time, which keeps the frontend types and backend DTOs in lockstep without manual sync. Swashbuckle is fine for OpenAPI doc generation but the TypeScript client story is weaker.

**Frontend tooling** (declared in tracer-viewer/package.json):

- **Vue 3.4+** with Composition API
- **Vite 5+** as build tool
- **TypeScript 5.3+**
- **Pinia** for state management
- **Vue Router 4** for routing
- **@microsoft/fetch-event-source** for SSE (more robust than native EventSource for our needs)
- **Vitest** for unit tests
- **Playwright** for E2E
- **ESLint + Prettier** for formatting

---

## 3. The TracerObserver Process

### 3.1 Role and Responsibilities

The observer is a **single central process** running on a designated machine. During a session it:

1. Subscribes to all live data sources (one or more `IAgentTransport`-shaped sources — mock today, DDS later)
2. Persists records to its local DuckDB, applying the same interval rotation pattern as the agent (reusing Phase 2's `IntervalRotator`)
3. Serves an HTTP API for queries against captured data
4. Streams live updates to viewers via SSE
5. Hosts the Vue SPA static assets

Notable architectural distinction from the agent (Phase 2):
- Agent: per-node, durable record, uploads to NAS, primary diagnostic source post-hoc
- Observer: central, real-time convenience, **disposable** — if it crashes, agents on nodes still capture everything

### 3.2 Process Lifecycle

```
┌──────────────────────────────────────────────────────────────┐
│  tracer-observer.exe (console mode or Windows service)       │
│                                                              │
│  ┌──────────────────────────────────────────────────────┐    │
│  │  Generic Host (IHost) + ASP.NET Core Web Host        │    │
│  │                                                      │    │
│  │  Singleton services:                                 │    │
│  │   - IClock (SystemClock prod, SimulatedClock test)   │    │
│  │   - ObserverConfig                                   │    │
│  │   - IReadOnlyList<NamedDataSource> (composed at      │    │
│  │       startup from ObserverConfig)                   │    │
│  │   - IntervalScheduler, IntervalRotator (reused)      │    │
│  │   - ReadOnlyConnectionPool                           │    │
│  │   - LiveEventBroadcaster                             │    │
│  │   - SseConnectionManager                             │    │
│  │   - ObserverStateReporter                            │    │
│  │   - Query services (Session, Scenario, Topology,     │    │
│  │       EventLookup)                                   │    │
│  │                                                      │    │
│  │  Hosted services:                                    │    │
│  │   - ObserverHostedService                            │    │
│  │     ├─ Initialize storage                            │    │
│  │     ├─ Open current interval                         │    │
│  │     ├─ Start ingestion pipeline                      │    │
│  │     └─ Rotation/retention loops                      │    │
│  │   - LiveEventBroadcaster (background fanout)         │    │
│  │                                                      │    │
│  │  Endpoint routes:                                    │    │
│  │   - /api/* (REST)                                    │    │
│  │   - /api/live/* (SSE)                                │    │
│  │   - /*   (Vue SPA static assets)                     │    │
│  └──────────────────────────────────────────────────────┘    │
└──────────────────────────────────────────────────────────────┘
```

### 3.3 Program.cs

```csharp
namespace Tracer.Observer;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            var host = ObserverHostBuilder.Build(args);

            // LOG_FILE convention
            var config = host.Services.GetRequiredService<ObserverConfig>();
            var logFilePath = LoggingPaths.GetCurrentLogFilePath(config.LogsRoot, "tracer-observer");
            Console.WriteLine($"LOG_FILE={logFilePath}");

            await host.RunAsync();
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"FATAL: {ex}");
            return 1;
        }
    }
}
```

### 3.4 ObserverHostBuilder

The observer uses `WebApplicationBuilder` (which builds on `Microsoft.Extensions.Hosting`) because it needs both background services and an HTTP server.

```csharp
namespace Tracer.Observer;

public static class ObserverHostBuilder
{
    public static WebApplication Build(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Configuration
        var configPath = ResolveConfigPath(args);
        builder.Configuration.AddJsonFile(configPath, optional: false);
        builder.Services.Configure<ObserverConfig>(builder.Configuration.GetSection("Observer"));
        builder.Services.AddSingleton<ObserverConfig>(sp =>
        {
            var cfg = sp.GetRequiredService<IOptions<ObserverConfig>>().Value;
            ConfigValidation.Validate(cfg);
            return cfg;
        });

        // Logging (Serilog — same conventions as agent)
        ConfigureSerilog(builder);

        // Kestrel — bind to the configured port, no HTTPS in Phase 3
        builder.WebHost.ConfigureKestrel((ctx, options) =>
        {
            var cfg = ctx.Configuration.GetSection("Observer").Get<ObserverConfig>()!;
            options.ListenAnyIP(cfg.HttpPort);
            options.AddServerHeader = false;
            options.Limits.MaxConcurrentConnections = 1000;
        });

        // Core services
        builder.Services.AddSingleton<IClock, SystemClock>();
        builder.Services.AddSingleton<IReadOnlyDictionary<string, ParquetTopicSchema>>(
            _ => WellKnownTopicSchemas.ToDictionary());

        // Data sources — composed from ObserverConfig at startup
        builder.Services.AddSingleton<IReadOnlyList<NamedDataSource>>(sp =>
            DataSourceComposition.Build(sp.GetRequiredService<ObserverConfig>()));

        // Storage / rotation — reused from agent, with observer-specific storage root
        builder.Services.AddSingleton<IntervalScheduler>();
        builder.Services.AddSingleton<IntervalRotator>();
        builder.Services.AddSingleton<StartupRecoveryService>();
        builder.Services.AddSingleton<ManifestWriter>();
        builder.Services.AddSingleton<RetentionManager>();

        // Observer ingestion + state
        builder.Services.AddSingleton<ObserverStateReporter>();
        builder.Services.AddSingleton<ObserverIngestionPipeline>();

        // Reader pool — for serving HTTP queries (initialized by ObserverHostedService)
        builder.Services.AddSingleton<ReadOnlyConnectionPool>();

        // Query services
        builder.Services.AddSingleton<SessionQueryService>();
        builder.Services.AddSingleton<ScenarioQueryService>();
        builder.Services.AddSingleton<TopologyQueryService>();
        builder.Services.AddSingleton<EventLookupService>();

        // Live streaming
        builder.Services.AddSingleton<SseConnectionManager>();
        builder.Services.AddSingleton<LiveEventBroadcaster>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<LiveEventBroadcaster>());

        // CORS — for development; in production the SPA and API are same-origin
        builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
            .AllowAnyOrigin()
            .AllowAnyMethod()
            .AllowAnyHeader()));

        // OpenAPI + NSwag
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddOpenApiDocument(c =>
        {
            c.Title = "Tracer Web API";
            c.Version = "v1";
        });

        // The main hosted service that ties storage + ingestion together
        builder.Services.AddHostedService<ObserverHostedService>();

        // Windows service support
        builder.Services.AddWindowsService(o => o.ServiceName = "TracerObserver");

        var app = builder.Build();
        ConfigureMiddleware(app);
        return app;
    }

    private static void ConfigureMiddleware(WebApplication app)
    {
        app.UseExceptionHandler(new ExceptionHandlerOptions
        {
            ExceptionHandler = ApiExceptionMiddleware.HandleAsync
        });

        app.UseCors();

        // OpenAPI + Swagger UI (dev mode only)
        if (app.Environment.IsDevelopment())
        {
            app.UseOpenApi();
            app.UseSwaggerUi();
        }

        // API endpoints
        SessionEndpoints.Map(app);
        EventEndpoints.Map(app);
        ScenarioEndpoints.Map(app);
        TopologyEndpoints.Map(app);
        HealthEndpoints.Map(app);
        SseEndpoints.Map(app);

        // SPA static assets (last — fallback to index.html for client-side routing)
        var spaPath = Path.Combine(AppContext.BaseDirectory, "wwwroot");
        if (Directory.Exists(spaPath))
        {
            app.UseDefaultFiles(new DefaultFilesOptions
            {
                FileProvider = new PhysicalFileProvider(spaPath)
            });
            app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = new PhysicalFileProvider(spaPath)
            });
            app.MapFallbackToFile("index.html", new StaticFileOptions
            {
                FileProvider = new PhysicalFileProvider(spaPath)
            });
        }
    }

    private static string ResolveConfigPath(string[] args)
    {
        // Same convention as agent: --config <absolute-path>
        // Fallback: %PROGRAMDATA%\Tracer\observer\config.json
        // ...
        return ""; // detail omitted
    }

    private static void ConfigureSerilog(WebApplicationBuilder builder)
    {
        builder.Services.AddSerilog((sp, lc) =>
        {
            var cfg = sp.GetRequiredService<ObserverConfig>();
            lc.MinimumLevel.Information()
              .MinimumLevel.Override("Tracer", LogEventLevel.Debug)
              .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
              .Enrich.FromLogContext()
              .Enrich.WithProperty("Service", "TracerObserver")
              .WriteTo.File(
                  new CompactJsonFormatter(),
                  Path.Combine(cfg.LogsRoot, "tracer-observer-.json"),
                  rollingInterval: RollingInterval.Day,
                  retainedFileCountLimit: 14);
            if (cfg.LogToConsole)
                lc.WriteTo.Console(new CompactJsonFormatter());
        });
    }
}
```

### 3.5 ObserverConfig

```csharp
namespace Tracer.Observer.Configuration;

public sealed class ObserverConfig
{
    [Required]
    public required string DataRoot { get; set; }       // absolute

    [Required]
    public required string LogsRoot { get; set; }       // absolute

    [Range(1024, 65535)]
    public int HttpPort { get; set; } = 5300;

    public TimeSpan IntervalDuration { get; set; } = TimeSpan.FromHours(1);

    public int KeepLastNIntervals { get; set; } = 4;    // observer is less durable than agents

    public int DiskWatermarkPercent { get; set; } = 10;

    public bool LogToConsole { get; set; } = false;

    [Required]
    public required DataSourcesConfig DataSources { get; set; }

    public LiveStreamingConfig LiveStreaming { get; set; } = new();
}

public sealed class DataSourcesConfig
{
    /// <summary>"Mock" or "Dds" (Phase 11)</summary>
    public string Kind { get; set; } = "Mock";

    public MockSourcesConfig? Mock { get; set; }
}

public sealed class MockSourcesConfig
{
    public IList<MockSourceEntry> Sources { get; set; } = new List<MockSourceEntry>();
}

public sealed class MockSourceEntry
{
    public required string Name { get; set; }              // logical name for logs
    public required string ScenarioName { get; set; }
    public ScenarioConfig ScenarioConfig { get; set; } = new();
}

public sealed class LiveStreamingConfig
{
    public int MaxConcurrentSseClients { get; set; } = 50;
    public int PerClientBufferSize { get; set; } = 1000;
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(15);
}
```

### 3.6 Example observer.json

```json
{
  "Observer": {
    "DataRoot": "C:/ProgramData/Tracer/observer",
    "LogsRoot": "C:/ProgramData/Tracer/observer/logs",
    "HttpPort": 5300,
    "IntervalDuration": "01:00:00",
    "KeepLastNIntervals": 4,
    "LogToConsole": true,
    "DataSources": {
      "Kind": "Mock",
      "Mock": {
        "Sources": [
          {
            "Name": "fakenode-cluster",
            "ScenarioName": "CombatEngagement",
            "ScenarioConfig": {
              "Duration": "00:30:00",
              "NodeCount": 4,
              "EntityCount": 20,
              "EventsPerSecond": 200,
              "Seed": 42
            }
          }
        ]
      }
    }
  }
}
```

For development/demo: observer self-drives a mock scenario internally. For integration with FakeNode processes (multi-process testing), see §10 — the observer can be configured to subscribe to a TCP-bridged transport from one or more FakeNode instances.

### 3.7 ObserverIngestionPipeline

The observer's ingestion is conceptually similar to the agent's (Phase 2 §6.4) with two differences:

1. **It uses `IDiagnosticDataSource`** (Phase 1, §3.5) directly rather than `IAgentTransport`. The Observer subscribes to data sources; it doesn't read from a producer-fed transport. This is the symmetric counterpart to how the Agent uses `IAgentTransport`.
2. **Every event written is also broadcast to live SSE subscribers**.

The Observer does not implement backpressure-driven dropping (the Agent's Phase 2 §6.5 escalation policy). Rationale: the Observer is disposable. If it can't keep up, the appropriate response is to log loudly and let the connection-pool / channel buffers naturally throttle. Per-node Agents on each node are the durable record; data dropped by the Observer is not lost.

```csharp
namespace Tracer.Observer.Lifecycle;

public sealed class ObserverIngestionPipeline
{
    private readonly IReadOnlyList<NamedDataSource> _sources;
    private readonly IntervalRotator _rotator;
    private readonly LiveEventBroadcaster _broadcaster;
    private readonly ObserverStateReporter _state;
    private readonly ILogger<ObserverIngestionPipeline> _logger;

    public ObserverIngestionPipeline(
        IReadOnlyList<NamedDataSource> sources, IntervalRotator rotator,
        LiveEventBroadcaster broadcaster, ObserverStateReporter state,
        ILogger<ObserverIngestionPipeline> logger)
    {
        _sources = sources;
        _rotator = rotator;
        _broadcaster = broadcaster;
        _state = state;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        _logger.LogInformation("Observer ingestion starting with {SourceCount} source(s)", _sources.Count);

        // Drain all sources concurrently; finishing one source doesn't stop the others
        var tasks = _sources.Select(s => RunOneSourceAsync(s, ct)).ToList();
        try
        {
            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogInformation("Observer ingestion stopping (cancelled)");
        }
    }

    private async Task RunOneSourceAsync(NamedDataSource source, CancellationToken ct)
    {
        try
        {
            await foreach (var record in source.Source.ReadAsync(ct))
            {
                await ProcessOneAsync(record, ct);
            }
            _logger.LogInformation("Source {Source} completed", source.Name);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { /* normal shutdown */ }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Source {Source} failed unrecoverably", source.Name);
            throw;
        }
    }

    private async Task ProcessOneAsync(DiagnosticRecord record, CancellationToken ct)
    {
        var writer = _rotator.CurrentWriter;
        if (writer is null)
        {
            // Should not normally happen — ObserverHostedService opens the first interval
            // before starting ingestion. Log and drop.
            _state.IncrementDropped();
            return;
        }

        try
        {
            switch (record)
            {
                case EventRecord ev:
                    await writer.AppendEventAsync(ev, ct);
                    _broadcaster.Publish(ev);  // live broadcast — events only for Phase 3
                    break;
                case StateSampleRecord ss when ss.Rate == StateSampleRate.Slow:
                    await writer.AppendStateAsync(ss, ct);
                    // Phase 3 doesn't stream slow state to clients
                    break;
                case StateSampleRecord ss when ss.Rate == StateSampleRate.Fast:
                    await writer.AppendFastStateAsync(ss, ct);
                    break;
            }
            _rotator.NotifyRecordWritten(record);
            _state.IncrementIngested();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write record from {Publisher} on topic {Topic}",
                record.PublisherNode, record.Topic);
            _state.IncrementDropped();
            // Don't propagate — keep the pipeline running
        }
    }
}
```

**Streaming choice in Phase 3**: only events flow to the live broadcaster. Slow state changes and fast state samples aren't streamed in Phase 3 because the Scenario View doesn't need them. Phase 5 (timeline) can extend streaming to slow state if needed.

### 3.8 NamedDataSource and Source Composition

The Observer subscribes to one or more `IDiagnosticDataSource` instances, each given a logical name for diagnostic purposes (logs, health output). This is a thin wrapping of the Phase 1 interface — no parallel hierarchy.

```csharp
namespace Tracer.Observer.Sources;

/// <summary>
/// Pairs an IDiagnosticDataSource with a logical name for the observer's logs and stats.
/// </summary>
public sealed record NamedDataSource(string Name, IDiagnosticDataSource Source);

/// <summary>
/// Builds the set of NamedDataSource instances from ObserverConfig.
/// In Phase 3: only Mock sources are supported.
/// In Phase 11: DDS source factory plugs in here.
/// </summary>
public static class DataSourceComposition
{
    public static IReadOnlyList<NamedDataSource> Build(ObserverConfig config)
    {
        return config.DataSources.Kind switch
        {
            "Mock" => BuildMockSources(config),
            _ => throw new InvalidOperationException(
                $"Unknown DataSources.Kind: '{config.DataSources.Kind}'")
        };
    }

    private static IReadOnlyList<NamedDataSource> BuildMockSources(ObserverConfig config)
    {
        var mock = config.DataSources.Mock 
            ?? throw new InvalidOperationException("Mock sources configured but config missing");
        if (mock.Sources.Count == 0)
            throw new InvalidOperationException("At least one mock source required");

        return mock.Sources
            .Select(s => new NamedDataSource(s.Name, new MockDataSource(s.ScenarioName, s.ScenarioConfig)))
            .ToList();
    }
}
```

The `MockDataSource` here is the same Phase 1 class implementing `IDiagnosticDataSource`. No new "adapter" or "factory interface" is introduced — the Observer just uses what Phase 1 provided.

In `ObserverHostBuilder`:

```csharp
builder.Services.AddSingleton<IReadOnlyList<NamedDataSource>>(sp =>
    DataSourceComposition.Build(sp.GetRequiredService<ObserverConfig>()));
```

For Phase 11, `DataSourceComposition.Build` gains a `"Dds"` branch that constructs `DdsDiagnosticDataSource` instances (which also implement `IDiagnosticDataSource`). The Observer doesn't change.

In Phase 11, a `DdsDataSourceFactory` adds DDS subscribers behind the same `IDiagnosticDataSource` interface — and the Observer code does not change.

### 3.9 ReadOnlyConnectionPool

Serving HTTP queries requires read access to the **active interval's** events.duckdb. Two facts complicate this:

1. **The writer's path changes at each interval rotation**: Phase 2 §6.2 stores intervals at `intervals/{intervalTimestamp}/events.duckdb` — there is no fixed "current" path. The active interval's path is whatever `IntervalRotator.CurrentDirectory` is right now.
2. **DuckDB connections opened against one file are not valid against another file**. When the writer closes its current Appender (interval rotation) and opens a new DB file, the read-only connections need to be replaced.

The pool resolves both concerns by being **rotation-aware**: it subscribes to rotation notifications from `IntervalRotator` and rebuilds its connection set when the active interval changes.

```csharp
namespace Tracer.Observer.Lifecycle;

public sealed class ReadOnlyConnectionPool : IAsyncDisposable
{
    private readonly ILogger<ReadOnlyConnectionPool> _logger;
    private readonly int _poolSize;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    
    private Channel<DuckDBConnection>? _connections;
    private string? _currentDbPath;
    private bool _disposed;

    public ReadOnlyConnectionPool(ObserverConfig config, ILogger<ReadOnlyConnectionPool> logger)
    {
        _logger = logger;
        _poolSize = 8;  // tunable; Phase 5 may grow this
    }

    /// <summary>
    /// Initial pool open against the current interval's events.duckdb.
    /// Called by ObserverHostedService after the first interval is opened.
    /// </summary>
    public async Task InitializeAsync(string activeIntervalDbPath, CancellationToken ct)
    {
        await SwitchToAsync(activeIntervalDbPath, ct);
    }

    /// <summary>
    /// Called by ObserverHostedService when IntervalRotator has switched to a new interval.
    /// Drains and disposes the old pool, builds a fresh pool against the new path.
    /// </summary>
    public async Task OnIntervalRotatedAsync(string newActiveDbPath, CancellationToken ct)
    {
        await SwitchToAsync(newActiveDbPath, ct);
    }

    private async Task SwitchToAsync(string newPath, CancellationToken ct)
    {
        await _refreshLock.WaitAsync(ct);
        try
        {
            if (_currentDbPath == newPath) return;
            
            _logger.LogInformation(
                "ReadOnlyConnectionPool switching from {Old} to {New}",
                _currentDbPath ?? "<none>", newPath);

            // Drain and dispose old connections (acquirers will get the new pool's connections)
            var old = _connections;
            _connections = Channel.CreateBounded<DuckDBConnection>(_poolSize);

            if (old is not null)
            {
                old.Writer.TryComplete();
                while (old.Reader.TryRead(out var conn))
                {
                    try { await conn.DisposeAsync(); } catch { /* best effort */ }
                }
                // Note: connections currently held by ongoing queries will dispose
                // themselves on return (see PooledConnection.DisposeAsync below).
            }

            _currentDbPath = newPath;

            // Open new connections against the new path
            for (int i = 0; i < _poolSize; i++)
            {
                var conn = new DuckDBConnection($"Data Source={newPath};ACCESS_MODE=READ_ONLY");
                await conn.OpenAsync(ct);
                await _connections.Writer.WriteAsync(conn, ct);
            }
        }
        finally { _refreshLock.Release(); }
    }

    public async Task<PooledConnection> AcquireAsync(CancellationToken ct)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(ReadOnlyConnectionPool));
        var pool = _connections ?? throw new InvalidOperationException(
            "ReadOnlyConnectionPool not initialized — InitializeAsync must be called first");
        var conn = await pool.Reader.ReadAsync(ct);
        // Capture the channel that owned this connection so PooledConnection.Dispose
        // returns to the correct (current) channel.
        return new PooledConnection(conn, pool, this);
    }

    internal async ValueTask ReturnAsync(DuckDBConnection conn, Channel<DuckDBConnection> ownerPool)
    {
        if (_disposed)
        {
            try { await conn.DisposeAsync(); } catch { }
            return;
        }
        // If the pool has rotated away from the channel that issued this connection,
        // dispose rather than return — the underlying file is no longer the active one.
        if (ownerPool != _connections)
        {
            try { await conn.DisposeAsync(); } catch { }
            return;
        }
        await ownerPool.Writer.WriteAsync(conn);
    }

    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        var pool = _connections;
        if (pool is null) return;
        pool.Writer.TryComplete();
        while (pool.Reader.TryRead(out var conn))
        {
            try { await conn.DisposeAsync(); } catch { }
        }
        _refreshLock.Dispose();
    }

    public sealed class PooledConnection : IAsyncDisposable
    {
        public DuckDBConnection Connection { get; }
        private readonly Channel<DuckDBConnection> _ownerPool;
        private readonly ReadOnlyConnectionPool _pool;
        private bool _disposed;

        internal PooledConnection(DuckDBConnection connection, Channel<DuckDBConnection> ownerPool, ReadOnlyConnectionPool pool)
        {
            Connection = connection;
            _ownerPool = ownerPool;
            _pool = pool;
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;
            await _pool.ReturnAsync(Connection, _ownerPool);
        }
    }
}
```

**Rotation handling design notes**:

- Queries that started before a rotation continue to read from the old DB file (already-issued read-only connections remain valid until the file is deleted). Their connection is disposed (not returned) when the query finishes — the `ownerPool != _connections` check catches this.
- Queries that arrive during rotation block briefly on `_refreshLock`, then receive a connection against the new path.
- DuckDB read connections against a file that has been closed by the writer are still valid for reading the file's content — the file remains on disk until retention evicts it. Phase 3's retention keeps `KeepLastNIntervals` (default 4) so there's no race between rotation and file deletion.

**Pool size**: 8 connections is a sensible starting default for one writer + a handful of concurrent viewers. Phase 3 doesn't need to be perfectly tuned. The pool can grow in Phase 5 when the timeline view drives more concurrent queries.

**Important Phase 3 simplification**: queries hit only the **active interval's** DB. Cross-interval queries (which span multiple completed intervals) are deferred to Phase 5 — the Scenario View only needs active-interval data because it shows live scenario state. The session-listing query can return sessions started in earlier intervals via session-start events in the active interval IF the session is still running; older finalized sessions only become visible when Phase 5 adds cross-interval querying.

This is a real simplification worth being explicit about: **Phase 3 queries only the active interval**. The handoff in §11 calls this out.

### 3.10 ObserverStateReporter

Observer-side counters and health state. The SSE health endpoint and the live status DTO read from this. Analogous in spirit to Phase 2's `AgentStateReporter` but observer-scoped.

```csharp
namespace Tracer.Observer.Lifecycle;

public sealed class ObserverStateReporter
{
    private long _ingestedTotal;
    private long _droppedTotal;
    private readonly RollingCounter _ingestedLastMinute = new(TimeSpan.FromMinutes(1));
    private DateTimeOffset _lastEventAt;
    private readonly object _lock = new();

    public void IncrementIngested()
    {
        Interlocked.Increment(ref _ingestedTotal);
        _ingestedLastMinute.Increment();
        lock (_lock) { _lastEventAt = DateTimeOffset.UtcNow; }
    }

    public void IncrementDropped()
    {
        Interlocked.Increment(ref _droppedTotal);
    }

    public ObserverStateSnapshot Snapshot()
    {
        DateTimeOffset lastEvent;
        lock (_lock) { lastEvent = _lastEventAt; }
        return new ObserverStateSnapshot
        {
            IngestedTotal = Interlocked.Read(ref _ingestedTotal),
            DroppedTotal = Interlocked.Read(ref _droppedTotal),
            IngestedLastMinute = _ingestedLastMinute.Count,
            LastEventUtc = lastEvent == default ? null : lastEvent
        };
    }
}

public sealed record ObserverStateSnapshot
{
    public required long IngestedTotal { get; init; }
    public required long DroppedTotal { get; init; }
    public required long IngestedLastMinute { get; init; }
    public DateTimeOffset? LastEventUtc { get; init; }
}

/// <summary>
/// Counter of increments that occurred within a sliding window.
/// Bucketed implementation; precision is one second.
/// </summary>
internal sealed class RollingCounter
{
    private readonly TimeSpan _window;
    private readonly long[] _buckets;
    private readonly object _lock = new();
    private long _lastBucketSecond;

    public RollingCounter(TimeSpan window)
    {
        _window = window;
        _buckets = new long[(int)window.TotalSeconds + 1];
        _lastBucketSecond = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }

    public void Increment()
    {
        var nowSec = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        lock (_lock)
        {
            AdvanceTo(nowSec);
            _buckets[nowSec % _buckets.Length]++;
        }
    }

    public long Count
    {
        get
        {
            var nowSec = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            lock (_lock)
            {
                AdvanceTo(nowSec);
                long sum = 0;
                for (int i = 0; i < _buckets.Length; i++) sum += _buckets[i];
                return sum;
            }
        }
    }

    private void AdvanceTo(long nowSec)
    {
        var gap = nowSec - _lastBucketSecond;
        if (gap <= 0) return;
        if (gap >= _buckets.Length)
        {
            Array.Clear(_buckets, 0, _buckets.Length);
        }
        else
        {
            for (long s = _lastBucketSecond + 1; s <= nowSec; s++)
                _buckets[s % _buckets.Length] = 0;
        }
        _lastBucketSecond = nowSec;
    }
}
```

### 3.11 ObserverHostedService

The hosted service that orchestrates startup, ingestion, rotation, retention, and shutdown. Analogous to Phase 2 §6.6 `AgentHostedService` but observer-shaped.

```csharp
namespace Tracer.Observer.Lifecycle;

public sealed class ObserverHostedService : BackgroundService
{
    private readonly StartupRecoveryService _recovery;
    private readonly IntervalRotator _rotator;
    private readonly IntervalScheduler _scheduler;
    private readonly ObserverIngestionPipeline _ingestion;
    private readonly ReadOnlyConnectionPool _pool;
    private readonly RetentionManager _retention;
    private readonly IClock _clock;
    private readonly ILogger<ObserverHostedService> _logger;

    public ObserverHostedService(
        StartupRecoveryService recovery, IntervalRotator rotator,
        IntervalScheduler scheduler, ObserverIngestionPipeline ingestion,
        ReadOnlyConnectionPool pool, RetentionManager retention,
        IClock clock, ILogger<ObserverHostedService> logger)
    {
        _recovery = recovery;
        _rotator = rotator;
        _scheduler = scheduler;
        _ingestion = ingestion;
        _pool = pool;
        _retention = retention;
        _clock = clock;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("TracerObserver starting");

        // 1. Recovery — finalize any orphaned intervals from previous run
        await _recovery.RecoverAsync(stoppingToken);

        // 2. Open the current interval
        await _rotator.OpenCurrentAsync(stoppingToken);

        // 3. Initialize the read-only connection pool against the active interval
        var activeDb = _rotator.CurrentDirectory!.EventsDbPath;
        await _pool.InitializeAsync(activeDb, stoppingToken);

        // 4. Start ingestion in background
        var ingestionTask = _ingestion.RunAsync(stoppingToken);

        // 5. Retention loop in background
        var retentionTask = RetentionLoopAsync(stoppingToken);

        // 6. Rotation loop runs on this task
        await RotationLoopAsync(stoppingToken);

        // 7. Shutdown propagates to background tasks
        await Task.WhenAll(ingestionTask, retentionTask);

        // 8. Final rotation to close the current interval cleanly
        await _rotator.RotateAsync(ManifestFinalizationReason.GracefulShutdown, CancellationToken.None);

        _logger.LogInformation("TracerObserver stopped");
    }

    private async Task RotationLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var timeUntilBoundary = _scheduler.TimeUntilNextBoundary();
            if (timeUntilBoundary > TimeSpan.Zero)
            {
                try { await Task.Delay(timeUntilBoundary, ct); }
                catch (OperationCanceledException) { return; }
            }
            await _rotator.RotateAsync(ManifestFinalizationReason.ScheduledRotation, ct);

            // After rotation, refresh the connection pool to point at the new active interval
            var newActiveDb = _rotator.CurrentDirectory!.EventsDbPath;
            try
            {
                await _pool.OnIntervalRotatedAsync(newActiveDb, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Connection pool refresh failed after rotation");
                // Pool remains pointing at the previous interval until next rotation;
                // degraded but functional
            }
        }
    }

    private async Task RetentionLoopAsync(CancellationToken ct)
    {
        var interval = TimeSpan.FromMinutes(5);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await _retention.ApplyAsync(ct);
                await Task.Delay(interval, ct);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Retention pass failed; continuing");
                try { await Task.Delay(interval, ct); } catch (OperationCanceledException) { return; }
            }
        }
    }
}
```

The structure mirrors `AgentHostedService` closely with one important addition: **after each rotation, the connection pool is refreshed** to point at the new active interval's DB. Without this step, queries would continue hitting the previous interval's (now-finalized) file — they would still get data, but new events captured into the new interval would be invisible to queries until the next pool refresh.

---

## 4. The Web API: REST Endpoints

The Phase 3 API surface is the minimum needed for the Session Browser and Scenario View. It's a strict subset of the full API described in architecture §14.

### 4.1 Phase 3 Endpoint Set

```
GET  /api/health                              health check
GET  /api/topology                            list of known nodes (extracted from data)

GET  /api/sessions                            list sessions discovered in data
GET  /api/sessions/{sessionId}                session detail

GET  /api/scenario/phases?sessionId=...       phase timeline for a session
GET  /api/scenario/notables?sessionId=...&limit=...    notable events stream for a session
GET  /api/scenario/state?sessionId=...        current scenario state snapshot

GET  /api/events/{eventId}                    single event detail (for drill-down)

GET  /api/live/notables?sessionId=...         SSE stream of new notable events
GET  /api/live/status                         observer health and stats
```

**Not in Phase 3** (deferred to Phase 5+):
- `GET /api/events` with general filters and time-range queries
- `/api/traces/*` causal endpoints
- `/api/entities/*` entity history
- `POST /api/sql` query escape hatch
- `/api/stats/*` statistics
- `/api/annotations`, `/api/bundles`

### 4.2 Endpoint Implementations

Endpoints are organized as static `Map` methods on focused classes, using ASP.NET Core Minimal APIs.

**SessionEndpoints.cs**

```csharp
namespace Tracer.WebApi.Endpoints;

public static class SessionEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/api/sessions", HandleListAsync)
           .WithName("ListSessions")
           .WithOpenApi();

        app.MapGet("/api/sessions/{sessionId}", HandleGetAsync)
           .WithName("GetSession")
           .WithOpenApi();
    }

    public static async Task<Results<Ok<IReadOnlyList<SessionDto>>, ProblemHttpResult>> HandleListAsync(
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromServices] SessionQueryService sessions,
        CancellationToken ct)
    {
        try
        {
            var range = (from is not null || to is not null)
                ? new TimeRange(
                    from is null ? WallclockTime.Zero : WallclockTime.FromDateTimeOffset(from.Value),
                    to is null ? WallclockTime.MaxValue : WallclockTime.FromDateTimeOffset(to.Value))
                : (TimeRange?)null;

            var results = await sessions.ListAsync(range, ct);
            return TypedResults.Ok(results);
        }
        catch (Exception ex)
        {
            return TypedResults.Problem(ProblemDetailsFactory.From(ex));
        }
    }

    public static async Task<Results<Ok<SessionDto>, NotFound, ProblemHttpResult>> HandleGetAsync(
        string sessionId,
        [FromServices] SessionQueryService sessions,
        CancellationToken ct)
    {
        try
        {
            var result = await sessions.GetAsync(sessionId, ct);
            return result is null
                ? TypedResults.NotFound()
                : TypedResults.Ok(result);
        }
        catch (Exception ex)
        {
            return TypedResults.Problem(ProblemDetailsFactory.From(ex));
        }
    }
}
```

**ScenarioEndpoints.cs**

```csharp
namespace Tracer.WebApi.Endpoints;

public static class ScenarioEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/api/scenario/phases", HandlePhasesAsync).WithOpenApi();
        app.MapGet("/api/scenario/notables", HandleNotablesAsync).WithOpenApi();
        app.MapGet("/api/scenario/state", HandleStateAsync).WithOpenApi();
    }

    public static async Task<Results<Ok<IReadOnlyList<ScenarioPhaseDto>>, ProblemHttpResult>> HandlePhasesAsync(
        [FromQuery] string sessionId,
        [FromServices] ScenarioQueryService scenarios,
        CancellationToken ct)
    {
        var result = await scenarios.GetPhasesAsync(sessionId, ct);
        return TypedResults.Ok(result);
    }

    public static async Task<Results<Ok<IReadOnlyList<NotableEventDto>>, ProblemHttpResult>> HandleNotablesAsync(
        [FromQuery] string sessionId,
        [FromQuery] int limit = 100,
        [FromQuery] DateTimeOffset? before = null,
        [FromServices] ScenarioQueryService scenarios = default!,
        CancellationToken ct = default)
    {
        if (limit < 1 || limit > 500)
            return TypedResults.Problem(new ProblemDetails
            {
                Title = "limit out of range",
                Detail = "limit must be 1..500",
                Status = StatusCodes.Status400BadRequest
            });

        var result = await scenarios.GetNotablesAsync(sessionId, limit, before, ct);
        return TypedResults.Ok(result);
    }

    public static async Task<Results<Ok<ScenarioStateDto>, ProblemHttpResult>> HandleStateAsync(
        [FromQuery] string sessionId,
        [FromServices] ScenarioQueryService scenarios,
        CancellationToken ct)
    {
        var result = await scenarios.GetCurrentStateAsync(sessionId, ct);
        return TypedResults.Ok(result);
    }
}
```

**EventEndpoints.cs**

Phase 3 only needs single-event lookup (for drill-down from the notable events list). The full event-query endpoint surface arrives in Phase 5.

```csharp
namespace Tracer.WebApi.Endpoints;

public static class EventEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/api/events/{eventId}", HandleGetAsync)
           .WithName("GetEvent")
           .WithOpenApi();
    }

    public static async Task<Results<Ok<EventDto>, NotFound, ProblemHttpResult>> HandleGetAsync(
        string eventId,
        [FromServices] EventLookupService lookups,
        CancellationToken ct)
    {
        if (!ulong.TryParse(eventId, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var id))
            return TypedResults.Problem(new ProblemDetails
            {
                Title = "Invalid eventId",
                Detail = "eventId must be a 16-character hex string",
                Status = StatusCodes.Status400BadRequest
            });

        var result = await lookups.GetAsync(new EventId(id), ct);
        return result is null ? TypedResults.NotFound() : TypedResults.Ok(result);
    }
}
```

`EventLookupService` is a thin query service: `SELECT * FROM events WHERE event_id = ? LIMIT 1`, mapped to `EventDto`. Implementation is straightforward and omitted here.

**TopologyEndpoints.cs**

```csharp
namespace Tracer.WebApi.Endpoints;

public static class TopologyEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/api/topology", HandleAsync)
           .WithName("GetTopology")
           .WithOpenApi();
    }

    public static async Task<Ok<TopologyDto>> HandleAsync(
        [FromServices] TopologyQueryService topology,
        CancellationToken ct)
    {
        var result = await topology.GetCurrentAsync(ct);
        return TypedResults.Ok(result);
    }
}
```

`TopologyQueryService.GetCurrentAsync` runs `SELECT publisher_node, MIN(publish_wallclock), MAX(publish_wallclock), COUNT(*) FROM events GROUP BY publisher_node` against the active interval and maps to `TopologyDto`. Phase 3's view is current-interval-only by the simplification noted in §3.9.

**HealthEndpoints.cs**

```csharp
namespace Tracer.WebApi.Endpoints;

public static class HealthEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/api/health", () => TypedResults.Ok(new { status = "ok" }))
           .WithName("Health")
           .WithOpenApi();
    }
}
```

Trivial liveness check. The richer health view is the SSE `/api/live/status` endpoint (§5.3).

### 4.3 DTOs

The DTOs are the contract between backend and frontend. Generated TypeScript types come from these via NSwag.

```csharp
namespace Tracer.WebApi.Contracts.Dto;

public sealed record SessionDto
{
    public required string SessionId { get; init; }
    public required string ScenarioId { get; init; }
    public string? Label { get; init; }
    public required DateTimeOffset StartUtc { get; init; }
    public DateTimeOffset? EndUtc { get; init; }
    public required SessionStatus Status { get; init; }
    public required IReadOnlyList<string> ParticipatingNodes { get; init; }
    public required long EventCount { get; init; }
}

public enum SessionStatus
{
    Active,         // session-start observed, no session-end yet
    Completed,      // session-end observed
    Inferred        // no session events but inferred from time range
}

public sealed record ScenarioPhaseDto
{
    public required string PhaseName { get; init; }
    public required DateTimeOffset StartedAtUtc { get; init; }
    public DateTimeOffset? EndedAtUtc { get; init; }
    public required PhaseStatus Status { get; init; }
}

public enum PhaseStatus { Active, Completed }

public sealed record NotableEventDto
{
    public required string EventId { get; init; }
    public required string TraceId { get; init; }
    public required DateTimeOffset OccurredAtUtc { get; init; }
    public required string NodeId { get; init; }
    public required string Topic { get; init; }
    public required string Label { get; init; }    // the scenario-author-provided text
    public string? Severity { get; init; }
    public string? EntityId { get; init; }
    public string? PlayerId { get; init; }
    public string? ScenarioPhase { get; init; }
    public required string PayloadJson { get; init; }
}

/// <summary>
/// Full event detail used by /api/events/{eventId} for drill-down from notables.
/// Phase 5 timeline view returns lists of these for general event queries.
/// </summary>
public sealed record EventDto
{
    public required string EventId { get; init; }              // 16-char hex
    public required string TraceId { get; init; }              // 16-char hex
    public string? ParentEventId { get; init; }                // 16-char hex, null for root
    public required DateTimeOffset PublishWallclock { get; init; }
    public required DateTimeOffset ReceiveWallclock { get; init; }
    public required string PublisherNode { get; init; }
    public required string SubscriberNode { get; init; }
    public required string Topic { get; init; }
    public required ulong SequenceNumber { get; init; }
    public string? EntityId { get; init; }
    public string? OwningPlayerId { get; init; }
    public string? ScenarioPhase { get; init; }
    public string? Severity { get; init; }
    public string? NotableLabel { get; init; }
    public required string PayloadJson { get; init; }
}

public sealed record ScenarioStateDto
{
    public required string SessionId { get; init; }
    public required string ScenarioId { get; init; }
    public string? CurrentPhase { get; init; }
    public DateTimeOffset? CurrentPhaseStartedAt { get; init; }
    public required DateTimeOffset NowUtc { get; init; }
    public required TimeSpan SessionElapsed { get; init; }
    public required long TotalEvents { get; init; }
    public required long TotalNotables { get; init; }
    public required IReadOnlyList<string> ParticipatingNodes { get; init; }
}

public sealed record TopologyDto
{
    public required IReadOnlyList<NodeInfoDto> Nodes { get; init; }
}

public sealed record NodeInfoDto
{
    public required string NodeId { get; init; }
    public required DateTimeOffset FirstSeenUtc { get; init; }
    public required DateTimeOffset LastSeenUtc { get; init; }
    public required long EventsPublished { get; init; }
}

public sealed record LiveStatusDto
{
    public required DateTimeOffset NowUtc { get; init; }
    public required bool IngestionHealthy { get; init; }
    public required long EventsIngestedTotal { get; init; }
    public required long EventsLastMinute { get; init; }
    public required int ActiveSseClients { get; init; }
    public required string CurrentIntervalTimestamp { get; init; }
}
```

**DTO conventions**:
- Use `DateTimeOffset` rather than `WallclockTime` at the API boundary — easier for clients to consume. Conversion happens in mapping.
- IDs are strings (uint64 trace/event IDs are serialized as hex strings; node IDs are already strings).
- `record` types with `init`-only properties — immutable, well-equality-defined for tests.

### 4.4 Query Services

Query services encapsulate the SQL and DTO mapping. They take a `PooledConnection`, run the query, map results, return the connection to the pool.

**SessionQueryService.cs (sketch)**

The session listing query has subtleties worth explaining:

- A "session" is defined by a `system.session_start` event. It may or may not be followed by a `system.session_end` event (active sessions don't have one yet).
- For each session start, find the matching end (if any), then count events that fell within the session's time range and the set of participating nodes.
- The query uses two steps: (1) pair starts and ends; (2) for each session, query its event statistics. Doing it all in one big LEFT JOIN multiplies rows in ways DuckDB's query planner can handle but that are harder to reason about and debug.

```csharp
namespace Tracer.WebApi.Queries;

public sealed class SessionQueryService
{
    private readonly ReadOnlyConnectionPool _pool;
    private readonly ILogger<SessionQueryService> _logger;

    public async Task<IReadOnlyList<SessionDto>> ListAsync(TimeRange? range, CancellationToken ct)
    {
        await using var pooled = await _pool.AcquireAsync(ct);
        
        // Step 1: discover session starts and pair with ends.
        // session_label and scenario_id come from the start event payload.
        var sessions = await ListSessionStartsAsync(pooled.Connection, range, ct);
        if (sessions.Count == 0) return Array.Empty<SessionDto>();

        // Step 2: for each session, fetch participating nodes and event count.
        // For small N (Phase 3: typically 1-10 sessions per active interval), serial
        // queries are fine. Phase 5 may batch.
        var results = new List<SessionDto>(sessions.Count);
        foreach (var s in sessions)
        {
            var (nodes, eventCount) = await GetSessionStatsAsync(
                pooled.Connection, s.SessionId, s.StartedAt, s.EndedAt, ct);
            results.Add(new SessionDto
            {
                SessionId = s.SessionId,
                ScenarioId = s.ScenarioId,
                Label = s.Label,
                StartUtc = new DateTimeOffset(s.StartedAt, TimeSpan.Zero),
                EndUtc = s.EndedAt is null ? null : new DateTimeOffset(s.EndedAt.Value, TimeSpan.Zero),
                Status = s.EndedAt is null ? SessionStatus.Active : SessionStatus.Completed,
                ParticipatingNodes = nodes,
                EventCount = eventCount
            });
        }
        return results;
    }

    private sealed record SessionStartEndPair(
        string SessionId, string ScenarioId, string? Label,
        DateTime StartedAt, DateTime? EndedAt);

    private async Task<IReadOnlyList<SessionStartEndPair>> ListSessionStartsAsync(
        DuckDBConnection conn, TimeRange? range, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            WITH starts AS (
                SELECT
                    JSON_EXTRACT_STRING(payload, '$.sessionId')   AS session_id,
                    JSON_EXTRACT_STRING(payload, '$.scenarioId')  AS scenario_id,
                    JSON_EXTRACT_STRING(payload, '$.sessionLabel') AS label,
                    publish_wallclock AS started_at
                FROM events
                WHERE topic = 'system.session_start'
                  AND ($from IS NULL OR publish_wallclock >= $from)
                  AND ($to   IS NULL OR publish_wallclock <  $to)
            ),
            ends AS (
                SELECT
                    JSON_EXTRACT_STRING(payload, '$.sessionId') AS session_id,
                    MIN(publish_wallclock) AS ended_at
                FROM events
                WHERE topic = 'system.session_end'
                GROUP BY session_id
            )
            SELECT s.session_id, s.scenario_id, s.label, s.started_at, e.ended_at
            FROM starts s
            LEFT JOIN ends e ON s.session_id = e.session_id
            ORDER BY s.started_at DESC
            LIMIT 100;
            """;
        cmd.Parameters.Add(new DuckDBParameter("from", range?.From.ToDateTimeOffset()));
        cmd.Parameters.Add(new DuckDBParameter("to",   range?.To.ToDateTimeOffset()));

        var list = new List<SessionStartEndPair>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new SessionStartEndPair(
                SessionId:  reader.GetString(0),
                ScenarioId: reader.GetString(1),
                Label:      reader.IsDBNull(2) ? null : reader.GetString(2),
                StartedAt:  reader.GetDateTime(3),
                EndedAt:    reader.IsDBNull(4) ? null : reader.GetDateTime(4)));
        }
        return list;
    }

    private async Task<(IReadOnlyList<string> Nodes, long EventCount)> GetSessionStatsAsync(
        DuckDBConnection conn, string sessionId, DateTime startedAt, DateTime? endedAt, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        // Stats over events within the session's time range.
        // For active (no end) sessions, use "everything from start onward".
        cmd.CommandText = """
            SELECT publisher_node, COUNT(*) as cnt
            FROM events
            WHERE publish_wallclock >= $startedAt
              AND ($endedAt IS NULL OR publish_wallclock < $endedAt)
            GROUP BY publisher_node
            ORDER BY publisher_node;
            """;
        cmd.Parameters.Add(new DuckDBParameter("startedAt", startedAt));
        cmd.Parameters.Add(new DuckDBParameter("endedAt",   endedAt));

        var nodes = new List<string>();
        long eventCount = 0;
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            nodes.Add(reader.GetString(0));
            eventCount += reader.GetInt64(1);
        }
        return (nodes, eventCount);
    }

    public async Task<SessionDto?> GetAsync(string sessionId, CancellationToken ct) { /* analogous: find specific session, fetch stats */ }
}
```

**ScenarioQueryService.cs (sketch)**

```csharp
namespace Tracer.WebApi.Queries;

public sealed class ScenarioQueryService
{
    private readonly ReadOnlyConnectionPool _pool;

    public async Task<IReadOnlyList<NotableEventDto>> GetNotablesAsync(
        string sessionId, int limit, DateTimeOffset? before, CancellationToken ct)
    {
        await using var pooled = await _pool.AcquireAsync(ct);
        await using var cmd = pooled.Connection.CreateCommand();
        
        // First resolve the session's time range
        var sessionRange = await ResolveSessionTimeRangeAsync(pooled.Connection, sessionId, ct);
        if (sessionRange is null) return Array.Empty<NotableEventDto>();

        cmd.CommandText = """
            SELECT event_id, trace_id, publish_wallclock, publisher_node, topic,
                   notable_label, severity, entity_id, owning_player_id, scenario_phase, payload
            FROM events
            WHERE notable_label IS NOT NULL
              AND publish_wallclock >= $sessionStart
              AND ($sessionEnd IS NULL OR publish_wallclock < $sessionEnd)
              AND ($before IS NULL OR publish_wallclock < $before)
            ORDER BY publish_wallclock DESC
            LIMIT $limit;
            """;
        cmd.Parameters.Add(new DuckDBParameter("sessionStart", sessionRange.Value.From.ToDateTimeOffset()));
        cmd.Parameters.Add(new DuckDBParameter("sessionEnd", 
            sessionRange.Value.To == WallclockTime.MaxValue ? null : (object?)sessionRange.Value.To.ToDateTimeOffset()));
        cmd.Parameters.Add(new DuckDBParameter("before", before));
        cmd.Parameters.Add(new DuckDBParameter("limit", limit));

        // ...read + map to NotableEventDto...
        return Array.Empty<NotableEventDto>(); // detail omitted
    }

    public async Task<IReadOnlyList<ScenarioPhaseDto>> GetPhasesAsync(string sessionId, CancellationToken ct)
    {
        // Convention: scenario emits events on topic 'scenario.phase_started' and 'scenario.phase_ended'
        // with payload { phaseName: string }
        // Pair them, return ordered list
        return Array.Empty<ScenarioPhaseDto>(); // detail omitted
    }

    public async Task<ScenarioStateDto> GetCurrentStateAsync(string sessionId, CancellationToken ct)
    {
        // Aggregate query: session start, current phase (latest phase_started without phase_ended),
        // total events, total notables, participating nodes
        return null!; // detail omitted
    }
}
```

**Pragmatic SQL choice**: the queries lean on `JSON_EXTRACT_STRING` for fields stored in the JSON payload column. For Phase 3 this is acceptable — the query rates are low (one query per browser interaction). When Phase 5 introduces high-volume timeline queries, frequently-accessed JSON fields should be promoted to top-level columns at ingest time. The schema already provides for this (`entity_id`, `owning_player_id`, `scenario_phase`, `notable_label`, `severity` are already promoted).

### 4.5 Error Handling

Single source of truth for HTTP error responses: a problem-details factory plus middleware.

```csharp
namespace Tracer.WebApi.Errors;

public static class ApiExceptionMiddleware
{
    public static async Task HandleAsync(HttpContext ctx)
    {
        var feature = ctx.Features.Get<IExceptionHandlerFeature>();
        var ex = feature?.Error;
        var problem = ProblemDetailsFactory.From(ex);
        ctx.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;
        ctx.Response.ContentType = "application/problem+json";
        await ctx.Response.WriteAsJsonAsync(problem);
    }
}

public static class ProblemDetailsFactory
{
    public static ProblemDetails From(Exception? ex) => ex switch
    {
        ArgumentException ae => new ProblemDetails
        {
            Title = "Bad request",
            Detail = ae.Message,
            Status = StatusCodes.Status400BadRequest
        },
        TracerStorageException tse => new ProblemDetails
        {
            Title = "Storage error",
            Detail = tse.Message,
            Status = StatusCodes.Status500InternalServerError
        },
        _ => new ProblemDetails
        {
            Title = "Internal server error",
            Detail = "An unexpected error occurred",
            Status = StatusCodes.Status500InternalServerError
        }
    };
}
```

**No raw exception bodies in HTTP responses.** Stack traces are logged server-side; clients receive sanitized problem details. This is a security discipline (avoids leaking internals) and a usability discipline (clients shouldn't have to parse stack traces).

---

## 5. Live Streaming via SSE

The Scenario View needs new notable events to appear without page refresh. Server-Sent Events (SSE) is the natural choice: one-way push, simple HTTP, automatic reconnect by browsers, no WebSocket complexity.

### 5.1 LiveEventBroadcaster

```csharp
namespace Tracer.WebApi.Streaming;

/// <summary>
/// Receives published events from the ingestion pipeline and fans them out
/// to subscribed SSE clients per their filter.
/// </summary>
public sealed class LiveEventBroadcaster : BackgroundService
{
    private readonly Channel<EventRecord> _inbox;
    private readonly SseConnectionManager _connections;
    private readonly ILogger<LiveEventBroadcaster> _logger;

    public LiveEventBroadcaster(SseConnectionManager connections, ILogger<LiveEventBroadcaster> logger)
    {
        _inbox = Channel.CreateUnbounded<EventRecord>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false   // ingestion pipeline writes; many sources possible
        });
        _connections = connections;
        _logger = logger;
    }

    /// <summary>Called by ObserverIngestionPipeline when an event is captured.</summary>
    public void Publish(EventRecord ev)
    {
        _inbox.Writer.TryWrite(ev);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var ev in _inbox.Reader.ReadAllAsync(stoppingToken))
        {
            await _connections.BroadcastAsync(ev, stoppingToken);
        }
    }
}
```

### 5.2 SseConnectionManager

```csharp
namespace Tracer.WebApi.Streaming;

public sealed class SseConnectionManager
{
    private readonly ConcurrentDictionary<Guid, SseConnection> _connections = new();
    private readonly LiveStreamingConfig _config;
    private readonly ILogger<SseConnectionManager> _logger;

    public SseConnectionManager(ObserverConfig observerConfig, ILogger<SseConnectionManager> logger)
    {
        _config = observerConfig.LiveStreaming;
        _logger = logger;
    }

    public int ActiveCount => _connections.Count;

    public async Task<bool> TryRegisterAsync(SseConnection conn, CancellationToken ct)
    {
        if (_connections.Count >= _config.MaxConcurrentSseClients)
            return false;
        _connections[conn.Id] = conn;
        await Task.CompletedTask;
        return true;
    }

    public void Deregister(Guid id) => _connections.TryRemove(id, out _);

    public async Task BroadcastAsync(EventRecord ev, CancellationToken ct)
    {
        // Fan out to clients whose filter matches.
        // For Phase 3, the only "filter" is sessionId — clients subscribe to one session.
        var tasks = new List<Task>();
        foreach (var conn in _connections.Values)
        {
            if (conn.Filter.Matches(ev))
                tasks.Add(conn.EnqueueAsync(ev, ct));
        }
        await Task.WhenAll(tasks);
    }
}

public sealed class SseConnection
{
    public Guid Id { get; } = Guid.NewGuid();
    public required SseFilter Filter { get; init; }
    private readonly Channel<EventRecord> _outbox;
    private readonly int _bufferSize;
    private long _droppedCount;

    public SseConnection(int bufferSize)
    {
        _bufferSize = bufferSize;
        _outbox = Channel.CreateBounded<EventRecord>(new BoundedChannelOptions(bufferSize)
        {
            FullMode = BoundedChannelFullMode.DropOldest
        });
    }

    public async Task EnqueueAsync(EventRecord ev, CancellationToken ct)
    {
        // If client is too slow and bounded channel drops oldest, increment drop counter
        var wrote = _outbox.Writer.TryWrite(ev);
        if (!wrote) Interlocked.Increment(ref _droppedCount);
        await Task.CompletedTask;
    }

    public IAsyncEnumerable<EventRecord> ReadAsync(CancellationToken ct)
        => _outbox.Reader.ReadAllAsync(ct);

    public void Close() => _outbox.Writer.TryComplete();
}

public sealed record SseFilter
{
    public string? SessionId { get; init; }
    public bool NotablesOnly { get; init; } = false;

    public bool Matches(EventRecord ev)
    {
        if (NotablesOnly && string.IsNullOrEmpty(ev.NotableLabel)) return false;
        if (SessionId is not null)
        {
            // Match by inferring session from event time range — implemented separately
            // For Phase 3: simple heuristic, all events broadcast to all session subscribers
            // and the frontend filters client-side. Acceptable at expected scales.
            // Phase 5 may implement server-side session-to-event correlation.
        }
        return true;
    }
}
```

### 5.3 SseEndpoints

```csharp
namespace Tracer.WebApi.Endpoints;

public static class SseEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/api/live/notables", HandleNotablesStreamAsync);
        app.MapGet("/api/live/status",   HandleStatusAsync);
    }

    public static async Task HandleNotablesStreamAsync(
        HttpContext ctx,
        [FromQuery] string sessionId,
        [FromServices] SseConnectionManager mgr,
        [FromServices] ObserverConfig config,
        CancellationToken ct)
    {
        ctx.Response.Headers["Content-Type"] = "text/event-stream";
        ctx.Response.Headers["Cache-Control"] = "no-cache";
        ctx.Response.Headers["X-Accel-Buffering"] = "no";  // disable reverse-proxy buffering
        await ctx.Response.Body.FlushAsync(ct);

        var conn = new SseConnection(config.LiveStreaming.PerClientBufferSize)
        {
            Filter = new SseFilter { SessionId = sessionId, NotablesOnly = true }
        };

        if (!await mgr.TryRegisterAsync(conn, ct))
        {
            ctx.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return;
        }

        try
        {
            var heartbeatCt = CancellationTokenSource.CreateLinkedTokenSource(ct, ctx.RequestAborted);
            var heartbeatTask = SendHeartbeatsAsync(ctx, config.LiveStreaming.HeartbeatInterval, heartbeatCt.Token);

            await foreach (var ev in conn.ReadAsync(heartbeatCt.Token))
            {
                // Only send notables
                if (string.IsNullOrEmpty(ev.NotableLabel)) continue;
                
                var dto = DtoMappers.ToNotableEventDto(ev);
                var json = JsonSerializer.Serialize(dto, ApiJsonSettings.Default);
                await ctx.Response.WriteAsync($"data: {json}\n\n", heartbeatCt.Token);
                await ctx.Response.Body.FlushAsync(heartbeatCt.Token);
            }

            heartbeatCt.Cancel();
            try { await heartbeatTask; } catch { /* expected on cancel */ }
        }
        catch (OperationCanceledException) { /* client disconnect */ }
        finally
        {
            mgr.Deregister(conn.Id);
            conn.Close();
        }
    }

    private static async Task SendHeartbeatsAsync(HttpContext ctx, TimeSpan interval, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(interval, ct);
            await ctx.Response.WriteAsync(": keepalive\n\n", ct);
            await ctx.Response.Body.FlushAsync(ct);
        }
    }

    public static async Task<Ok<LiveStatusDto>> HandleStatusAsync(
        [FromServices] SseConnectionManager mgr,
        [FromServices] ObserverStateReporter state,
        [FromServices] IntervalRotator rotator,
        [FromServices] IClock clock)
    {
        var snap = state.Snapshot();
        var now = clock.Now.ToDateTimeOffset();
        var ingestionHealthy = snap.LastEventUtc is { } last
            && (now - last) < TimeSpan.FromSeconds(60);

        var dto = new LiveStatusDto
        {
            NowUtc = now,
            IngestionHealthy = ingestionHealthy,
            EventsIngestedTotal = snap.IngestedTotal,
            EventsLastMinute = snap.IngestedLastMinute,
            ActiveSseClients = mgr.ActiveCount,
            CurrentIntervalTimestamp = rotator.CurrentDirectory?.Timestamp.Value ?? ""
        };
        return TypedResults.Ok(dto);
    }
}
```

### 5.4 SSE Reliability Considerations

- **Client reconnect**: browsers auto-reconnect on transient SSE failures. The `Last-Event-ID` header semantics are not used in Phase 3 — clients fetch a fresh page of notables from `GET /api/scenario/notables` on reconnect to catch up. This is simpler than implementing replay-from-id and acceptable for the Scenario View use case.
- **Slow clients**: the bounded channel drops oldest events when the client doesn't keep up. The dropped count is exposed via the connection's diagnostic state. Phase 3 logs slow-client warnings but doesn't expose them to operators yet.
- **Heartbeat**: every 15 seconds (configurable) the server sends a `: keepalive` comment line. Browsers and intermediate proxies will keep the connection alive. Without heartbeats, idle connections risk being terminated by network middleboxes.
- **Connection limits**: configured cap (default 50). Returning 503 when at cap is the right behavior; clients should treat this as transient.

### 5.5 Health Indicators

The `LiveStatusDto` and `/api/health` endpoint expose enough signal for the viewer to show a "live indicator" (green/yellow/red) in the UI. The mapping:

- **Green ("healthy live")**: ingestion healthy in last 60s, < 80% SSE capacity, last event within 30s
- **Yellow ("degraded")**: ingestion healthy but slow, or SSE capacity 80-95%
- **Red ("not live")**: no events in last 60s, OR ingestion errors, OR SSE capacity full

Phase 3 implements the green/red distinction. Yellow is a future refinement.

---

## 6. The Vue Frontend

### 6.1 Project Setup

```json
// tracer-viewer/package.json
{
  "name": "tracer-viewer",
  "private": true,
  "version": "0.1.0",
  "type": "module",
  "scripts": {
    "dev": "vite",
    "build": "vue-tsc -b && vite build",
    "preview": "vite preview",
    "test:unit": "vitest run",
    "test:e2e": "playwright test",
    "lint": "eslint . --ext .ts,.vue",
    "format": "prettier --write \"src/**/*.{ts,vue,css,scss}\""
  },
  "dependencies": {
    "vue": "^3.4.0",
    "vue-router": "^4.2.5",
    "pinia": "^2.1.7",
    "@microsoft/fetch-event-source": "^2.0.1"
  },
  "devDependencies": {
    "@vitejs/plugin-vue": "^5.0.0",
    "typescript": "~5.3.0",
    "vite": "^5.0.0",
    "vue-tsc": "^1.8.0",
    "vitest": "^1.0.0",
    "@vue/test-utils": "^2.4.0",
    "jsdom": "^23.0.0",
    "@playwright/test": "^1.40.0",
    "eslint": "^8.55.0",
    "eslint-plugin-vue": "^9.18.0",
    "@typescript-eslint/eslint-plugin": "^6.13.0",
    "@typescript-eslint/parser": "^6.13.0",
    "prettier": "^3.1.0",
    "sass": "^1.69.0"
  }
}
```

### 6.2 vite.config.ts

```typescript
import { defineConfig } from 'vite';
import vue from '@vitejs/plugin-vue';
import path from 'node:path';

export default defineConfig({
  plugins: [vue()],
  resolve: {
    alias: {
      '@': path.resolve(__dirname, './src'),
    },
  },
  server: {
    port: 5173,
    proxy: {
      '/api': {
        target: 'http://localhost:5300',
        changeOrigin: true,
      },
    },
  },
  build: {
    outDir: '../src/Tracer.Observer/wwwroot',
    emptyOutDir: true,
    sourcemap: true,
    rollupOptions: {
      output: {
        manualChunks: {
          'vue-runtime': ['vue', 'vue-router', 'pinia'],
        },
      },
    },
  },
});
```

**Notable choices**:
- Dev server runs on 5173, proxies `/api/*` to the observer on 5300. Devs run both together: `dotnet run --project Tracer.Observer` plus `pnpm dev`.
- Production build output goes directly into `Tracer.Observer/wwwroot`. This means the observer process serves the built frontend with no separate web server needed.
- Manual vendor chunk for the major Vue libraries — helps with caching.
- Sourcemaps in production — they're small relative to the bundle and invaluable for debugging real issues.

### 6.3 App Shell and Routing

```vue
<!-- src/App.vue -->
<script setup lang="ts">
import { RouterView } from 'vue-router';
import AppHeader from './components/AppHeader.vue';
</script>

<template>
  <div class="app">
    <AppHeader />
    <main class="app__main">
      <RouterView v-slot="{ Component }">
        <Transition mode="out-in" name="fade">
          <component :is="Component" />
        </Transition>
      </RouterView>
    </main>
  </div>
</template>
```

```typescript
// src/router/index.ts
import { createRouter, createWebHistory } from 'vue-router';

const router = createRouter({
  history: createWebHistory(),
  routes: [
    {
      path: '/',
      redirect: '/sessions',
    },
    {
      path: '/sessions',
      name: 'sessions',
      component: () => import('@/views/SessionBrowserView.vue'),
    },
    {
      path: '/scenario/:sessionId',
      name: 'scenario',
      component: () => import('@/views/ScenarioView.vue'),
      props: true,
    },
  ],
});

export default router;
```

### 6.4 Generated API Client

NSwag generates a TypeScript client from the observer's OpenAPI document. Configured via `NSwag.MSBuild`:

```xml
<!-- Tracer.WebApi.csproj -->
<Target Name="GenerateTypeScriptClient" AfterTargets="Build" Condition="'$(Configuration)' == 'Debug'">
  <Exec Command="dotnet nswag run nswag.json /variables:Configuration=$(Configuration)"
        WorkingDirectory="$(MSBuildProjectDirectory)" />
</Target>
```

The output is `tracer-viewer/src/api/tracerApiClient.ts` — types and methods for every endpoint.

```typescript
// example shape of generated code (simplified):
export class TracerApiClient {
  baseUrl: string;
  constructor(baseUrl = '') { this.baseUrl = baseUrl; }

  async listSessions(from?: Date, to?: Date): Promise<SessionDto[]> { /* ... */ }
  async getSession(sessionId: string): Promise<SessionDto> { /* ... */ }
  async getScenarioPhases(sessionId: string): Promise<ScenarioPhaseDto[]> { /* ... */ }
  async getScenarioNotables(sessionId: string, limit?: number, before?: Date): Promise<NotableEventDto[]> { /* ... */ }
  async getScenarioState(sessionId: string): Promise<ScenarioStateDto> { /* ... */ }
  async getLiveStatus(): Promise<LiveStatusDto> { /* ... */ }
}

export interface SessionDto {
  sessionId: string;
  scenarioId: string;
  label?: string;
  startUtc: Date;
  endUtc?: Date;
  status: SessionStatus;
  participatingNodes: string[];
  eventCount: number;
}

// ... etc
```

### 6.5 Stores (Pinia)

State is held in Pinia stores. Phase 3 stores:

**sessionStore.ts** — currently-selected session, derived data

```typescript
import { defineStore } from 'pinia';
import type { SessionDto, ScenarioStateDto } from '@/api/tracerApiClient';

export const useSessionStore = defineStore('session', {
  state: () => ({
    current: null as SessionDto | null,
    state: null as ScenarioStateDto | null,
    loading: false,
    error: null as string | null,
  }),
  actions: {
    async load(sessionId: string) {
      this.loading = true;
      this.error = null;
      try {
        const api = useApi();
        this.current = await api.getSession(sessionId);
        this.state = await api.getScenarioState(sessionId);
      } catch (err: any) {
        this.error = err.message ?? 'Failed to load session';
      } finally {
        this.loading = false;
      }
    },
    async refreshState() {
      if (!this.current) return;
      const api = useApi();
      this.state = await api.getScenarioState(this.current.sessionId);
    },
    clear() {
      this.current = null;
      this.state = null;
      this.error = null;
    },
  },
});
```

**liveStore.ts** — SSE state, connection health, drop indicators

```typescript
import { defineStore } from 'pinia';

export interface LiveConnectionState {
  connected: boolean;
  lastEventAt: Date | null;
  reconnectAttempts: number;
}

export const useLiveStore = defineStore('live', {
  state: () => ({
    connection: {
      connected: false,
      lastEventAt: null,
      reconnectAttempts: 0,
    } as LiveConnectionState,
  }),
  actions: {
    setConnected(connected: boolean) {
      this.connection.connected = connected;
      if (connected) this.connection.reconnectAttempts = 0;
    },
    onEvent() {
      this.connection.lastEventAt = new Date();
    },
    onReconnect() {
      this.connection.reconnectAttempts += 1;
    },
  },
});
```

### 6.6 useLiveSse Composable

Encapsulates SSE connection logic, reconnect, cleanup.

```typescript
// src/composables/useLiveSse.ts
import { onMounted, onUnmounted, ref } from 'vue';
import { fetchEventSource } from '@microsoft/fetch-event-source';
import { useLiveStore } from '@/stores/liveStore';
import type { NotableEventDto } from '@/api/tracerApiClient';

export function useLiveNotables(sessionId: string) {
  const liveStore = useLiveStore();
  const events = ref<NotableEventDto[]>([]);
  let abortCtrl: AbortController | null = null;

  const connect = async () => {
    abortCtrl = new AbortController();
    const url = `/api/live/notables?sessionId=${encodeURIComponent(sessionId)}`;

    try {
      await fetchEventSource(url, {
        signal: abortCtrl.signal,
        openWhenHidden: true,
        onopen: async (response) => {
          if (response.ok) liveStore.setConnected(true);
          else throw new Error(`SSE open failed: ${response.status}`);
        },
        onmessage: (ev) => {
          if (!ev.data) return;
          try {
            const dto = JSON.parse(ev.data) as NotableEventDto;
            events.value = [dto, ...events.value].slice(0, 200); // keep latest 200
            liveStore.onEvent();
          } catch (err) {
            console.error('Failed to parse SSE event:', err);
          }
        },
        onclose: () => liveStore.setConnected(false),
        onerror: (err) => {
          liveStore.setConnected(false);
          liveStore.onReconnect();
          // Let fetchEventSource handle backoff
        },
      });
    } catch (err) {
      console.error('SSE connection error:', err);
    }
  };

  onMounted(connect);
  onUnmounted(() => abortCtrl?.abort());

  return { events };
}
```

### 6.7 Session Browser View

The entry-point view. Lists available sessions, lets the user pick one.

```vue
<!-- src/views/SessionBrowserView.vue -->
<script setup lang="ts">
import { onMounted, ref } from 'vue';
import { useRouter } from 'vue-router';
import { useApi } from '@/api/useApi';
import type { SessionDto } from '@/api/tracerApiClient';
import SessionCard from '@/components/SessionCard.vue';
import LoadingSpinner from '@/components/LoadingSpinner.vue';
import ErrorMessage from '@/components/ErrorMessage.vue';

const router = useRouter();
const sessions = ref<SessionDto[]>([]);
const loading = ref(false);
const error = ref<string | null>(null);

const load = async () => {
  loading.value = true;
  error.value = null;
  try {
    const api = useApi();
    sessions.value = await api.listSessions();
  } catch (err: any) {
    error.value = err.message ?? 'Failed to load sessions';
  } finally {
    loading.value = false;
  }
};

const openSession = (s: SessionDto) => {
  router.push({ name: 'scenario', params: { sessionId: s.sessionId } });
};

onMounted(load);
</script>

<template>
  <div class="session-browser">
    <h1>Sessions</h1>
    <p class="session-browser__hint">
      Select a session to view its scenario flow and notable events.
    </p>

    <LoadingSpinner v-if="loading" />
    <ErrorMessage v-else-if="error" :message="error" @retry="load" />
    <div v-else-if="sessions.length === 0" class="session-browser__empty">
      No sessions yet. Start FakeNode and refresh.
    </div>
    <div v-else class="session-browser__list">
      <SessionCard
        v-for="s in sessions"
        :key="s.sessionId"
        :session="s"
        @click="openSession(s)"
      />
    </div>
  </div>
</template>

<style lang="scss">
.session-browser {
  max-width: 1200px;
  margin: 0 auto;
  padding: 2rem;
  
  &__hint {
    color: var(--c-text-muted);
    margin-bottom: 1.5rem;
  }
  
  &__list {
    display: grid;
    grid-template-columns: repeat(auto-fill, minmax(360px, 1fr));
    gap: 1rem;
  }
  
  &__empty {
    padding: 3rem;
    text-align: center;
    color: var(--c-text-muted);
    background: var(--c-bg-subtle);
    border-radius: 8px;
  }
}
</style>
```

### 6.8 Scenario View — The First User-Facing View

```vue
<!-- src/views/ScenarioView.vue -->
<script setup lang="ts">
import { computed, onMounted, onUnmounted, watch } from 'vue';
import { useSessionStore } from '@/stores/sessionStore';
import { useLiveNotables } from '@/composables/useLiveSse';
import ScenarioStatePanel from '@/components/ScenarioStatePanel.vue';
import PhaseTimeline from '@/components/PhaseTimeline.vue';
import NotableEventsList from '@/components/NotableEventsList.vue';
import LiveIndicator from '@/components/LiveIndicator.vue';
import LoadingSpinner from '@/components/LoadingSpinner.vue';

const props = defineProps<{ sessionId: string }>();
const sessionStore = useSessionStore();

// Initial load
onMounted(() => sessionStore.load(props.sessionId));

// Re-load when session id changes (router navigation)
watch(() => props.sessionId, (sid) => sessionStore.load(sid));

// Periodic refresh of overall state (for counts, elapsed time, etc.)
let refreshTimer: number | null = null;
onMounted(() => {
  refreshTimer = window.setInterval(() => sessionStore.refreshState(), 5000);
});
onUnmounted(() => {
  if (refreshTimer) window.clearInterval(refreshTimer);
});

// SSE for live notable events
const { events: liveEvents } = useLiveNotables(props.sessionId);

const headerTitle = computed(() => {
  const s = sessionStore.current;
  if (!s) return 'Loading session…';
  return s.label ?? `Session ${s.sessionId.slice(0, 8)}`;
});
</script>

<template>
  <div class="scenario-view">
    <header class="scenario-view__header">
      <div>
        <h1>{{ headerTitle }}</h1>
        <p class="scenario-view__subtitle" v-if="sessionStore.current">
          {{ sessionStore.current.scenarioId }}
        </p>
      </div>
      <LiveIndicator />
    </header>

    <LoadingSpinner v-if="sessionStore.loading && !sessionStore.current" />

    <div v-else-if="sessionStore.current" class="scenario-view__grid">
      <ScenarioStatePanel
        class="scenario-view__state"
        :session="sessionStore.current"
        :state="sessionStore.state"
      />

      <PhaseTimeline
        class="scenario-view__phases"
        :session="sessionStore.current"
      />

      <NotableEventsList
        class="scenario-view__notables"
        :session-id="sessionStore.current.sessionId"
        :live-events="liveEvents"
      />
    </div>
  </div>
</template>

<style lang="scss">
.scenario-view {
  max-width: 1400px;
  margin: 0 auto;
  padding: 2rem;

  &__header {
    display: flex;
    justify-content: space-between;
    align-items: center;
    margin-bottom: 1.5rem;
  }

  &__subtitle {
    color: var(--c-text-muted);
    margin: 0.25rem 0 0;
  }

  &__grid {
    display: grid;
    grid-template-columns: 1fr 2fr;
    grid-template-rows: auto 1fr;
    gap: 1.5rem;
    grid-template-areas:
      "state  phases"
      "state  notables";
  }

  &__state    { grid-area: state; }
  &__phases   { grid-area: phases; }
  &__notables { grid-area: notables; }
}
</style>
```

### 6.9 ScenarioStatePanel Component

The "at-a-glance" panel: scenario, current phase, time elapsed, totals.

```vue
<!-- src/components/ScenarioStatePanel.vue -->
<script setup lang="ts">
import { computed } from 'vue';
import type { SessionDto, ScenarioStateDto } from '@/api/tracerApiClient';
import { formatDuration, formatTime } from '@/utils/time';

const props = defineProps<{
  session: SessionDto;
  state: ScenarioStateDto | null;
}>();

const elapsedDisplay = computed(() => {
  if (!props.state) return '—';
  return formatDuration(props.state.sessionElapsed);
});

const phaseDisplay = computed(() => props.state?.currentPhase ?? 'unknown');
const statusLabel = computed(() => props.session.status);  // Active / Completed / Inferred
</script>

<template>
  <section class="scenario-state-panel">
    <div class="scenario-state-panel__row">
      <div class="scenario-state-panel__field">
        <div class="scenario-state-panel__label">Status</div>
        <div class="scenario-state-panel__value scenario-state-panel__value--status"
             :class="`scenario-state-panel__value--${statusLabel.toLowerCase()}`">
          {{ statusLabel }}
        </div>
      </div>
      <div class="scenario-state-panel__field">
        <div class="scenario-state-panel__label">Elapsed</div>
        <div class="scenario-state-panel__value">{{ elapsedDisplay }}</div>
      </div>
    </div>

    <div class="scenario-state-panel__field">
      <div class="scenario-state-panel__label">Current phase</div>
      <div class="scenario-state-panel__value scenario-state-panel__value--phase">
        {{ phaseDisplay }}
      </div>
    </div>

    <div class="scenario-state-panel__row">
      <div class="scenario-state-panel__field">
        <div class="scenario-state-panel__label">Events</div>
        <div class="scenario-state-panel__value">
          {{ state?.totalEvents?.toLocaleString() ?? '—' }}
        </div>
      </div>
      <div class="scenario-state-panel__field">
        <div class="scenario-state-panel__label">Notables</div>
        <div class="scenario-state-panel__value">
          {{ state?.totalNotables?.toLocaleString() ?? '—' }}
        </div>
      </div>
    </div>

    <div class="scenario-state-panel__field">
      <div class="scenario-state-panel__label">Nodes ({{ session.participatingNodes.length }})</div>
      <div class="scenario-state-panel__nodes">
        <span
          v-for="node in session.participatingNodes"
          :key="node"
          class="scenario-state-panel__node"
        >
          {{ node }}
        </span>
      </div>
    </div>
  </section>
</template>

<style lang="scss">
.scenario-state-panel {
  background: var(--c-bg-surface);
  border-radius: 12px;
  padding: 1.5rem;
  display: flex;
  flex-direction: column;
  gap: 1rem;
  
  &__row {
    display: grid;
    grid-template-columns: 1fr 1fr;
    gap: 1rem;
  }

  &__label {
    font-size: 0.75rem;
    color: var(--c-text-muted);
    text-transform: uppercase;
    letter-spacing: 0.05em;
    margin-bottom: 0.25rem;
  }

  &__value {
    font-size: 1.5rem;
    font-weight: 500;

    &--status {
      &.scenario-state-panel__value--active   { color: var(--c-success); }
      &.scenario-state-panel__value--completed { color: var(--c-text); }
      &.scenario-state-panel__value--inferred { color: var(--c-text-muted); }
    }
    
    &--phase {
      color: var(--c-accent);
    }
  }
  
  &__nodes {
    display: flex;
    flex-wrap: wrap;
    gap: 0.5rem;
  }
  
  &__node {
    padding: 0.25rem 0.5rem;
    background: var(--c-bg-subtle);
    border-radius: 4px;
    font-size: 0.875rem;
    font-family: var(--font-mono);
  }
}
</style>
```

### 6.10 NotableEventsList Component

Live-updating stream of notable events. Newest at top. Click to view detail.

```vue
<!-- src/components/NotableEventsList.vue -->
<script setup lang="ts">
import { computed, ref, watch } from 'vue';
import { useApi } from '@/api/useApi';
import type { NotableEventDto } from '@/api/tracerApiClient';
import NotableEventCard from './NotableEventCard.vue';

const props = defineProps<{
  sessionId: string;
  liveEvents: NotableEventDto[];   // from useLiveNotables composable
}>();

const initialEvents = ref<NotableEventDto[]>([]);
const loading = ref(false);

// Load initial page on mount and when session changes
const loadInitial = async () => {
  loading.value = true;
  try {
    const api = useApi();
    initialEvents.value = await api.getScenarioNotables(props.sessionId, 100);
  } finally {
    loading.value = false;
  }
};
watch(() => props.sessionId, loadInitial, { immediate: true });

// Merged + deduplicated view: live events ahead of initial, deduped by eventId
const allEvents = computed(() => {
  const seen = new Set<string>();
  const merged: NotableEventDto[] = [];
  for (const ev of props.liveEvents) {
    if (seen.has(ev.eventId)) continue;
    seen.add(ev.eventId);
    merged.push(ev);
  }
  for (const ev of initialEvents.value) {
    if (seen.has(ev.eventId)) continue;
    seen.add(ev.eventId);
    merged.push(ev);
  }
  return merged;
});
</script>

<template>
  <section class="notables-list">
    <header class="notables-list__header">
      <h2>Notable events</h2>
      <span class="notables-list__count">{{ allEvents.length }}</span>
    </header>

    <div v-if="loading && allEvents.length === 0" class="notables-list__loading">
      Loading…
    </div>
    <div v-else-if="allEvents.length === 0" class="notables-list__empty">
      No notable events yet.
    </div>
    <TransitionGroup v-else name="notable" tag="div" class="notables-list__items">
      <NotableEventCard
        v-for="ev in allEvents"
        :key="ev.eventId"
        :event="ev"
      />
    </TransitionGroup>
  </section>
</template>

<style lang="scss">
.notables-list {
  background: var(--c-bg-surface);
  border-radius: 12px;
  padding: 1.5rem;
  display: flex;
  flex-direction: column;
  
  &__header {
    display: flex;
    align-items: center;
    gap: 1rem;
    margin-bottom: 1rem;
    
    h2 {
      margin: 0;
      font-size: 1.125rem;
    }
  }
  
  &__count {
    padding: 0.125rem 0.5rem;
    background: var(--c-bg-subtle);
    border-radius: 999px;
    font-size: 0.875rem;
    color: var(--c-text-muted);
  }
  
  &__items {
    display: flex;
    flex-direction: column;
    gap: 0.5rem;
    overflow-y: auto;
    max-height: 70vh;
  }
}

// Transitions for live updates
.notable-enter-active { transition: all 250ms ease; }
.notable-enter-from   { opacity: 0; transform: translateY(-10px); }
</style>
```

### 6.11 LiveIndicator Component

Green/red dot showing live connection state.

```vue
<!-- src/components/LiveIndicator.vue -->
<script setup lang="ts">
import { computed } from 'vue';
import { useLiveStore } from '@/stores/liveStore';

const liveStore = useLiveStore();
const stale = computed(() => {
  if (!liveStore.connection.lastEventAt) return false;
  return Date.now() - liveStore.connection.lastEventAt.getTime() > 30_000;
});
const status = computed(() => {
  if (!liveStore.connection.connected) return 'disconnected';
  if (stale.value) return 'stale';
  return 'live';
});
</script>

<template>
  <div class="live-indicator" :class="`live-indicator--${status}`">
    <span class="live-indicator__dot" />
    <span class="live-indicator__label">
      {{ status === 'live' ? 'Live' : status === 'stale' ? 'Quiet' : 'Disconnected' }}
    </span>
  </div>
</template>

<style lang="scss">
.live-indicator {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  padding: 0.25rem 0.75rem;
  border-radius: 999px;
  background: var(--c-bg-subtle);
  font-size: 0.875rem;
  
  &__dot {
    width: 8px;
    height: 8px;
    border-radius: 50%;
  }
  
  &--live .live-indicator__dot {
    background: var(--c-success);
    animation: pulse 2s infinite;
  }
  &--stale .live-indicator__dot { background: var(--c-warning); }
  &--disconnected .live-indicator__dot { background: var(--c-danger); }
}

@keyframes pulse {
  0%, 100% { opacity: 1; }
  50% { opacity: 0.4; }
}
</style>
```

### 6.12 Color Tokens

A minimal design tokens file. Phase 5 may expand significantly.

```scss
// src/styles/tokens.scss
:root {
  // Backgrounds
  --c-bg: #0e1015;
  --c-bg-surface: #181b22;
  --c-bg-subtle: #21242c;

  // Text
  --c-text: #e4e6eb;
  --c-text-muted: #8a91a0;

  // Accents
  --c-accent: #5b9dff;
  --c-success: #4ec97a;
  --c-warning: #e8b048;
  --c-danger:  #e85c5c;

  // Fonts
  --font-sans: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif;
  --font-mono: "JetBrains Mono", "Consolas", "Cascadia Mono", monospace;
}
```

Dark theme by default — consistent with the engineer-tool aesthetic. Phase 8 may add a theme switcher.

---

## 7. End-to-End Demo Path

The Phase 3 demo workflow proves all the pieces work together. This is the success-criteria smoke test.

### 7.1 Two-Process Demo

```
Terminal 1:
  > tracer-fakenode.exe --config fakenode.json
  LOG_FILE=C:/Tracer/fakenode/logs/tracer-fakenode.json
  [info] Starting CombatEngagement scenario, 30 min, seed=42
  [info] Producing events; agent capturing...

Terminal 2:
  > tracer-observer.exe --config observer.json
  LOG_FILE=C:/Tracer/observer/logs/tracer-observer.json
  [info] Observer ingestion starting with 1 source(s)
  [info] HTTP listening on http://localhost:5300
```

For the observer to actually receive FakeNode's events in this multi-process setup, the FakeNode needs to forward its records somewhere the observer can subscribe to. Two options:

- **A) FakeNode is the observer**: configure FakeNode's `observer.json`-like settings to also start a Web API. The Observer and FakeNode run as one process. This is the simplest Phase 3 demo.
- **B) TCP bridge**: a small transport that the FakeNode publishes to and the Observer subscribes to. Adds complexity but proves multi-process design.

**Phase 3 ships with option A as the default demo**: a single process running FakeNode + Observer + Web API. Multi-process via TCP bridge is a Phase 11 concern. The architecture supports both; we just don't need the complexity yet.

In option A, the runnable is `tracer-fakenode.exe` with an extended config:

```json
{
  "FakeNode": {
    "ScenarioName": "CombatEngagement",
    "ScenarioConfig": { /* ... */ },
    "AgentConfig": { /* ... */ },
    "Observer": {
      "Enabled": true,
      "DataRoot": "C:/Tracer/observer-data",
      "LogsRoot": "C:/Tracer/observer-logs",
      "HttpPort": 5300,
      "IntervalDuration": "00:15:00"
    }
  }
}
```

When `Observer.Enabled` is true, FakeNode hosts both the agent stack AND the Observer stack in one process. They share an in-process transport. The Web API serves on the configured port.

### 7.2 Browser Demo

1. Open `http://localhost:5300` → Vue SPA loads
2. Auto-redirects to `/sessions` → Session Browser
3. After ~2 seconds, a session card appears for the scenario being played
4. Click the card → `/scenario/{sessionId}` opens
5. Scenario View renders with:
   - Status: Active
   - Current phase: "approach" / "engagement" / "withdrawal" as the scenario progresses
   - Live indicator: green and pulsing
   - Notable events streaming in as the scenario plays
6. Refreshing the page reconnects SSE; state is preserved

If you can perform this demo end-to-end, Phase 3 is functionally complete.

---

## 8. Test Plan for Phase 3

### 8.1 Backend Unit Tests

**Observer/ObserverIngestionTests.cs**
- Records from data source are written via `IntervalRotator.CurrentWriter`
- Events published to `LiveEventBroadcaster`
- Slow state written to slow_state.duckdb but NOT broadcast
- Fast state written via `AppendFastStateAsync` to Parquet
- Cancellation propagates cleanly through `RunAsync` → all sources stop
- Individual record write failures don't crash the pipeline (drop counter increments, ingestion continues)

**Observer/ObserverStateReporterTests.cs**
- `IncrementIngested` updates total, rolling-minute counter, and last-event timestamp
- `IncrementDropped` updates dropped total only (not ingested counters)
- `Snapshot()` returns immutable record reflecting current state
- `RollingCounter` returns 0 when window has elapsed since last increment
- `RollingCounter` correctly sums multiple buckets within window

**Observer/ReadOnlyConnectionPoolTests.cs**
- `InitializeAsync` opens `_poolSize` connections against the target file
- `AcquireAsync` returns connections in FIFO order
- `PooledConnection.DisposeAsync` returns connection to pool
- `OnIntervalRotatedAsync` switches pool to new path; existing borrowed connections dispose on return rather than returning to the new pool
- `DisposeAsync` closes all connections cleanly
- Acquire after dispose throws `ObjectDisposedException`

**Observer/ObserverHostedServiceTests.cs**
- On start: recovery runs, current interval opens, pool initializes, ingestion + retention + rotation loops start
- On scheduled rotation: rotator rotates, then pool refreshes to new active DB path
- On graceful shutdown: ingestion drains, final rotation runs with `GracefulShutdown` reason
- Pool refresh failure after rotation is logged but doesn't crash the host

**WebApi/EventEndpointTests.cs**
- `GET /api/events/{eventId}` with valid 16-char hex returns 200 with `EventDto`
- `GET /api/events/{eventId}` with unknown id returns 404
- `GET /api/events/{eventId}` with non-hex or wrong-length id returns 400 ProblemDetails

**WebApi/SessionEndpointTests.cs**
- `GET /api/sessions` returns empty array when no session-start events exist
- `GET /api/sessions` returns sessions in descending `started_at` order
- Active sessions (session-start with no matching session-end) have `Status = Active`
- Completed sessions (matched session-end) have `Status = Completed` and `EndUtc` set
- Time-range filter (`from`, `to`) applied to session-start time, not session contents
- `GET /api/sessions/{id}` returns 404 for unknown sessionId
- `EventCount` and `ParticipatingNodes` reflect events within session's time range

**WebApi/ScenarioEndpointTests.cs**
- `GET /api/scenario/notables` returns events with non-null `notable_label`, ordered by time desc
- `GET /api/scenario/notables?before=...` pages backward from given timestamp
- `GET /api/scenario/notables?limit=600` returns 400 (out of bounds)
- `GET /api/scenario/phases` pairs `scenario.phase_started` and `scenario.phase_ended` events correctly; unpaired phase_started results in `Status = Active`
- `GET /api/scenario/state` reflects current scenario phase (latest phase_started without matching phase_ended)

**WebApi/SseEndpointTests.cs**
- SSE endpoint returns 200 with `Content-Type: text/event-stream`
- Client receives `: keepalive` heartbeat within configured interval
- New EventRecord with non-null notable_label appears on stream within 100ms of broadcast
- `SseConnection`'s bounded outbox drops oldest events when client is slow; drop counter increments
- Client disconnect (RequestAborted) calls `Deregister` and closes the connection's outbox
- When `MaxConcurrentSseClients` reached, new connections receive 503

**WebApi/LiveStatusTests.cs**
- `GET /api/live/status` returns DTO with current counters from `ObserverStateReporter`
- `IngestionHealthy = true` when `LastEventUtc` within 60s of now
- `IngestionHealthy = false` when no events in last 60s or `LastEventUtc` is null
- `ActiveSseClients` matches `SseConnectionManager.ActiveCount`

**WebApi/DtoMappingTests.cs**
- `EventRecord` → `EventDto`: every field maps correctly; `TraceId` and `EventId` formatted as 16-char uppercase hex
- `EventRecord` → `NotableEventDto`: drops fields not relevant to notables view (subscriber_node, sequence_number); includes label
- Severity enum serializes as title-case string (`"Info"`, `"Warning"`, `"Error"`)
- Null nullable fields render as missing keys in JSON, not as `null` literals or empty strings
- `DateTimeOffset` round-trips through ISO 8601 with UTC offset

### 8.2 Backend Integration Tests

**ObserverFakeNodeEndToEndTests.cs**
- Start observer fixture configured with mock data source running `CombatEngagement` (Phase 1 scenario)
- Within X simulated minutes, scenario produces session-start, phases, and notables
- `GET /api/sessions` returns the active session
- `GET /api/scenario/notables?sessionId=...` returns the scenario's notable events
- `GET /api/scenario/phases?sessionId=...` returns active phase

**ObserverRotationIntegrationTests.cs**
- Start fixture with 1-minute interval duration (via `SimulatedClock`)
- Push 100 events, advance clock past boundary, push 100 more
- Verify: first interval has 100 events with `_ready`, second interval is current with 100 events
- Verify: connection pool was refreshed (queries return events from current interval, not first)
- Verify: queries during the rotation moment block briefly but succeed

**WebApiQueryRoundTripTests.cs**
- Push known `EventRecord` instances into observer's writer (via fixture's `PushAsync`)
- Query each endpoint, compare DTOs to expected shapes
- Verify URL-shareable filters work (passing same parameters returns same results)

**LiveStreamingTests.cs**
- Subscribe to `/api/live/notables?sessionId=...` with `HttpClient.GetStreamAsync`
- Push notable events through ingestion
- Verify they arrive on the SSE stream in order
- Disconnect, reconnect — verify reconnect succeeds and new events arrive
- Verify slow-client drop behavior: subscribe with artificial delay, push faster than client reads, verify drops counted but stream remains alive

### 8.3 Frontend Unit Tests (Vitest)

```typescript
// tests/unit/ScenarioStatePanel.spec.ts
import { mount } from '@vue/test-utils';
import ScenarioStatePanel from '@/components/ScenarioStatePanel.vue';

describe('ScenarioStatePanel', () => {
  it('shows session status', () => {
    const wrapper = mount(ScenarioStatePanel, {
      props: {
        session: makeMockSession({ status: 'Active' }),
        state: makeMockState({ currentPhase: 'engagement' }),
      },
    });
    expect(wrapper.text()).toContain('Active');
    expect(wrapper.text()).toContain('engagement');
  });

  it('renders dash when state is null', () => {
    const wrapper = mount(ScenarioStatePanel, {
      props: { session: makeMockSession(), state: null },
    });
    expect(wrapper.text()).toContain('—');
  });
});
```

Similar shape for:
- `NotableEventsList.spec.ts`: merges initial + live events, dedupes by eventId
- `LiveIndicator.spec.ts`: shows correct state for connected/stale/disconnected
- `useLiveSse.spec.ts`: mocks fetchEventSource, verifies event handling

### 8.4 E2E Tests (Playwright)

```typescript
// tests/e2e/scenario-view.spec.ts
import { test, expect } from '@playwright/test';

test('full demo path', async ({ page }) => {
  // Assumes a FakeNode+Observer is running on localhost:5300
  await page.goto('http://localhost:5300/');
  
  // Auto-redirects to /sessions
  await expect(page).toHaveURL(/\/sessions$/);
  
  // At least one session card appears within 10 seconds
  const card = page.locator('.session-card').first();
  await expect(card).toBeVisible({ timeout: 10_000 });
  
  // Click into the session
  await card.click();
  await expect(page).toHaveURL(/\/scenario\//);
  
  // Scenario view renders
  await expect(page.locator('.scenario-state-panel')).toBeVisible();
  await expect(page.locator('.live-indicator--live')).toBeVisible({ timeout: 5_000 });
  
  // Notable events appear
  await expect(page.locator('.notable-event-card')).toHaveCount.greaterThan(0, { timeout: 30_000 });
});
```

Playwright tests run against a real FakeNode+Observer. They're not part of the fast CI suite; run on a separate workflow that spins up the FakeNode for them.

### 8.5 Performance Tests

- Observer ingesting 1000 events/sec while serving 10 concurrent SSE clients: zero drops, p99 SSE latency < 50ms
- `GET /api/sessions` over a session with 1M events: returns in < 300ms
- `GET /api/scenario/notables?limit=100` over the same session: < 200ms

---

## 9. Phase 3 Risks and Mitigations

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| NSwag TypeScript generation flakey on Windows builds | Medium | Medium | Day 1 spike: round-trip one endpoint from .NET to generated TS. Have fallback: hand-written types if NSwag is problematic. |
| SSE behavior differs between Edge, Chrome, Firefox under load | Medium | Low | Test in all three early. `@microsoft/fetch-event-source` smooths out differences vs raw EventSource. |
| DuckDB JSON_EXTRACT on every event is slow at scale | Medium | Medium | The fields used in JSON_EXTRACT (sessionId, scenarioId) are promotable to columns. Phase 5 will promote if profiling shows hot spots. Phase 3 is small enough to skip. |
| Phase 3 view design feels wrong once real users see it | High | Low | This is fine — Phase 3 is a learning artifact. Iterate based on instructor feedback. The framework supports rework cheaply. |
| Build pipeline complexity (Vue build + .NET build + NSwag) breaks CI | Medium | Medium | Establish a single `build.ps1` that runs all three in order. Document. Test in CI from day 1. |
| Connection pool exhaustion during heavy concurrent queries | Low | Medium | Default pool size 8. If exhausted, requests queue (channel.ReadAsync awaits). Worst case: increase pool. Tests verify under expected load. |
| Cross-origin issues in dev (Vite proxy fragility) | Medium | Low | Document the proxy setup. Production is same-origin so no concerns there. |
| SSE keepalive conflicts with reverse proxy buffering | Low | Medium | We set `X-Accel-Buffering: no`. Document for ops. |

---

## 10. Definition of Done for Phase 3

### Build & Run

- [ ] `tracer-observer.exe` builds clean with `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`
- [ ] `tracer-observer.exe` runs as console application
- [ ] `tracer-observer.exe` runs as Windows service via `sc create`
- [ ] `tracer-fakenode.exe` builds and runs with `Observer.Enabled: true`, producing the all-in-one demo end-to-end
- [ ] Vue build output lands in `Tracer.Observer/wwwroot` and is served from there in production builds
- [ ] OpenAPI document generated at `/swagger` in dev mode; TypeScript client regenerates cleanly on backend build
- [ ] First stdout line of `tracer-observer.exe` is `LOG_FILE=<absolute path>` per the convention

### Observer behavior

- [ ] Startup recovery runs and finalizes any orphaned intervals from a prior session before opening the current interval
- [ ] `IntervalRotator.OpenCurrentAsync` is called before `ObserverIngestionPipeline.RunAsync` starts (no records dropped at startup due to no writer)
- [ ] `ReadOnlyConnectionPool.InitializeAsync` is called after the first interval opens; queries succeed from that point on
- [ ] After scheduled rotation, the pool is refreshed via `OnIntervalRotatedAsync`; queries hit the new active interval, not the previous one
- [ ] Graceful shutdown completes the final rotation with `ManifestFinalizationReason.GracefulShutdown` and writes `_ready`

### User-facing behavior

- [ ] Browser at `http://localhost:5300/` loads the SPA in under 2 seconds (cold cache)
- [ ] Session Browser shows the active session within 5 seconds of FakeNode start
- [ ] Clicking a session opens the Scenario View at `/scenario/{sessionId}`
- [ ] Notable events appear via SSE within 500ms of being captured by the observer
- [ ] Live indicator shows green when receiving events, red when disconnected, "Quiet" when stale
- [ ] Scenario View's elapsed-time and total-event counters update every 5 seconds via state refresh
- [ ] Browser back/forward navigation between Session Browser and Scenario View works
- [ ] Refreshing the Scenario View preserves the selected session and reconnects SSE

### Testing

- [ ] All Phase 1 and Phase 2 tests still pass
- [ ] Backend unit tests pass (target: 40+ test methods covering observer pipeline, state reporter, connection pool, hosted service, endpoints, DTO mapping)
- [ ] Backend integration tests pass (target: 4+ scenarios including the rotation integration test)
- [ ] Frontend unit tests pass (target: 15+ test methods)
- [ ] At least one Playwright E2E test passes locally, demonstrating the full demo path

### Performance (sanity-check at Phase 3 scale, not full §17 targets)

- [ ] Cold-cache SPA load: < 2 seconds
- [ ] `GET /api/sessions` response: < 300 ms for sessions with under 1M events
- [ ] `GET /api/scenario/notables?limit=100`: < 200 ms
- [ ] `GET /api/events/{eventId}`: < 100 ms
- [ ] SSE notable delivery latency (broadcast → client receive): < 100 ms p95
- [ ] Observer ingestion at FakeNode's default rate (~200 events/sec): zero dropped records

### Documentation

- [ ] `README.md` for Tracer project explains how to: build everything, run the demo, run tests, regenerate the TypeScript client
- [ ] Each section header in this document corresponds to a real component in the codebase
- [ ] Configuration file examples (`agent.json`, `observer.json`, `fakenode.json`) are valid and runnable as shipped

---

## 11. Handoff to Phase 4

What Phase 4 inherits from Phase 3:

- **`Tracer.Observer`** — the live observer process exists with interval rotation, pool refresh, recovery, retention, SSE. Phase 4 adds bundle export/import operations on top.
- **`Tracer.WebApi`** — the API surface (sessions, scenario, single event, SSE, health) is in place. Phase 4 adds `/api/bundles/*` endpoints.
- **`ReadOnlyConnectionPool`** — Phase 3 pool is rotation-aware against the *active* interval. Phase 4 / 5 will need pool variants that query *across* multiple intervals (or one pool per attached file).
- **`ObserverHostedService` orchestration pattern** — Phase 4 reuses the same pattern for any bundle-build background tasks.
- **The Vue SPA** — Phase 4 adds bundle management UI (lists, opens, builds) and an "offline mode" indicator.
- **`LocalFileSystemUploadService`** mock-NAS layout from Phase 2 — Phase 4's aggregator reads from this layout. The mock-NAS files produced during Phase 3 demo runs become readable by Phase 4's aggregator.
- **Multi-interval data on disk** — by Phase 3's end, the observer has produced multiple completed intervals. Phase 5 builds queries across them; Phase 4 builds bundle export from them.

What Phase 4 must address that Phase 3 deferred:

- **Cross-interval queries.** Phase 3 queries only the current interval. Phase 4's bundle export must collate multiple intervals; Phase 5's timeline will need the same capability for live observation across rotations.
- **Bundle format and schema versioning.** Defined in architecture §8.3; Phase 4 implements.
- **`TracerAggregator`** as a standalone CLI plus library.
- **The viewer's "offline mode"** — opening a bundle file from local disk instead of connecting to live observer.
- **Self-contained packaging.** Phase 4's deliverable includes a single-folder distribution of the viewer + a local backend that can open bundles without any live cluster.

Phase 3's contribution: **the system is first visible to humans**. End users — even non-technical bystanders — can see scenarios as they unfold. Everything subsequent (timeline, causal trees, entity history, replication latency) is elaboration on top of the same observer + API + viewer architecture.
