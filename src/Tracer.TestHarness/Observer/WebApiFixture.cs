using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Tracer.Agent.Storage;
using Tracer.Observer.Configuration;
using Tracer.Observer.Lifecycle;
using Tracer.Storage.DuckDB.MultiInterval;
using Tracer.WebApi.Endpoints;
using Tracer.WebApi.Lifecycle;
using Tracer.WebApi.Queries;
using Tracer.WebApi.Streaming;

namespace Tracer.TestHarness.Observer;

/// <summary>
/// Lightweight fixture that hosts the WebApi layer in-process for unit-level HTTP tests.
/// Uses a minimal web application with no Observer hosted services or DuckDB.
/// The LiveMultiIntervalReader is NOT initialized — endpoint tests that need real data
/// should use <see cref="ObserverFixture"/> instead.
/// </summary>
public sealed class WebApiFixture : IAsyncDisposable
{
    public HttpClient Client { get; private set; } = null!;
    public LiveEventBroadcaster Broadcaster =>
        _app.Services.GetRequiredService<LiveEventBroadcaster>();
    public SseConnectionManager SseConnections =>
        _app.Services.GetRequiredService<SseConnectionManager>();

    private WebApplication _app = null!;
    private bool _disposed;

    private WebApiFixture() { }

    public static async Task<WebApiFixture> CreateAsync(
        SseStreamingOptions? sseOptions = null,
        CancellationToken ct = default)
    {
        var fixture = new WebApiFixture();

        var builder = WebApplication.CreateBuilder([]);
        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer();

        Tracer.WebApi.OpenApi.OpenApiConfiguration.Configure(builder);

        var streaming = sseOptions ?? new SseStreamingOptions();
        builder.Services.AddSingleton(streaming);
        builder.Services.AddSingleton<SseConnectionManager>();
        builder.Services.AddSingleton<IntervalSetTracker>(sp =>
            new NullIntervalSetTracker(NullLogger<IntervalSetTracker>.Instance));
        builder.Services.AddSingleton<LiveMultiIntervalReader>(sp =>
            new LiveMultiIntervalReader(
                sp.GetRequiredService<IntervalSetTracker>(),
                NullLogger<LiveMultiIntervalReader>.Instance));
        builder.Services.AddSingleton<SessionQueryService>();
        builder.Services.AddSingleton<TopologyQueryService>();
        builder.Services.AddSingleton<ScenarioQueryService>();
        builder.Services.AddSingleton<EventLookupService>();
        builder.Services.AddSingleton<EventQueryService>();
        builder.Services.AddSingleton<EventAggregationService>();
        builder.Services.AddSingleton<TraceQueryService>();
        builder.Services.AddSingleton<ILiveStatusProvider, NoOpLiveStatusProvider>();
        builder.Services.AddSingleton<LiveEventBroadcaster>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<LiveEventBroadcaster>());

        builder.Services.AddSingleton<Tracer.Storage.Parquet.ParquetReader>();
        builder.Services.AddSingleton<Tracer.WebApi.Queries.FastStateFileLocator>();
        builder.Services.AddSingleton<Tracer.WebApi.Queries.EntityDiscoveryService>();
        builder.Services.AddSingleton<Tracer.WebApi.Queries.EntityEventsService>();
        builder.Services.AddSingleton<Tracer.WebApi.Queries.EntitySlowStateService>();
        builder.Services.AddSingleton<Tracer.WebApi.Queries.EntityFastStateService>();

        var app = builder.Build();

        app.UseExceptionHandler(eb =>
            eb.Run(Tracer.WebApi.Errors.ApiExceptionMiddleware.HandleAsync));

        HealthEndpoints.Map(app);
        SessionEndpoints.Map(app);
        TopologyEndpoints.Map(app);
        ScenarioEndpoints.Map(app);
        EventEndpoints.Map(app);
        SseEndpoints.Map(app);
        TraceEndpoints.Map(app);
        Tracer.WebApi.Endpoints.EntityEndpoints.Map(app);

        await app.StartAsync(ct);

        fixture._app = app;
        fixture.Client = app.GetTestClient();

        return fixture;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        Client?.Dispose();
        if (_app is not null)
            await _app.DisposeAsync();
    }

    private sealed class NoOpLiveStatusProvider : ILiveStatusProvider
    {
        public long IngestedTotal => 0;
        public long DroppedTotal => 0;
        public DateTimeOffset? LastEventUtc => null;
    }

    /// <summary>Stub tracker for unit-level tests that never initializes or queries DuckDB.</summary>
    private sealed class NullIntervalSetTracker : IntervalSetTracker
    {
        public NullIntervalSetTracker(ILogger<IntervalSetTracker> logger)
            : base(null!, 0, logger) { }

        public override Task InitializeAsync(CancellationToken ct) => Task.CompletedTask;
        public override Task OnIntervalRotatedAsync(CancellationToken ct) => Task.CompletedTask;
        public override Task OnIntervalEvictedAsync(IntervalDirectory evicted, CancellationToken ct) => Task.CompletedTask;
        public override IntervalSetSnapshot CurrentSnapshot() =>
            new(new System.Collections.Generic.List<IntervalReference>().AsReadOnly());
    }
}

