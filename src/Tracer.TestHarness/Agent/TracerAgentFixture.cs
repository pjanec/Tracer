using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tracer.Adapters.Mock;
using Tracer.Adapters.Mock.Transport;
using Tracer.Adapters.Mock.Upload;
using Tracer.Agent.Configuration;
using Tracer.Agent.Diagnostics;
using Tracer.Agent.Ingestion;
using Tracer.Agent.Lifecycle;
using Tracer.Agent.Storage;
using Tracer.Agent.Time;
using Tracer.Agent.Upload;
using Tracer.Core.Abstractions;
using Tracer.Core.Domain;
using Tracer.Core.Records;
using Tracer.Core.Time;
using Tracer.Storage.DuckDB.Parquet;

namespace Tracer.TestHarness;

/// <summary>
/// Runs a full agent in-process with mock transport and upload service.
/// Provides helpers to push records, force rotation, and inspect results.
/// </summary>
public sealed class TracerAgentFixture : IAsyncDisposable
{
    private readonly IHost _host;
    private readonly string _dataRoot;
    private readonly string _logsRoot;
    private readonly string _uploadRoot;
    private bool _disposed;

    /// <summary>The underlying in-process channel transport.</summary>
    public InProcessChannelTransport Transport { get; }

    /// <summary>The local-filesystem upload service used by the agent.</summary>
    public LocalFileSystemUploadService UploadService { get; }

    /// <summary>The temp directory used as the agent's DataRoot.</summary>
    public string DataRoot => _dataRoot;

    /// <summary>The temp directory used for upload staging (zip files are written here).</summary>
    public string UploadRoot => _uploadRoot;

    /// <summary>
    /// Non-null when <see cref="AgentFixtureOptions.UseSimulatedClock"/> was <c>true</c>.
    /// </summary>
    public SimulatedClock? SimulatedClock { get; }

    private TracerAgentFixture(
        IHost host,
        string dataRoot,
        string logsRoot,
        string uploadRoot,
        InProcessChannelTransport transport,
        LocalFileSystemUploadService uploadService,
        SimulatedClock? simulatedClock)
    {
        _host = host;
        _dataRoot = dataRoot;
        _logsRoot = logsRoot;
        _uploadRoot = uploadRoot;
        Transport = transport;
        UploadService = uploadService;
        SimulatedClock = simulatedClock;
    }

    /// <summary>Creates and starts the fixture. Caller must <c>await using</c> the result.</summary>
    public static async Task<TracerAgentFixture> CreateAsync(
        AgentFixtureOptions? options = null,
        CancellationToken ct = default)
    {
        options ??= new AgentFixtureOptions();

        var id = Guid.NewGuid().ToString("N")[..8];
        var dataRoot = Path.Combine(Path.GetTempPath(), $"tracer-fixture-{id}-data");
        var logsRoot = Path.Combine(Path.GetTempPath(), $"tracer-fixture-{id}-logs");
        var uploadRoot = Path.Combine(Path.GetTempPath(), $"tracer-fixture-{id}-upload");
        Directory.CreateDirectory(dataRoot);
        Directory.CreateDirectory(logsRoot);
        Directory.CreateDirectory(uploadRoot);

        var agentConfig = new AgentConfig
        {
            NodeId = $"fixture-{id}",
            DataRoot = dataRoot,
            LogsRoot = logsRoot,
            IntervalDuration = TimeSpan.FromHours(1),
            KeepLastNIntervals = options.KeepLastNIntervals,
            Transport = new TransportConfig { CapacityRecords = options.TransportCapacity },
            UploadService = new UploadServiceConfig { LocalFileSystemRoot = uploadRoot },
        };

        var transport = new InProcessChannelTransport(options.TransportCapacity);
        var uploadService = new LocalFileSystemUploadService(uploadRoot);
        SimulatedClock? simulatedClock = options.UseSimulatedClock ? new SimulatedClock() : null;

        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton(agentConfig);

        builder.Services.AddSingleton<InProcessChannelTransport>(transport);
        builder.Services.AddSingleton<IAgentTransport>(transport);
        builder.Services.AddSingleton<ITelemetryUploadService>(uploadService);

        if (simulatedClock is not null)
            builder.Services.AddSingleton<IClock>(simulatedClock);
        else
        {
            builder.Services.AddSingleton(TimeProvider.System);
            builder.Services.AddSingleton<IClock, SystemClock>();
        }

        builder.Services.AddSingleton<IReadOnlyDictionary<string, ParquetTopicSchema>>(
            _ => WellKnownTopicSchemas.ToDictionary());

        builder.Services.AddSingleton<BackpressureMonitor>();
        builder.Services.AddSingleton<DropPolicy>();
        builder.Services.AddSingleton<RecordRouter>();
        builder.Services.AddSingleton<IngestionPipeline>();

        builder.Services.AddSingleton<IntervalScheduler>();
        builder.Services.AddSingleton<UploadIntentDispatcher>();
        builder.Services.AddSingleton<IntervalRotator>();
        builder.Services.AddSingleton<IIntervalContext>(sp => sp.GetRequiredService<IntervalRotator>());
        builder.Services.AddSingleton<StartupRecoveryService>();
        builder.Services.AddSingleton<RetentionManager>();
        builder.Services.AddSingleton<AgentStateReporter>();
        builder.Services.AddSingleton<TransportMonitor>();

        builder.Services.AddHostedService<AgentHostedService>();

        // Suppress noisy log output from tests
        builder.Logging.ClearProviders();

        var host = builder.Build();
        await host.StartAsync(ct);

        return new TracerAgentFixture(host, dataRoot, logsRoot, uploadRoot, transport, uploadService, simulatedClock);
    }

    /// <summary>Pushes a record into the agent's ingestion pipeline.</summary>
    public Task PushAsync(DiagnosticRecord record, CancellationToken ct = default)
        => Transport.WriteAsync(record, ct);

    /// <summary>
    /// Forces an immediate interval rotation (reason = <c>ScheduledRotation</c>).
    /// After the call the previous interval is complete and a new one is open.
    /// </summary>
    public async Task ForceRotationAsync(CancellationToken ct = default)
    {
        var rotator = _host.Services.GetRequiredService<IntervalRotator>();
        await rotator.RotateAsync(ManifestFinalizationReason.ScheduledRotation, ct);
    }

    /// <summary>
    /// Stops the agent gracefully: marks the transport complete then stops the host.
    /// The agent will finalize the current interval with <c>GracefulShutdown</c>.
    /// </summary>
    public async Task StopAsync(CancellationToken ct = default)
    {
        Transport.Complete();
        // Give ingestion pipeline a moment to drain
        await Task.Delay(200, CancellationToken.None);
        await _host.StopAsync(ct);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        try { await StopAsync(CancellationToken.None); } catch { /* best-effort */ }
        _host.Dispose();

        TryDeleteDirectory(_dataRoot);
        TryDeleteDirectory(_logsRoot);
        TryDeleteDirectory(_uploadRoot);
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch { /* best-effort */ }
    }
}
