using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Tracer.Agent.Configuration;
using Tracer.Agent.Lifecycle;
using Tracer.Agent.Storage;
using Tracer.Agent.Upload;
using Tracer.Core.Abstractions;
using Tracer.Core.Domain;
using Tracer.Core.Identity;
using Tracer.Core.Records;
using Tracer.Core.Time;
using Xunit;

namespace Tracer.Tests.Unit.Agent;

public sealed class IntervalRotatorTests : IAsyncDisposable
{
    private readonly string _tempDir;
    private IntervalRotator? _rotator;

    public IntervalRotatorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
    }

    public async ValueTask DisposeAsync()
    {
        if (_rotator is not null)
            await _rotator.DisposeAsync();

        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // ── Fakes ────────────────────────────────────────────────────────────────

    private sealed class FakeUploadService : ITelemetryUploadService
    {
        public Task<UploadIntentId> RequestUploadAsync(UploadRequest request, CancellationToken ct)
            => Task.FromResult(new UploadIntentId(Guid.NewGuid().ToString("N")));

        public Task<UploadStatus> GetStatusAsync(UploadIntentId intentId, CancellationToken ct)
            => Task.FromResult(UploadStatus.Unknown);
    }

    private sealed class FakeClock(WallclockTime now) : IClock
    {
        public WallclockTime Now => now;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static readonly DateTimeOffset Anchor =
        new(2026, 5, 19, 14, 0, 0, TimeSpan.Zero);

    private IntervalRotator BuildRotator(IClock? clock = null)
    {
        var config = new AgentConfig
        {
            NodeId = "test-node",
            DataRoot = _tempDir,
            LogsRoot = _tempDir,
            IntervalDuration = TimeSpan.FromHours(1),
        };
        var fakeClock = clock ?? new FakeClock(WallclockTime.FromDateTimeOffset(Anchor));
        var scheduler = new IntervalScheduler(fakeClock, config);
        var uploader = new UploadIntentDispatcher(
            new FakeUploadService(),
            NullLogger<UploadIntentDispatcher>.Instance);
        _rotator = new IntervalRotator(
            scheduler, config, uploader, fakeClock, NullLogger<IntervalRotator>.Instance);
        return _rotator;
    }

    private static EventRecord MakeEvent(string topic = "test.topic") => new()
    {
        SequenceNumber = 1,
        PublishWallclock = WallclockTime.Zero,
        ReceiveWallclock = WallclockTime.Zero,
        PublisherNode = new AgentId("n"),
        SubscriberNode = new AgentId("n"),
        Topic = new TopicName(topic),
        EventId = new EventId(1),
        TraceId = TraceId.None,
        PayloadJson = "{}",
    };

    private static StateSampleRecord MakeSlow() => new()
    {
        SequenceNumber = 2,
        PublishWallclock = WallclockTime.Zero,
        ReceiveWallclock = WallclockTime.Zero,
        PublisherNode = new AgentId("n"),
        SubscriberNode = new AgentId("n"),
        Topic = new TopicName("state.topic"),
        InstanceKey = "k",
        PayloadJson = "{}",
        Rate = StateSampleRate.Slow,
    };

    private static StateSampleRecord MakeFast(string topic = "fast.topic") => new()
    {
        SequenceNumber = 3,
        PublishWallclock = WallclockTime.Zero,
        ReceiveWallclock = WallclockTime.Zero,
        PublisherNode = new AgentId("n"),
        SubscriberNode = new AgentId("n"),
        Topic = new TopicName(topic),
        InstanceKey = "k",
        PayloadJson = "{}",
        Rate = StateSampleRate.Fast,
    };

    // ── Tests ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task IntervalRotator_OpenCurrentAsync_CreatesDirectory()
    {
        var rotator = BuildRotator();
        await rotator.OpenCurrentAsync(CancellationToken.None);

        rotator.CurrentDirectory.Should().NotBeNull();
        Directory.Exists(rotator.CurrentDirectory!.RootPath).Should().BeTrue();
    }

    [Fact]
    public async Task IntervalRotator_OpenCurrentAsync_Twice_ThrowsInvalidOperation()
    {
        var rotator = BuildRotator();
        await rotator.OpenCurrentAsync(CancellationToken.None);

        var act = async () => await rotator.OpenCurrentAsync(CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task IntervalRotator_NotifyRecordWritten_EventRecord_IncrementsCount()
    {
        var rotator = BuildRotator();
        await rotator.OpenCurrentAsync(CancellationToken.None);

        rotator.NotifyRecordWritten(MakeEvent());

        // Rotate to flush manifest and inspect counts
        await rotator.RotateAsync(ManifestFinalizationReason.GracefulShutdown, CancellationToken.None);
        var dir = rotator.CurrentDirectory; // null after GracefulShutdown
        // Find the written manifest
        var manifestPath = Directory.GetFiles(_tempDir, "manifest.json", SearchOption.AllDirectories)
            .Should().ContainSingle().Which;

        var manifest = await ManifestWriter.ReadAsync(manifestPath, CancellationToken.None);
        manifest!.EventCount.Should().Be(1);
    }

    [Fact]
    public async Task IntervalRotator_NotifyRecordWritten_SlowState_IncrementsCount()
    {
        var rotator = BuildRotator();
        await rotator.OpenCurrentAsync(CancellationToken.None);

        rotator.NotifyRecordWritten(MakeSlow());

        await rotator.RotateAsync(ManifestFinalizationReason.GracefulShutdown, CancellationToken.None);

        var manifestPath = Directory.GetFiles(_tempDir, "manifest.json", SearchOption.AllDirectories)
            .Should().ContainSingle().Which;
        var manifest = await ManifestWriter.ReadAsync(manifestPath, CancellationToken.None);
        manifest!.SlowStateCount.Should().Be(1);
    }

    [Fact]
    public async Task IntervalRotator_NotifyRecordWritten_FastState_AddsToTopics()
    {
        var rotator = BuildRotator();
        await rotator.OpenCurrentAsync(CancellationToken.None);

        rotator.NotifyRecordWritten(MakeFast("telemetry.sensors"));

        await rotator.RotateAsync(ManifestFinalizationReason.GracefulShutdown, CancellationToken.None);

        var manifestPath = Directory.GetFiles(_tempDir, "manifest.json", SearchOption.AllDirectories)
            .Should().ContainSingle().Which;
        var manifest = await ManifestWriter.ReadAsync(manifestPath, CancellationToken.None);
        manifest!.FastStateTopics.Should().Contain("telemetry.sensors");
    }

    [Fact]
    public async Task IntervalRotator_RotateAsync_WritesManifestAndSentinel()
    {
        var rotator = BuildRotator();
        await rotator.OpenCurrentAsync(CancellationToken.None);
        var intervalDir = rotator.CurrentDirectory!;

        await rotator.RotateAsync(ManifestFinalizationReason.GracefulShutdown, CancellationToken.None);

        File.Exists(intervalDir.ManifestPath).Should().BeTrue();
        File.Exists(intervalDir.ReadySentinelPath).Should().BeTrue();
    }

    [Fact]
    public async Task IntervalRotator_RotateAsync_GracefulShutdown_DoesNotOpenNext()
    {
        var rotator = BuildRotator();
        await rotator.OpenCurrentAsync(CancellationToken.None);
        await rotator.RotateAsync(ManifestFinalizationReason.GracefulShutdown, CancellationToken.None);

        rotator.CurrentWriter.Should().BeNull();
    }

    // DT-007 fix ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task IntervalRotator_NotifyCaptureGap_AccumulatesInManifest()
    {
        var rotator = BuildRotator();
        await rotator.OpenCurrentAsync(CancellationToken.None);

        var gap = new CaptureGap
        {
            StartUtc = WallclockTime.Zero,
            EndUtc = WallclockTime.Zero,
            Reason = CaptureGapReason.TransportDisconnected,
            DroppedRecordCount = 5,
        };
        rotator.NotifyCaptureGap(gap);

        await rotator.RotateAsync(ManifestFinalizationReason.GracefulShutdown, CancellationToken.None);

        var manifestPath = Directory.GetFiles(_tempDir, "manifest.json", SearchOption.AllDirectories)
            .Should().ContainSingle().Which;

        var manifest = await ManifestWriter.ReadAsync(manifestPath, CancellationToken.None);
        manifest!.CaptureGaps.Should().ContainSingle()
            .Which.Reason.Should().Be(CaptureGapReason.TransportDisconnected);
    }
}

