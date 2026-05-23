using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Tracer.Agent.Configuration;
using Tracer.Agent.Lifecycle;
using Tracer.Agent.Storage;
using Tracer.Agent.Upload;
using Tracer.Core.Abstractions;
using Tracer.Core.Domain;
using Tracer.Core.Identity;
using Tracer.Core.Time;
using Tracer.Tests.Unit.Adapters.DDS;
using Xunit;

namespace Tracer.Tests.Unit.Agent;

public sealed class SyncUploadHardeningTests : IDisposable
{
    private readonly string _tempDir;

    public SyncUploadHardeningTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // ── Fakes ─────────────────────────────────────────────────────────────────

    private sealed class SlowUploadService : ITelemetryUploadService
    {
        private readonly TimeSpan _delay;
        public SlowUploadService(TimeSpan delay) => _delay = delay;

        public async Task<UploadIntentId> RequestUploadAsync(UploadRequest request, CancellationToken ct)
        {
            await Task.Delay(_delay, ct);
            return new UploadIntentId(Guid.NewGuid().ToString("N"));
        }

        public Task<UploadStatus> GetStatusAsync(UploadIntentId intentId, CancellationToken ct)
            => Task.FromResult(UploadStatus.Unknown);
    }

    private sealed class InstantUploadService : ITelemetryUploadService
    {
        public Task<UploadIntentId> RequestUploadAsync(UploadRequest request, CancellationToken ct)
            => Task.FromResult(new UploadIntentId(Guid.NewGuid().ToString("N")));

        public Task<UploadStatus> GetStatusAsync(UploadIntentId intentId, CancellationToken ct)
            => Task.FromResult(UploadStatus.Unknown);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private IntervalDirectory MakeDir(string tsString, int index = 0)
    {
        IntervalTimestamp.TryParse(tsString, out var ts);
        var path = Path.Combine(_tempDir, $"dir{index}");
        Directory.CreateDirectory(path);
        var dir = new IntervalDirectory(path, ts!);
        dir.EnsureCreated();
        return dir;
    }

    private static IntervalManifest MakeManifest(IntervalDirectory dir) => new()
    {
        NodeId = new AgentId("test-node"),
        IntervalStart = dir.Timestamp,
        IntervalEnd = dir.Timestamp,
        FinalizationReason = ManifestFinalizationReason.GracefulShutdown,
        TracerVersion = "1.0.0-test",
        SchemaVersion = 1,
        FinalizedAt = WallclockTime.Zero,
        EventCount = 0,
        SlowStateCount = 0,
        FastStateTopics = Array.Empty<string>(),
        CaptureGaps = Array.Empty<CaptureGap>(),
        SessionMarkers = Array.Empty<SessionMarker>(),
    };

    // ── Tests ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PendingCount_ExceedsThreshold_LogsWarning()
    {
        var logger = new CapturingLogger<UploadIntentDispatcher>();
        var config = new AgentConfig
        {
            NodeId = "test",
            DataRoot = _tempDir,
            LogsRoot = _tempDir,
            BacklogWarningThreshold = 2,
        };

        // Use a slow upload service so dispatches pile up.
        var dispatcher = new UploadIntentDispatcher(
            new SlowUploadService(TimeSpan.FromMilliseconds(200)),
            logger,
            config);

        var tasks = new List<Task>();
        for (int i = 0; i < 4; i++)
        {
            var dir = MakeDir("20260519T140000Z", i);
            var manifest = MakeManifest(dir);
            tasks.Add(dispatcher.DispatchAsync(dir, manifest, CancellationToken.None));
        }

        await Task.WhenAll(tasks);

        logger.Warnings.Should().Contain(w => w.Contains("backlog exceeds threshold"),
            "a warning should be logged when PendingCount exceeds BacklogWarningThreshold");
    }

    [Fact]
    public async Task WaitForPendingAsync_WaitsUntilDispatchComplete()
    {
        var dispatcher = new UploadIntentDispatcher(
            new SlowUploadService(TimeSpan.FromMilliseconds(100)),
            NullLogger<UploadIntentDispatcher>.Instance);

        var dir = MakeDir("20260519T140000Z");
        var manifest = MakeManifest(dir);

        // Fire off a dispatch without awaiting.
        var dispatchTask = dispatcher.DispatchAsync(dir, manifest, CancellationToken.None);

        // At least one item should be pending right after firing.
        // (Race-condition safe: WaitForPendingAsync timeout is generous.)
        await dispatcher.WaitForPendingAsync(TimeSpan.FromSeconds(5));

        dispatcher.PendingCount.Should().Be(0,
            "WaitForPendingAsync should return only after all dispatches complete");

        await dispatchTask; // Clean up.
    }
}
