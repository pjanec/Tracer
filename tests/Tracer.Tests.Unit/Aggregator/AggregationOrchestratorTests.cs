using System.IO.Compression;
using FluentAssertions;
using Tracer.Adapters.Mock.Storage;
using Tracer.Agent.Storage;
using Tracer.Aggregator;
using Tracer.Aggregator.Configuration;
using Tracer.Aggregator.Progress;
using Tracer.Core.Domain;
using Tracer.Core.Identity;
using Tracer.Core.Time;
using Xunit;

namespace Tracer.Tests.Unit.Aggregator;

public class AggregationOrchestratorTests : IDisposable
{
    private readonly List<string> _tempDirs = new();

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private string TempDir()
    {
        var d = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        _tempDirs.Add(d);
        return d;
    }

    public void Dispose()
    {
        foreach (var d in _tempDirs)
        {
            try { if (Directory.Exists(d)) Directory.Delete(d, recursive: true); }
            catch { /* ignore cleanup failures */ }
        }
    }

    // ── Tests ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_NeitherTimeRangeNorSessionId_ThrowsArgumentException()
    {
        var nasRoot = TempDir();
        var outputDir = Path.Combine(TempDir(), "output");
        var reader = new LocalFileSystemStorageReader(nasRoot);
        var orchestrator = new AggregationOrchestrator(reader);

        var request = new AggregationRequest { OutputPath = outputDir };

        await Assert.ThrowsAsync<ArgumentException>(() =>
            orchestrator.RunAsync(request));
    }

    [Fact]
    public async Task RunAsync_NoIntervalsFound_ThrowsInvalidOperationException()
    {
        var nasRoot = TempDir(); // empty — no nodes, no intervals
        var outputDir = Path.Combine(TempDir(), "output");
        var reader = new LocalFileSystemStorageReader(nasRoot);
        var orchestrator = new AggregationOrchestrator(reader);

        var start = new DateTimeOffset(2026, 5, 19, 14, 0, 0, TimeSpan.Zero);
        var end = start.AddHours(1);
        var request = new AggregationRequest
        {
            OutputPath = outputDir,
            TimeRange = new Tracer.Core.Time.TimeRange(
                WallclockTime.FromDateTimeOffset(start),
                WallclockTime.FromDateTimeOffset(end)),
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            orchestrator.RunAsync(request));
        ex.Message.Should().Contain("No intervals found");
    }

    [Fact]
    public async Task RunAsync_ValidRequest_ProgressStartedAndCompletedReported()
    {
        // Build a NAS directory with one node and one interval zip
        var nasRoot = TempDir();
        var nodeId = "test-node";
        var start = new DateTimeOffset(2026, 5, 19, 14, 0, 0, TimeSpan.Zero);
        var end = start.AddHours(1);

        await CreateMinimalNasZipAsync(nasRoot, nodeId, start, end);

        var outputDir = Path.Combine(TempDir(), "output-bundle");
        var reader = new LocalFileSystemStorageReader(nasRoot);
        var orchestrator = new AggregationOrchestrator(reader);

        var progressEvents = new List<AggregationStage>();
        var reporter = new LambdaProgressReporter(
            (stage, _) => progressEvents.Add(stage));

        var request = new AggregationRequest
        {
            OutputPath = outputDir,
            TimeRange = new Tracer.Core.Time.TimeRange(
                WallclockTime.FromDateTimeOffset(start.AddMinutes(-1)),
                WallclockTime.FromDateTimeOffset(end.AddMinutes(1))),
        };

        var result = await orchestrator.RunAsync(request, reporter);

        progressEvents.Should().StartWith(new[] { AggregationStage.Started });
        progressEvents.Should().EndWith(new[] { AggregationStage.Completed });
        result.Should().NotBeNull();
        result.SourceIntervalsUsed.Should().Be(1);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates {nasRoot}/{nodeId}/{ts}.zip with manifest.json, empty events.duckdb,
    /// and empty slow_state.duckdb.
    /// </summary>
    private async Task CreateMinimalNasZipAsync(
        string nasRoot, string nodeId, DateTimeOffset start, DateTimeOffset end)
    {
        var ts = IntervalTimestamp.FromUtc(start);
        var nodeDir = Path.Combine(nasRoot, nodeId);
        Directory.CreateDirectory(nodeDir);

        var manifest = new IntervalManifest
        {
            IntervalStart = ts,
            IntervalEnd = IntervalTimestamp.FromUtc(end),
            NodeId = new AgentId(nodeId),
            TracerVersion = "1.0.0",
            SchemaVersion = 1,
            EventCount = 0,
            SlowStateCount = 0,
            FastStateTopics = Array.Empty<string>(),
            CaptureGaps = Array.Empty<CaptureGap>(),
            SessionMarkers = Array.Empty<SessionMarker>(),
            FinalizedAt = WallclockTime.FromDateTimeOffset(end),
            FinalizationReason = ManifestFinalizationReason.ScheduledRotation,
        };

        // Write to temp staging dir, then zip it up
        var staging = TempDir();
        var manifestPath = Path.Combine(staging, "manifest.json");
        await ManifestWriter.WriteAsync(manifestPath, manifest, CancellationToken.None);

        // Write empty placeholder database files
        var eventsPath = Path.Combine(staging, "events.duckdb");
        var slowStatePath = Path.Combine(staging, "slow_state.duckdb");
        await File.WriteAllBytesAsync(eventsPath, Array.Empty<byte>());
        await File.WriteAllBytesAsync(slowStatePath, Array.Empty<byte>());

        var zipPath = Path.Combine(nodeDir, $"{ts.Value}.zip");
        ZipFile.CreateFromDirectory(staging, zipPath);
    }

    private sealed class LambdaProgressReporter : IAggregationProgressReporter
    {
        private readonly Action<AggregationStage, string?> _action;
        public LambdaProgressReporter(Action<AggregationStage, string?> action) => _action = action;
        public void Report(AggregationStage stage, string? message = null) => _action(stage, message);
    }
}
