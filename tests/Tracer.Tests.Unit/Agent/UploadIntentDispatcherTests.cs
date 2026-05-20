using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Tracer.Agent.Lifecycle;
using Tracer.Agent.Storage;
using Tracer.Agent.Upload;
using Tracer.Core.Abstractions;
using Tracer.Core.Domain;
using Tracer.Core.Identity;
using Tracer.Core.Time;
using Xunit;

namespace Tracer.Tests.Unit.Agent;

public sealed class UploadIntentDispatcherTests : IDisposable
{
    private readonly string _tempDir;

    public UploadIntentDispatcherTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // ── Fakes ────────────────────────────────────────────────────────────────

    private sealed class CapturingUploadService : ITelemetryUploadService
    {
        public List<UploadRequest> Requests { get; } = new();

        public Task<UploadIntentId> RequestUploadAsync(UploadRequest request, CancellationToken ct)
        {
            Requests.Add(request);
            return Task.FromResult(new UploadIntentId(Guid.NewGuid().ToString("N")));
        }

        public Task<UploadStatus> GetStatusAsync(UploadIntentId intentId, CancellationToken ct)
            => Task.FromResult(UploadStatus.Unknown);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private IntervalDirectory MakeDir(string tsString)
    {
        IntervalTimestamp.TryParse(tsString, out var ts);
        var dir = new IntervalDirectory(_tempDir, ts!);
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

    // ── Tests ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task UploadIntentDispatcher_Dispatch_CallsUploadServiceOnce()
    {
        var capture = new CapturingUploadService();
        var dispatcher = new UploadIntentDispatcher(capture, NullLogger<UploadIntentDispatcher>.Instance);
        var dir = MakeDir("20260519T140000Z");
        var manifest = MakeManifest(dir);

        await dispatcher.DispatchAsync(dir, manifest, CancellationToken.None);

        capture.Requests.Should().HaveCount(1);
    }

    [Fact]
    public async Task UploadIntentDispatcher_Dispatch_IncludesManifestInFiles()
    {
        var capture = new CapturingUploadService();
        var dispatcher = new UploadIntentDispatcher(capture, NullLogger<UploadIntentDispatcher>.Instance);
        var dir = MakeDir("20260519T140000Z");

        // Write a manifest so it shows up in enumeration
        var manifest = MakeManifest(dir);
        await ManifestWriter.WriteAsync(dir.ManifestPath, manifest, CancellationToken.None);

        await dispatcher.DispatchAsync(dir, manifest, CancellationToken.None);

        var req = capture.Requests.Single();
        req.Files.Should().Contain(f => f.Description == "manifest");
    }
}
