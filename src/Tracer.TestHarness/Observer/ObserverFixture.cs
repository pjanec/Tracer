using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tracer.Core.Records;
using Tracer.Observer.Configuration;
using Tracer.Observer.Lifecycle;
using Tracer.WebApi.Endpoints;
using Tracer.WebApi.Lifecycle;
using Tracer.WebApi.Queries;
using Tracer.WebApi.Streaming;

namespace Tracer.TestHarness.Observer;

/// <summary>
/// Options for <see cref="ObserverFixture"/>.
/// </summary>
public sealed class ObserverFixtureOptions
{
    public TimeSpan IntervalDuration { get; set; } = TimeSpan.FromMinutes(1);
    public int HttpPort { get; set; } = 0;
}

/// <summary>
/// Hosts a full Tracer Observer over a temporary data directory.
/// Exposes the <see cref="App"/> and helpers for pushing records
/// and forcing rotations.
/// </summary>
public sealed class ObserverFixture : IAsyncDisposable
{
    public WebApplication App { get; private set; } = null!;
    public HttpClient Client { get; private set; } = null!;
    public string DataRoot { get; private set; } = null!;
    public ObserverStateReporter StateReporter =>
        App.Services.GetRequiredService<ObserverStateReporter>();
    public ReadOnlyConnectionPool Pool =>
        App.Services.GetRequiredService<ReadOnlyConnectionPool>();
    public SseConnectionManager SseConnections =>
        App.Services.GetRequiredService<SseConnectionManager>();
    public LiveEventBroadcaster Broadcaster =>
        App.Services.GetRequiredService<LiveEventBroadcaster>();

    private string _tempDir = null!;
    private bool _disposed;
    private Task _runTask = Task.CompletedTask;

    private ObserverFixture() { }

    public static async Task<ObserverFixture> CreateAsync(
        ObserverFixtureOptions? options = null,
        SseStreamingOptions? sseOptions = null,
        CancellationToken ct = default)
    {
        options ??= new ObserverFixtureOptions();

        var fixture = new ObserverFixture();
        fixture._tempDir = Path.Combine(Path.GetTempPath(), $"tracer-obs-{Guid.NewGuid():N}");
        fixture.DataRoot = fixture._tempDir;

        Directory.CreateDirectory(fixture._tempDir);
        var logsRoot = Path.Combine(fixture._tempDir, "logs");
        Directory.CreateDirectory(logsRoot);

        var builder = WebApplication.CreateBuilder([]);
        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer();

        Tracer.WebApi.OpenApi.OpenApiConfiguration.Configure(builder);

        // Override config
        builder.Services.AddSingleton(new ObserverConfig
        {
            DataRoot = fixture._tempDir,
            LogsRoot = logsRoot,
            HttpPort = options.HttpPort,
            IntervalDuration = options.IntervalDuration,
            KeepLastNIntervals = 4,
            DiskWatermarkPercent = 10,
            DataSources = new DataSourcesConfig { Kind = "Mock" }
        });

        // Core services
        builder.Services.AddSingleton<Tracer.Core.Time.IClock, Tracer.Agent.Time.SystemClock>();
        builder.Services.AddSingleton(sp =>
        {
            var obs = sp.GetRequiredService<ObserverConfig>();
            return new Tracer.Agent.Configuration.AgentConfig
            {
                NodeId = "observer-test",
                DataRoot = obs.DataRoot,
                LogsRoot = obs.LogsRoot,
                IntervalDuration = obs.IntervalDuration,
                KeepLastNIntervals = obs.KeepLastNIntervals,
                DiskWatermarkPercent = obs.DiskWatermarkPercent,
            };
        });
        builder.Services.AddSingleton<Tracer.Core.Abstractions.ITelemetryUploadService>(sp =>
            new Tracer.Adapters.Mock.Upload.LocalFileSystemUploadService(
                Path.Combine(fixture._tempDir, "uploads-noop")));
        builder.Services.AddSingleton<Tracer.Agent.Upload.UploadIntentDispatcher>();
        builder.Services.AddSingleton<Tracer.Agent.Lifecycle.IntervalScheduler>();
        builder.Services.AddSingleton<Tracer.Agent.Lifecycle.IntervalRotator>();
        builder.Services.AddSingleton<Tracer.Agent.Storage.RetentionManager>();
        builder.Services.AddSingleton<ObserverStateReporter>();
        builder.Services.AddSingleton<ILiveStatusProvider>(sp =>
            sp.GetRequiredService<ObserverStateReporter>());
        builder.Services.AddSingleton<ReadOnlyConnectionPool>();

        // Query services
        builder.Services.AddSingleton<SessionQueryService>();
        builder.Services.AddSingleton<TopologyQueryService>();
        builder.Services.AddSingleton<ScenarioQueryService>();
        builder.Services.AddSingleton<EventLookupService>();

        // SSE services
        var streaming = sseOptions ?? new SseStreamingOptions();
        builder.Services.AddSingleton(streaming);
        builder.Services.AddSingleton<SseConnectionManager>();
        builder.Services.AddSingleton<LiveEventBroadcaster>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<LiveEventBroadcaster>());

