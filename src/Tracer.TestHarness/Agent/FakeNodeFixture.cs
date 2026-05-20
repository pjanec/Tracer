using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tracer.Adapters.Mock;
using Tracer.Adapters.Mock.Scenarios;
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
using Tracer.Core.Time;
using Tracer.FakeNode;
using Tracer.FakeNode.Configuration;
using Tracer.Storage.DuckDB.Parquet;

namespace Tracer.TestHarness;

/// <summary>
/// Runs a complete FakeNode host in-process for a named scenario.
/// After <see cref="RunScenarioAsync"/> returns the scenario has finished,
/// the agent has finalized all intervals, and <see cref="Manifests"/> and
/// <see cref="IntervalZipPaths"/> are populated.
/// </summary>
public sealed class FakeNodeFixture : IAsyncDisposable
{
    private readonly string _dataRoot;
    private readonly string _uploadRoot;
    private bool _disposed;

    /// <summary>Deserialized manifests from every completed interval.</summary>
    public IReadOnlyList<IntervalManifest> Manifests { get; private set; } = Array.Empty<IntervalManifest>();

    /// <summary>Paths to ZIP archives produced by the local-filesystem upload service.</summary>
    public IReadOnlyList<string> IntervalZipPaths { get; private set; } = Array.Empty<string>();

    private FakeNodeFixture(string dataRoot, string uploadRoot)
    {
        _dataRoot = dataRoot;
        _uploadRoot = uploadRoot;
    }

    /// <summary>
    /// Builds and runs a FakeNode host in-process until the scenario completes.
    /// The caller is responsible for disposing the fixture (which cleans up temp dirs).
    /// </summary>
    public static async Task<FakeNodeFixture> RunScenarioAsync(
        string scenarioName,
        ScenarioConfig scenarioConfig,
        AgentConfig agentConfig,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(agentConfig);
        var uploadRoot = !string.IsNullOrEmpty(agentConfig.UploadService.LocalFileSystemRoot)
            ? agentConfig.UploadService.LocalFileSystemRoot
            : Path.Combine(agentConfig.DataRoot, "_upload_staging");

        Directory.CreateDirectory(agentConfig.DataRoot);
        Directory.CreateDirectory(uploadRoot);

        var fakeNodeConfig = new FakeNodeConfig
        {
            ScenarioName = scenarioName,
            ScenarioConfig = scenarioConfig,
            AgentConfig = agentConfig,
        };

        var transport = new InProcessChannelTransport(agentConfig.Transport.CapacityRecords);
        var uploadService = new LocalFileSystemUploadService(uploadRoot);

        var builder = Host.CreateApplicationBuilder();

        // ── FakeNode-specific ─────────────────────────────────────────────────
        builder.Services.AddSingleton(fakeNodeConfig);
        builder.Services.AddSingleton(agentConfig);
        builder.Services.AddSingleton(sp => new MockDataSource(scenarioName, scenarioConfig));
        builder.Services.AddSingleton<InProcessChannelTransport>(transport);
        builder.Services.AddSingleton<IAgentTransport>(transport);
        builder.Services.AddSingleton<ITelemetryUploadService>(uploadService);

        // ── Agent services ────────────────────────────────────────────────────
        builder.Services.AddSingleton<IClock, SystemClock>();

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

        builder.Services.AddHostedService<AgentHostedService>();

        // Register orchestrator as singleton so we can resolve it for ExecuteTask access
        builder.Services.AddSingleton<FakeNodeOrchestrator>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<FakeNodeOrchestrator>());

        builder.Logging.ClearProviders();

        var host = builder.Build();
        await host.StartAsync(ct);

        // Wait for the scenario orchestrator to finish (it calls transport.Complete() when done)
        var orchestrator = host.Services.GetRequiredService<FakeNodeOrchestrator>();
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(120));
        await (orchestrator.ExecuteTask ?? Task.CompletedTask).WaitAsync(timeoutCts.Token);

        // Brief pause to allow the ingestion pipeline to drain the remaining channel items
        await Task.Delay(500, CancellationToken.None);

        // Stop the host (triggers GracefulShutdown rotation in AgentHostedService)
        await host.StopAsync(CancellationToken.None);
        host.Dispose();

        // Collect interval manifests and upload artifacts
        var fixture = new FakeNodeFixture(agentConfig.DataRoot, uploadRoot);
        await fixture.CollectResultsAsync();
        return fixture;
    }

    private async Task CollectResultsAsync()
    {
        var intervalsDir = Path.Combine(_dataRoot, "intervals");
        if (!Directory.Exists(intervalsDir))
        {
            Manifests = Array.Empty<IntervalManifest>();
            IntervalZipPaths = Array.Empty<string>();
            return;
        }

        var manifests = new List<IntervalManifest>();
        foreach (var dir in Directory.GetDirectories(intervalsDir).OrderBy(d => Path.GetFileName(d)))
        {
            var manifestPath = Path.Combine(dir, "manifest.json");
            if (!File.Exists(manifestPath)) continue;
            var manifest = await ManifestWriter.ReadAsync(manifestPath, CancellationToken.None);
            if (manifest is not null)
                manifests.Add(manifest);
        }
        Manifests = manifests;

        var zips = Directory.Exists(_uploadRoot)
            ? Directory.GetFiles(_uploadRoot, "*.zip", SearchOption.AllDirectories).ToList()
            : new List<string>();
        IntervalZipPaths = zips;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        TryDeleteDirectory(_dataRoot);
        TryDeleteDirectory(_uploadRoot);
        await Task.CompletedTask;
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
