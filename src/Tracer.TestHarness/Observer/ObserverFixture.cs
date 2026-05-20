using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tracer.Observer.Configuration;
using Tracer.Observer.Lifecycle;
using Tracer.WebApi.Lifecycle;

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
    public string DataRoot { get; private set; } = null!;
    public ObserverStateReporter StateReporter =>
        App.Services.GetRequiredService<ObserverStateReporter>();
    public ReadOnlyConnectionPool Pool =>
        App.Services.GetRequiredService<ReadOnlyConnectionPool>();

    private string _tempDir = null!;
    private bool _disposed;
    private Task _runTask = Task.CompletedTask;

    private ObserverFixture() { }

    public static async Task<ObserverFixture> CreateAsync(
        ObserverFixtureOptions? options = null,
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

        // Register minimal set of services needed for lifecycle + pool tests
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
        builder.Services.AddSingleton<ReadOnlyConnectionPool>();

        var app = builder.Build();
        fixture.App = app;

        // Open the initial interval and init pool
        var rotator = app.Services.GetRequiredService<Tracer.Agent.Lifecycle.IntervalRotator>();
        await rotator.OpenCurrentAsync(ct);
        var pool = app.Services.GetRequiredService<ReadOnlyConnectionPool>();
        await pool.InitializeAsync(rotator.CurrentDirectory!.EventsDbPath, ct);

        return fixture;
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
            await App.DisposeAsync();
        }

        try { Directory.Delete(_tempDir, recursive: true); }
        catch { /* best effort */ }
    }
}
