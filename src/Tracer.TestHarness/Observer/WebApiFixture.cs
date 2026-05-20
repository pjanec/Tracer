using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tracer.Observer.Configuration;
using Tracer.WebApi.Endpoints;
using Tracer.WebApi.Lifecycle;
using Tracer.WebApi.Queries;
using Tracer.WebApi.Streaming;

namespace Tracer.TestHarness.Observer;

/// <summary>
/// Lightweight fixture that hosts the WebApi layer in-process for unit-level HTTP tests.
/// Uses a minimal web application with no Observer hosted services or DuckDB.
/// The ReadOnlyConnectionPool is NOT initialized — endpoint tests that need real data
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
        builder.Services.AddSingleton<ReadOnlyConnectionPool>();
        builder.Services.AddSingleton<SessionQueryService>();
        builder.Services.AddSingleton<TopologyQueryService>();
        builder.Services.AddSingleton<ScenarioQueryService>();
        builder.Services.AddSingleton<EventLookupService>();
        builder.Services.AddSingleton<ILiveStatusProvider, NoOpLiveStatusProvider>();
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
}

