using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Serilog;
using Tracer.Adapters.Mock.Storage;
using Tracer.Adapters.Mock.Upload;
using Tracer.Aggregator;
using Tracer.Agent.Configuration;
using Tracer.Agent.Lifecycle;
using Tracer.Agent.Storage;
using Tracer.Agent.Time;
using Tracer.Agent.Upload;
using Tracer.Core.Abstractions;
using Tracer.Core.Time;
using Tracer.Observer.Configuration;
using Tracer.Observer.Lifecycle;
using Tracer.Observer.Sources;
using Tracer.Storage.DuckDB.Parquet;
using Tracer.Storage.DuckDB.MultiInterval;
using Tracer.WebApi.Bundles;
using Tracer.WebApi.Endpoints;
using Tracer.WebApi.Errors;
using Tracer.WebApi.Lifecycle;
using Tracer.WebApi.OpenApi;
using Tracer.WebApi.Queries;
using Tracer.WebApi.Streaming;

namespace Tracer.Observer;

public static class ObserverHostBuilder
{
    public static WebApplication Build(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var builder = WebApplication.CreateBuilder(args);

        // ── Configuration ────────────────────────────────────────────────────
        var configPath = ResolveConfigPath(args);
        builder.Configuration.AddJsonFile(configPath, optional: true);
        builder.Services.Configure<ObserverConfig>(builder.Configuration.GetSection("Observer"));
        builder.Services.AddSingleton(sp =>
        {
            var cfg = sp.GetRequiredService<IOptions<ObserverConfig>>().Value;
            if (!string.IsNullOrWhiteSpace(cfg.DataRoot) || !string.IsNullOrWhiteSpace(cfg.LogsRoot))
                Configuration.ConfigValidation.Validate(cfg);
            return cfg;
        });

        // ── Kestrel ──────────────────────────────────────────────────────────
        builder.WebHost.ConfigureKestrel(opts =>
        {
            var cfg = builder.Configuration.GetSection("Observer").Get<ObserverConfig>();
            var port = cfg?.HttpPort ?? 5300;
            opts.ListenAnyIP(port);
        });

        // ── Serilog ──────────────────────────────────────────────────────────
        builder.Services.AddSerilog((sp, lc) =>
        {
            var config = sp.GetService<ObserverConfig>();
            if (config is not null && !string.IsNullOrWhiteSpace(config.LogsRoot))
            {
                var logFilePath = Path.Combine(config.LogsRoot,
                    $"tracer-observer-{DateTime.UtcNow:yyyy-MM-dd}.json");

                lc.WriteTo.File(
                    new Serilog.Formatting.Compact.CompactJsonFormatter(),
                    logFilePath,
                    rollingInterval: Serilog.RollingInterval.Day,
                    retainedFileCountLimit: 30);

                if (config.LogToConsole)
                    lc.WriteTo.Console(new Serilog.Formatting.Compact.CompactJsonFormatter());
            }
            else
            {
                lc.WriteTo.Console();
            }

            lc.MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
              .MinimumLevel.Override("System", Serilog.Events.LogEventLevel.Warning)
              .MinimumLevel.Debug();
        });

        // ── Core / clock ─────────────────────────────────────────────────────
        builder.Services.AddSingleton<IClock, SystemClock>();

        // ── Schema registry ──────────────────────────────────────────────────
        builder.Services.AddSingleton<IReadOnlyDictionary<string, ParquetTopicSchema>>(
            _ => WellKnownTopicSchemas.ToDictionary());

        // ── Data sources ──────────────────────────────────────────────────────
        builder.Services.AddSingleton<IReadOnlyList<NamedDataSource>>(sp =>
            DataSourceComposition.Build(sp.GetRequiredService<ObserverConfig>()));

        // ── Synthetic AgentConfig (bridges Observer → Agent services) ─────────
        builder.Services.AddSingleton(sp =>
        {
            var obs = sp.GetRequiredService<ObserverConfig>();
            return new AgentConfig
            {
                NodeId = "observer",
                DataRoot = obs.DataRoot,
                LogsRoot = obs.LogsRoot,
                IntervalDuration = obs.IntervalDuration,
                KeepLastNIntervals = obs.KeepLastNIntervals,
                DiskWatermarkPercent = obs.DiskWatermarkPercent,
            };
        });

        // ── Upload (no-op) ───────────────────────────────────────────────────
        builder.Services.AddSingleton<ITelemetryUploadService>(sp =>
        {
            var obs = sp.GetRequiredService<ObserverConfig>();
            var uploadsDir = Path.Combine(obs.DataRoot, "uploads-noop");
            return new LocalFileSystemUploadService(uploadsDir);
        });
        builder.Services.AddSingleton<UploadIntentDispatcher>();

        // ── Lifecycle ─────────────────────────────────────────────────────────
        builder.Services.AddSingleton<IntervalScheduler>();
        builder.Services.AddSingleton<IntervalRotator>();
        builder.Services.AddSingleton<StartupRecoveryService>();
        builder.Services.AddSingleton<IStartupRecovery>(sp =>
            new StartupRecoveryAdapter(sp.GetRequiredService<StartupRecoveryService>()));

        // ── Storage ───────────────────────────────────────────────────────────
        builder.Services.AddSingleton<RetentionManager>(sp =>
        {
            var cfg = sp.GetRequiredService<AgentConfig>();
            var logger = sp.GetRequiredService<ILogger<RetentionManager>>();
            var rm = new RetentionManager(cfg, logger);
            var tracker = sp.GetRequiredService<IntervalSetTracker>();
            rm.SetPreDeletionCallback((dir, ct) => tracker.OnIntervalEvictedAsync(dir, ct));
            return rm;
        });

        // ── Observer services ─────────────────────────────────────────────────
        builder.Services.AddSingleton<ObserverStateReporter>();
        builder.Services.AddSingleton<ILiveStatusProvider>(sp =>
            sp.GetRequiredService<ObserverStateReporter>());
        builder.Services.AddSingleton<ObserverIngestionPipeline>();

        // ── WebApi services ───────────────────────────────────────────────────
        // ── Multi-interval query infrastructure ──────────────────────────────
        builder.Services.AddSingleton<IntervalSetTracker>(sp =>
            new IntervalSetTracker(
                sp.GetRequiredService<IntervalRotator>(),
                sp.GetRequiredService<ObserverConfig>().LiveQueryWindow.CompletedIntervalsToInclude,
                sp.GetRequiredService<ILogger<IntervalSetTracker>>()));
        builder.Services.AddSingleton<LiveMultiIntervalReader>();
        builder.Services.AddSingleton<SessionQueryService>();
        builder.Services.AddSingleton<ScenarioQueryService>();
        builder.Services.AddSingleton<TopologyQueryService>();
        builder.Services.AddSingleton<EventLookupService>();
        builder.Services.AddSingleton<EventQueryService>();
        builder.Services.AddSingleton<EventAggregationService>();
        builder.Services.AddSingleton<TraceQueryService>();

        // ── Live streaming ────────────────────────────────────────────────────
        builder.Services.AddSingleton<SseStreamingOptions>();
        builder.Services.AddSingleton<SseConnectionManager>();
        builder.Services.AddSingleton<LiveEventBroadcaster>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<LiveEventBroadcaster>());

