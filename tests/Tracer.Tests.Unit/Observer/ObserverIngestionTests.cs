using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Tracer.Agent.Configuration;
using Tracer.Agent.Lifecycle;
using Tracer.Agent.Time;
using Tracer.Agent.Upload;
using Tracer.Core.Abstractions;
using Tracer.Core.Domain;
using Tracer.Core.Identity;
using Tracer.Core.Records;
using Tracer.Core.Time;
using Tracer.Observer.Lifecycle;
using Tracer.Observer.Sources;
using Tracer.WebApi.Streaming;
using Xunit;

namespace Tracer.Tests.Unit.Observer;

public sealed class ObserverIngestionTests : IAsyncDisposable
{
    private readonly string _tempDir;
    private readonly AgentConfig _config;
    private readonly IntervalRotator _rotator;
    private readonly NoOpUploadService _upload;

    public ObserverIngestionTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"tracer-ingest-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _config = new AgentConfig
        {
            NodeId = "test",
            DataRoot = _tempDir,
            LogsRoot = _tempDir,
            IntervalDuration = TimeSpan.FromHours(1),
            KeepLastNIntervals = 4,
            DiskWatermarkPercent = 10,
        };
        var clock = new SystemClock();
        var scheduler = new IntervalScheduler(clock, _config);
        _upload = new NoOpUploadService();
        var dispatcher = new UploadIntentDispatcher(_upload, NullLogger<UploadIntentDispatcher>.Instance);
        _rotator = new IntervalRotator(scheduler, _config, dispatcher, clock,
            NullLogger<IntervalRotator>.Instance);
    }

    [Fact]
    public async Task Records_WrittenToCurrentWriter()
    {
        await _rotator.OpenCurrentAsync(default);
        var state = new ObserverStateReporter();
        var broadcaster = new LiveEventBroadcaster();
        var pipeline = new ObserverIngestionPipeline(
            Array.Empty<NamedDataSource>(), _rotator, broadcaster, state,
            NullLogger<ObserverIngestionPipeline>.Instance);

        var ev = MakeEvent();
        await _rotator.CurrentWriter!.AppendEventAsync(ev, default);
        _rotator.NotifyRecordWritten(ev);
        state.IncrementIngested();

        state.Snapshot().IngestedTotal.Should().Be(1);
    }

    [Fact]
    public async Task Events_PublishedToLiveBroadcaster()
    {
        await _rotator.OpenCurrentAsync(default);
        var published = new List<EventRecord>();
        var broadcaster = new TestBroadcaster(published);

        var ev = MakeEvent();
        await _rotator.CurrentWriter!.AppendEventAsync(ev, default);
        broadcaster.Publish(ev);

        published.Should().ContainSingle().Which.Should().BeEquivalentTo(ev);
    }

    [Fact]
    public async Task SlowState_WrittenButNotBroadcast()
    {
        await _rotator.OpenCurrentAsync(default);
        var published = new List<EventRecord>();
        var broadcaster = new TestBroadcaster(published);

        var ss = MakeSlowState();
        await _rotator.CurrentWriter!.AppendStateAsync(ss, default);
        _rotator.NotifyRecordWritten(ss);

        published.Should().BeEmpty();
    }

    [Fact]
    public async Task FastState_WrittenViaAppendFastStateAsync()
    {
        await _rotator.OpenCurrentAsync(default);

        var fs = MakeFastState();
        await _rotator.CurrentWriter!.AppendFastStateAsync(fs, default);
        _rotator.NotifyRecordWritten(fs);

        await Task.CompletedTask;
    }

    [Fact]
    public async Task Cancellation_PropagatesCleanly()
    {
        await _rotator.OpenCurrentAsync(default);
        var state = new ObserverStateReporter();
        var broadcaster = new LiveEventBroadcaster();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var pipeline = new ObserverIngestionPipeline(
            Array.Empty<NamedDataSource>(), _rotator, broadcaster, state,
            NullLogger<ObserverIngestionPipeline>.Instance);

        await pipeline.RunAsync(cts.Token);
    }

    [Fact]
    public async Task WriteFailure_IncrementsDropCounter_PipelineContinues()
    {
        var state = new ObserverStateReporter();
        state.IncrementDropped();
        state.Snapshot().DroppedTotal.Should().Be(1);
        await Task.CompletedTask;
    }

    private static readonly AgentId Node1 = new AgentId("node-1");
    private static readonly WallclockTime Now = WallclockTime.FromDateTimeOffset(DateTimeOffset.UtcNow);

    private static EventRecord MakeEvent() => new EventRecord
    {
        SequenceNumber = 1,
        PublishWallclock = Now,
        ReceiveWallclock = Now,
        PublisherNode = Node1,
        SubscriberNode = Node1,
        Topic = new TopicName("test.topic"),
        EventId = new EventId(1),
        TraceId = new TraceId(1),
        PayloadJson = "{}",
    };

    private static StateSampleRecord MakeSlowState() => new StateSampleRecord
    {
        SequenceNumber = 2,
        PublishWallclock = Now,
        ReceiveWallclock = Now,
        PublisherNode = Node1,
        SubscriberNode = Node1,
        Topic = new TopicName("test.state"),
        InstanceKey = "inst-1",
        Rate = StateSampleRate.Slow,
        PayloadJson = "{}",
    };

    private static StateSampleRecord MakeFastState() => new StateSampleRecord
    {
        SequenceNumber = 3,
        PublishWallclock = Now,
        ReceiveWallclock = Now,
        PublisherNode = Node1,
        SubscriberNode = Node1,
        Topic = new TopicName("test.faststate"),
        InstanceKey = "inst-2",
        Rate = StateSampleRate.Fast,
        PayloadJson = "{}",
    };

    public async ValueTask DisposeAsync()
    {
        await _rotator.DisposeAsync();
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private sealed class NoOpUploadService : ITelemetryUploadService
    {
        public Task<UploadIntentId> RequestUploadAsync(UploadRequest request, CancellationToken ct)
            => Task.FromResult(new UploadIntentId(Guid.NewGuid().ToString()));
        public Task<UploadStatus> GetStatusAsync(UploadIntentId intentId, CancellationToken ct)
            => Task.FromResult(UploadStatus.Complete);
    }

    private sealed class TestBroadcaster : LiveEventBroadcaster
    {
        private readonly List<EventRecord> _captured;
        public TestBroadcaster(List<EventRecord> captured) => _captured = captured;
        public override void Publish(EventRecord ev) => _captured.Add(ev);
    }
}
