using Microsoft.Extensions.Logging.Abstractions;
using Tracer.Adapters.Mock.Scenarios;
using Tracer.Adapters.Mock.Storage;
using Tracer.Aggregator;
using Tracer.Aggregator.Configuration;
using Tracer.Core.Time;

namespace Tracer.TestHarness;

/// <summary>
/// Creates a temporary mock-NAS root, populates it via a <see cref="FakeNodeFixture"/> run,
/// and exposes an <see cref="AggregationOrchestrator"/> ready to build bundles against that data.
/// </summary>
public sealed class AggregationFixture : IAsyncDisposable
{
    private readonly string _dataRoot;
    private readonly string _nasRoot;
    private bool _disposed;

    private AggregationFixture(string dataRoot, string nasRoot)
    {
        _dataRoot = dataRoot;
        _nasRoot = nasRoot;
        OrchestratorForNas = new AggregationOrchestrator(
            new LocalFileSystemStorageReader(nasRoot),
            NullLogger<AggregationOrchestrator>.Instance);
    }

    /// <summary>
    /// The aggregation orchestrator wired to the populated mock-NAS root.
    /// Valid only after <see cref="InitializeAsync"/> completes.
    /// </summary>
    public AggregationOrchestrator OrchestratorForNas { get; }

    /// <summary>
    /// The time range covering all populated intervals in the mock NAS.
    /// Valid only after <see cref="InitializeAsync"/> completes.
    /// </summary>
    public Core.Time.TimeRange NasTimeRange { get; private set; } = null!;

    /// <summary>
    /// Creates a <see cref="FakeNodeFixture"/>, runs a short Calm scenario,
    /// copies the upload root to a stable snapshot directory, and populates
    /// <see cref="NasTimeRange"/>.
    /// </summary>
    public static async Task<AggregationFixture> InitializeAsync(CancellationToken ct = default)
    {
        var dataRoot  = Path.Combine(Path.GetTempPath(), $"agg-fix-data-{Guid.NewGuid():N}");
        var uploadTmp = Path.Combine(Path.GetTempPath(), $"agg-fix-upload-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dataRoot);
        Directory.CreateDirectory(uploadTmp);

        var nasSnapshot = Path.Combine(Path.GetTempPath(), $"agg-fix-nas-{Guid.NewGuid():N}");
        Directory.CreateDirectory(nasSnapshot);

        var agentConfig = new Agent.Configuration.AgentConfig
        {
            NodeId = "agg-fixture-node",
            DataRoot = dataRoot,
            LogsRoot = Path.Combine(dataRoot, "_logs"),
            IntervalDuration = TimeSpan.FromMinutes(15),
            KeepLastNIntervals = 24,
            Transport = new Agent.Configuration.TransportConfig { CapacityRecords = 50_000 },
            UploadService = new Agent.Configuration.UploadServiceConfig { LocalFileSystemRoot = uploadTmp },
        };

        // Align the simulated start time with real UTC so events fall within real-time interval boundaries.
        var simulatedStart = DateTimeOffset.UtcNow;
        var scenarioDuration = TimeSpan.FromSeconds(8);

        var scenarioConfig = new ScenarioConfig
        {
            Duration = scenarioDuration,
            Seed = 11,
            EventsPerSecond = 30,
            StartTime = WallclockTime.FromDateTimeOffset(simulatedStart),
        };

        var fixture = await FakeNodeFixture.RunScenarioAsync("Calm", scenarioConfig, agentConfig, ct);

        if (fixture.Manifests.Count == 0)
            throw new InvalidOperationException("FakeNodeFixture produced no intervals.");

        // Snapshot the upload dir before the fixture disposes it
        foreach (var file in Directory.GetFiles(uploadTmp, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(uploadTmp, file);
            var dst = Path.Combine(nasSnapshot, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            File.Copy(file, dst);
        }

        // Use the simulated start time and duration as the precise event time range.
        // The interval boundaries are wider (real-clock 15-min windows), but using the
        // exact event window ensures the consolidator filter captures real events.
        var rangeStart = simulatedStart;
        var rangeEnd   = simulatedStart + scenarioDuration + TimeSpan.FromSeconds(5); // slight buffer

        await fixture.DisposeAsync();
        SafeDelete(dataRoot);

        var agg = new AggregationFixture(nasSnapshot, nasSnapshot);
        agg.NasTimeRange = new Core.Time.TimeRange(
            WallclockTime.FromDateTimeOffset(rangeStart),
            WallclockTime.FromDateTimeOffset(rangeEnd));

        return agg;
    }

    /// <summary>
    /// Runs a bundle aggregation over the mock NAS using the fixture's time range.
    /// </summary>
    public async Task<AggregationResult> RunDefaultBuildAsync(
        string outputPath,
        CancellationToken ct = default)
    {
        var request = new AggregationRequest
        {
            OutputPath = outputPath,
            TimeRange = NasTimeRange,
        };
        return await OrchestratorForNas.RunAsync(request, ct: ct);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        SafeDelete(_nasRoot);
        await Task.CompletedTask;
    }

    private static void SafeDelete(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); } catch { }
    }
}