        var app = builder.Build();

        app.UseExceptionHandler(eb =>
            eb.Run(Tracer.WebApi.Errors.ApiExceptionMiddleware.HandleAsync));

        HealthEndpoints.Map(app);
        SessionEndpoints.Map(app);
        TopologyEndpoints.Map(app);
        ScenarioEndpoints.Map(app);
        EventEndpoints.Map(app);
        SseEndpoints.Map(app);

        fixture.App = app;

        // Open the initial interval and init pool
        var rotator = app.Services.GetRequiredService<Tracer.Agent.Lifecycle.IntervalRotator>();
        await rotator.OpenCurrentAsync(ct);
        var pool = app.Services.GetRequiredService<ReadOnlyConnectionPool>();
        await pool.InitializeAsync(rotator.CurrentDirectory!.EventsDbPath, ct);

        await app.StartAsync(ct);
        fixture.Client = app.GetTestClient();

        return fixture;
    }

    /// <summary>Push a single event directly into the current interval's storage.</summary>
    public async Task PushAsync(EventRecord ev, CancellationToken ct = default)
    {
        var rotator = App.Services.GetRequiredService<Tracer.Agent.Lifecycle.IntervalRotator>();
        await rotator.CurrentWriter!.AppendEventAsync(ev, ct);
        await rotator.CurrentWriter.FlushAsync(ct);

        var broadcaster = App.Services.GetRequiredService<LiveEventBroadcaster>();
        broadcaster.Publish(ev);

        var stateReporter = App.Services.GetRequiredService<ObserverStateReporter>();
        stateReporter.IncrementIngested();
    }

    /// <summary>Push multiple events directly into the current interval's storage.</summary>
    public async Task PushAsync(IEnumerable<EventRecord> events, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(events);
        var rotator = App.Services.GetRequiredService<Tracer.Agent.Lifecycle.IntervalRotator>();
        var stateReporter = App.Services.GetRequiredService<ObserverStateReporter>();
        var broadcaster = App.Services.GetRequiredService<LiveEventBroadcaster>();
        foreach (var ev in events)
        {
            await rotator.CurrentWriter!.AppendEventAsync(ev, ct);
            broadcaster.Publish(ev);
            stateReporter.IncrementIngested();
        }
        await rotator.CurrentWriter!.FlushAsync(ct);
    }

    public async Task ForceRotationAsync(CancellationToken ct = default)
    {
        var rotator = App.Services.GetRequiredService<Tracer.Agent.Lifecycle.IntervalRotator>();
        var pool = Pool;
        await rotator.RotateAsync(
            Tracer.Core.Domain.ManifestFinalizationReason.ScheduledRotation, ct);
        if (rotator.CurrentDirectory is not null)
            await pool.OnIntervalRotatedAsync(rotator.CurrentDirectory.EventsDbPath, ct);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        if (App is not null)
        {
            await App.StopAsync();
            await App.DisposeAsync();
        }

        try { Directory.Delete(_tempDir, recursive: true); }
        catch { /* best effort */ }
    }
}

