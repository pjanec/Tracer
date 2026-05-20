using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Tracer.Observer.Lifecycle;
using Tracer.OfflineViewer.Lifecycle;
using Tracer.OfflineViewer.WebApi;
using Tracer.WebApi.Endpoints;
using Tracer.WebApi.Errors;
using Tracer.WebApi.Lifecycle;
using Tracer.WebApi.OpenApi;
using Tracer.WebApi.Queries;
using Tracer.WebApi.Streaming;

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
        builder.Services.AddSingleton<BundleOpenManager>();

        // Connection pool — same class as Observer uses
        builder.Services.AddSingleton<ReadOnlyConnectionPool>();

        // Query services — same classes as Observer uses
        builder.Services.AddSingleton<SessionQueryService>();
        builder.Services.AddSingleton<ScenarioQueryService>();
        builder.Services.AddSingleton<TopologyQueryService>();
        builder.Services.AddSingleton<EventLookupService>();

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
}
