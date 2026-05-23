using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Tracer.Observer.Lifecycle;
using Tracer.OfflineViewer.Lifecycle;
using Tracer.OfflineViewer.WebApi;
using Tracer.Storage.Annotations;
using Tracer.Storage.SavedViews;
using Tracer.Storage.SavedQueries;
using Tracer.Storage.SavedQueries.BuiltIn;
using Tracer.Storage.DuckDB.MultiInterval;
using Tracer.WebApi.Endpoints;
using Tracer.WebApi.Errors;
using Tracer.WebApi.Lifecycle;
using Tracer.WebApi.OpenApi;
using Tracer.WebApi.Queries;
using Tracer.WebApi.Streaming;
using Tracer.WebApi.Util;

namespace Tracer.OfflineViewer;

public static class OfflineViewerHostBuilder
{
    public static WebApplication Build(string? initialBundlePath)
    {
        var builder = WebApplication.CreateBuilder();

        var config = new OfflineViewerConfig
        {
            HttpPort = FindFreePort(5400, 5499),
            LogFilePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Tracer", "viewer-logs",
                $"tracer-viewer-{DateTime.UtcNow:yyyy-MM-dd}.json"),
            InitialBundlePath = initialBundlePath
        };
        builder.Services.AddSingleton(config);

        ConfigureSerilog(builder, config);

        // Kestrel — localhost only, no external access
        builder.WebHost.ConfigureKestrel((_, options) =>
        {
            options.ListenLocalhost(config.HttpPort);
            options.AddServerHeader = false;
        });

        // OpenAPI (NSwag)
        OpenApiConfiguration.Configure(builder);

        // Bundle management
        builder.Services.AddSingleton<BundleIntervalSetTracker>();
        builder.Services.AddSingleton<BundleOpenManager>();

        // Multi-interval reader backed by the bundle tracker
        builder.Services.AddSingleton<LiveMultiIntervalReader>(sp =>
            new LiveMultiIntervalReader(
                sp.GetRequiredService<BundleIntervalSetTracker>(),
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<LiveMultiIntervalReader>>(),
                poolSize: 4));

        // Query services — same classes as Observer uses
        builder.Services.AddSingleton<SessionQueryService>();
        builder.Services.AddSingleton<ScenarioQueryService>();
        builder.Services.AddSingleton<TopologyQueryService>();
        builder.Services.AddSingleton<EventLookupService>();
        builder.Services.AddSingleton<EventQueryService>();
        builder.Services.AddSingleton<EventAggregationService>();
        builder.Services.AddSingleton<TraceQueryService>();

        // ── Entity history services (Phase 7) ─────────────────────────────────
        builder.Services.AddSingleton<Tracer.Storage.Parquet.ParquetReader>();
        builder.Services.AddSingleton<FastStateFileLocator>(sp =>
            new FastStateFileLocator(
                sp.GetRequiredService<BundleIntervalSetTracker>(),
                () => sp.GetRequiredService<BundleOpenManager>().Current?.WorkingDirectory));
        builder.Services.AddSingleton<EntityDiscoveryService>();
        builder.Services.AddSingleton<EntityEventsService>();
        builder.Services.AddSingleton<EntitySlowStateService>();
        builder.Services.AddSingleton<EntityFastStateService>();

        // ── Annotations (Phase 8) ─────────────────────────────────────────────────
        builder.Services.AddSingleton<IAnnotationStore, LazyBundleAnnotationStore>();

        // ── Saved Views (Phase 8) ─────────────────────────────────────────────────
        builder.Services.AddSingleton<ISavedViewStore, LazyBundleSavedViewStore>();

        // ── Saved Queries (Phase 10) ──────────────────────────────────────────
        builder.Services.AddSingleton<ISavedQueryStore>(sp =>
        {
            var logDir = Path.GetDirectoryName(config.LogFilePath)
                         ?? Path.Combine(
                             Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                             "Tracer", "viewer-data");
            Directory.CreateDirectory(logDir);
            var dbPath = Path.Combine(logDir, "annotations.db");
            return new SqliteSavedQueryStore(dbPath, sp.GetRequiredService<ILogger<SqliteSavedQueryStore>>());
        });

        // ── SQL Console (Phase 10) ────────────────────────────────────────────
        builder.Services.AddSingleton(new SqlExecutorConfig
        {
            DefaultTimeoutSeconds = 30,
            DefaultMaxRows        = 100_000,
            MaxMemoryMb           = 1024,
        });
        builder.Services.AddSingleton<SqlExecutorService>();
        builder.Services.AddSingleton<SqlSchemaService>();
        builder.Services.AddSingleton<ViewSqlTemplateService>();

        // ── Bundle Library (Phase 10) ─────────────────────────────────────────
        builder.Services.AddSingleton(sp =>
        {
            var logDir = Path.GetDirectoryName(config.LogFilePath)
                         ?? Path.Combine(
                             Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                             "Tracer", "viewer-data");
            var bundlesRoot = Path.Combine(logDir, "bundles");
            return new BundleLibraryService(bundlesRoot, sp.GetService<ILogger<BundleLibraryService>>());
        });
        builder.Services.AddSingleton(sp =>
        {
            var logDir = Path.GetDirectoryName(config.LogFilePath)
                         ?? Path.Combine(
                             Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                             "Tracer", "viewer-data");
            return new BundleExportService(Path.Combine(logDir, "bundles"));
        });
        builder.Services.AddSingleton(sp =>
        {
            var logDir = Path.GetDirectoryName(config.LogFilePath)
                         ?? Path.Combine(
                             Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                             "Tracer", "viewer-data");
            return new BundleImportService(
                Path.Combine(logDir, "bundles"),
                sp.GetRequiredService<ILogger<BundleImportService>>());
        });