        // ── Bundle services ───────────────────────────────────────────────────
        builder.Services.AddSingleton<BundleCatalog>(sp =>
        {
            var cfg = sp.GetRequiredService<ObserverConfig>();
            var bundlesRoot = string.IsNullOrWhiteSpace(cfg.BundlesRoot)
                ? Path.Combine(cfg.DataRoot, "bundles")
                : cfg.BundlesRoot;
            Directory.CreateDirectory(bundlesRoot);
            return new BundleCatalog(bundlesRoot, sp.GetRequiredService<ILogger<BundleCatalog>>());
        });
        builder.Services.AddSingleton<ITelemetryStorageReader>(sp =>
        {
            var cfg = sp.GetRequiredService<ObserverConfig>();
            var nasRoot = string.IsNullOrWhiteSpace(cfg.NasMockRoot)
                ? Path.Combine(cfg.DataRoot, "nas-mock")
                : cfg.NasMockRoot;
            return new LocalFileSystemStorageReader(nasRoot,
                sp.GetRequiredService<ILogger<LocalFileSystemStorageReader>>());
        });
        builder.Services.AddSingleton<IAggregationOrchestrator>(sp =>
            new AggregationOrchestrator(
                sp.GetRequiredService<ITelemetryStorageReader>(),
                sp.GetRequiredService<ILogger<AggregationOrchestrator>>()));
        builder.Services.AddSingleton<BundleBuildService>();

        // ── Hosted service ────────────────────────────────────────────────────
        builder.Services.AddHostedService<ObserverHostedService>();

        // ── CORS ──────────────────────────────────────────────────────────────
        builder.Services.AddCors(opts =>
            opts.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

        // ── OpenAPI (NSwag) ───────────────────────────────────────────────────
        OpenApiConfiguration.Configure(builder);

        // ── Windows Service ───────────────────────────────────────────────────
        builder.Services.AddWindowsService(o => o.ServiceName = "TracerObserver");

        // ── Build ─────────────────────────────────────────────────────────────
        var app = builder.Build();

        app.UseExceptionHandler(errorApp =>
            errorApp.Run(ApiExceptionMiddleware.HandleAsync));

        app.UseCors();

        if (app.Environment.IsDevelopment())
        {
            app.UseOpenApi();
            app.UseSwaggerUi();
        }

        HealthEndpoints.Map(app);
        SessionEndpoints.Map(app);
        EventEndpoints.Map(app);
        ScenarioEndpoints.Map(app);
        TopologyEndpoints.Map(app);
        SseEndpoints.Map(app);
        BundleEndpoints.Map(app);
        TraceEndpoints.Map(app);

        // ── SPA static files (if present) ─────────────────────────────────────
        var spaPath = Path.Combine(app.Environment.ContentRootPath, "wwwroot");
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

        return app;
    }

    private static string ResolveConfigPath(string[] args)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] is "--config" or "-c")
                return args[i + 1];
        }
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Tracer", "observer", "config.json");
    }
}

/// <summary>
/// Adapts <see cref="Tracer.Agent.Lifecycle.StartupRecoveryService"/> to
/// the <see cref="Lifecycle.IStartupRecovery"/> interface.
/// </summary>
internal sealed class StartupRecoveryAdapter : Lifecycle.IStartupRecovery
{
    private readonly Tracer.Agent.Lifecycle.StartupRecoveryService _inner;

    public StartupRecoveryAdapter(Tracer.Agent.Lifecycle.StartupRecoveryService inner)
        => _inner = inner;

    public Task RecoverAsync(CancellationToken ct)
        => _inner.RecoverAsync(ct);
}
