using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tracer.Observer.Configuration;

namespace Tracer.TestHarness.Observer;

/// <summary>
/// Lightweight fixture that hosts the WebApi layer in-process for unit-level HTTP tests.
/// Uses a minimal web application with no Observer hosted services or DuckDB.
/// </summary>
public sealed class WebApiFixture : IAsyncDisposable
{
    public HttpClient Client { get; private set; } = null!;

    private WebApplication _app = null!;
    private bool _disposed;

    private WebApiFixture() { }

    public static async Task<WebApiFixture> CreateAsync(CancellationToken ct = default)
    {
        var fixture = new WebApiFixture();

        var builder = WebApplication.CreateBuilder([]);
        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer();

        // Register only what the health endpoint needs
        Tracer.WebApi.OpenApi.OpenApiConfiguration.Configure(builder);

        var app = builder.Build();

        app.UseExceptionHandler(eb =>
            eb.Run(Tracer.WebApi.Errors.ApiExceptionMiddleware.HandleAsync));

        Tracer.WebApi.Endpoints.HealthEndpoints.Map(app);

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
}