        // ── Trigger evaluation service (Phase 8) ──────────────────────────────
        builder.Services.AddSingleton<TriggerEvalService>();

        // ── Lifecycle classification (Phase 8) ────────────────────────────────
        builder.Services.AddSingleton(config.LifecycleClassification);
        builder.Services.AddSingleton<ILifecycleTopicClassifier>(
            new ConfigurableLifecycleTopicClassifier(config.LifecycleClassification));

        // ── Phase 9: Bundle mode marker ───────────────────────────────────────
        builder.Services.AddSingleton<IBundleModeMarker>(_ => new BundleModeSentinel());

        // ── Phase 9: Latency / Gap / Topology / Budget services ───────────────
        builder.Services.AddSingleton<LatencyDistributionService>();
        builder.Services.AddSingleton<LatencyTimeSeriesService>();
        builder.Services.AddSingleton<LatencyOutlierService>();
        builder.Services.AddSingleton<GapDetectionService>();
        builder.Services.AddSingleton<NetworkTopologyService>();
        builder.Services.AddSingleton<InMemoryBudgetRegistry>();
        builder.Services.AddSingleton<BudgetService>(sp =>
            new BudgetService(
                getBundleWorkingDirectory: () => sp.GetRequiredService<BundleOpenManager>().Current?.WorkingDirectory,
                registry: sp.GetRequiredService<InMemoryBudgetRegistry>(),
                logger: sp.GetRequiredService<ILogger<BudgetService>>()));

        // Observer state reporter — inert instance (no events in bundle mode)
        builder.Services.AddSingleton<ObserverStateReporter>(_ => new InertObserverStateReporter());
        builder.Services.AddSingleton<ILiveStatusProvider>(sp =>
            sp.GetRequiredService<ObserverStateReporter>());

        // SSE — connection manager only (no broadcaster in offline mode)
        builder.Services.AddSingleton<SseStreamingOptions>();
        builder.Services.AddSingleton<SseConnectionManager>();

        // Hosted service: opens initial bundle on startup
        builder.Services.AddHostedService<OfflineHostedService>();

        var app = builder.Build();

        app.UseExceptionHandler(errorApp =>
            errorApp.Run(ApiExceptionMiddleware.HandleAsync));

        if (app.Environment.IsDevelopment())
        {
            app.UseOpenApi();
            app.UseSwaggerUi();
        }

        // Static files (SPA assets embedded / wwwroot)
        app.UseStaticFiles();

        SessionEndpoints.Map(app);
        ScenarioEndpoints.Map(app);
        TopologyEndpoints.Map(app);
        EventEndpoints.Map(app);
        SseEndpoints.Map(app);
        BundleOpenEndpoints.Map(app);
        TraceEndpoints.Map(app);
        EntityEndpoints.Map(app);
        AnnotationEndpoints.Map(app);
        SavedViewEndpoints.Map(app);
        TriggerEvalEndpoints.Map(app);
        ConfigEndpoints.Map(app);

        // Phase 9 endpoints
        LatencyEndpoints.Map(app);
        GapEndpoints.Map(app);
        BudgetEndpoints.Map(app);

        // Phase 10 endpoints
        SqlEndpoints.Map(app);
        SavedQueriesEndpoints.Map(app);
        BundleLibraryEndpoints.Map(app);

        // Wire schema invalidation on bundle changes
        var bundleTracker = app.Services.GetRequiredService<BundleIntervalSetTracker>();
        var schemaService = app.Services.GetRequiredService<SqlSchemaService>();
        var hostLogger = app.Services.GetRequiredService<ILogger<WebApplication>>();
        bundleTracker.SetChanged += async (_, _) =>
        {
            try
            {
                await schemaService.InvalidateAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                hostLogger.LogError(ex, "Unhandled exception in schema invalidation after bundle set change");
            }
        };

        // Seed built-in queries on startup
        app.Lifetime.ApplicationStarted.Register(() =>
        {
            var store = app.Services.GetRequiredService<ISavedQueryStore>();
            _ = Task.Run(async () =>
            {
                try
                {
                    await BuiltInLoader.EnsureLoadedAsync(store, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    hostLogger.LogError(ex, "Unhandled exception while seeding built-in queries on startup");
                }
            });
        });

        // SPA fallback
        app.MapFallbackToFile("index.html");

        return app;
    }

    private static void ConfigureSerilog(WebApplicationBuilder builder, OfflineViewerConfig config)
    {
        var logDir = Path.GetDirectoryName(config.LogFilePath);
        if (logDir is not null)
            Directory.CreateDirectory(logDir);

        builder.Services.AddSerilog((_, lc) =>
        {
            lc.MinimumLevel.Information()
              .WriteTo.Console()
              .WriteTo.File(
                  new Serilog.Formatting.Compact.CompactJsonFormatter(),
                  config.LogFilePath,
                  rollingInterval: Serilog.RollingInterval.Day,
                  retainedFileCountLimit: 7);
        });
    }

    private static int FindFreePort(int startPort, int endPort)
    {
        for (var port = startPort; port <= endPort; port++)
        {
            try
            {
                using var listener = new System.Net.Sockets.TcpListener(
                    System.Net.IPAddress.Loopback, port);
                listener.Start();
                listener.Stop();
                return port;
            }
            catch (System.Net.Sockets.SocketException)
            {
                // Port in use, try next
            }
        }
        throw new InvalidOperationException(
            $"No free port found in range {startPort}-{endPort}");
    }

    /// <summary>Marker singleton registered only in bundle (OfflineViewer) mode.</summary>
    private sealed class BundleModeSentinel : IBundleModeMarker { }
}
