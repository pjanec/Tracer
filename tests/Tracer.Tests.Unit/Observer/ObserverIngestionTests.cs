using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using System.Runtime.CompilerServices;
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
using Tracer.Storage.DuckDB;
using Tracer.WebApi.Streaming;
using Xunit;

namespace Tracer.Tests.Unit.Observer;

public sealed class ObserverIngestionTests : IAsyncDisposable
{
    private readonly string _tempDir;
    private readonly AgentConfig _config;
    private readonly IntervalRotator _rotator;

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
        var clock = new SystemClock(TimeProvider.System);
        var scheduler = new IntervalScheduler(clock, _config);
        var upload = new NoOpUploadService();
        var dispatcher = new UploadIntentDispatcher(upload, NullLogger<UploadIntentDispatcher>.Instance);
        _rotator = new IntervalRotator(scheduler, _config, dispatcher, clock,
            NullLogger<IntervalRotator>.Instance);
    }

    [Fact]
    public async Task Records_WrittenToCurrentWriter()
    {
        await _rotator.OpenCurrentAsync(default);
        var state = new ObserverStateReporter();
        var broadcaster = new CountingBroadcaster();
        var source = new FixedDataSource(Enumerable.Range(1, 10).Select(i => (DiagnosticRecord)MakeEvent(i)));
        var pipeline = new ObserverIngestionPipeline(
            [new NamedDataSource("src", source)], _rotator, broadcaster, state,
            NullLogger<ObserverIngestionPipeline>.Instance);

        await pipeline.RunAsync(default);

        state.Snapshot().IngestedTotal.Should().Be(10);

        // Flush the appender so data is visible to a read-only connection
        await _rotator.CurrentWriter!.FlushAsync(default);

        // Verify via DuckDB reader
        await using var reader = await DuckDbStorageReader.OpenAsync(
            _rotator.CurrentDirectory!.EventsDbPath,
            NullLogger<DuckDbStorageReader>.Instance);
        var count = await reader.CountEventsAsync(Tracer.Core.Queries.EventFilter.All, default);
        count.Should().Be(10);
    }

    [Fact]
    public async Task Events_PublishedToLiveBroadcaster()
    {
        await _rotator.OpenCurrentAsync(default);
        var state = new ObserverStateReporter();
        var broadcaster = new CountingBroadcaster();
        var source = new FixedDataSource(Enumerable.Range(1, 3).Select(i => (DiagnosticRecord)MakeEvent(i)));
        var pipeline = new ObserverIngestionPipeline(
            [new NamedDataSource("src", source)], _rotator, broadcaster, state,
            NullLogger<ObserverIngestionPipeline>.Instance);

        await pipeline.RunAsync(default);

        broadcaster.PublishCount.Should().Be(3);
    }

    [Fact]
    public async Task SlowState_WrittenButNotBroadcast()
    {
        await _rotator.OpenCurrentAsync(default);
        var state = new ObserverStateReporter();
        var broadcaster = new CountingBroadcaster();
        var source = new FixedDataSource(new DiagnosticRecord[] { MakeSlowState(1), MakeSlowState(2) });
        var pipeline = new ObserverIngestionPipeline(
            [new NamedDataSource("src", source)], _rotator, broadcaster, state,
            NullLogger<ObserverIngestionPipeline>.Instance);

        await pipeline.RunAsync(default);

        broadcaster.PublishCount.Should().Be(0);
        state.Snapshot().IngestedTotal.Should().Be(2);
    }

    [Fact]
    public async Task FastState_WrittenViaAppendFastStateAsync()
    {
        await _rotator.OpenCurrentAsync(default);
        var state = new ObserverStateReporter();
        var broadcaster = new CountingBroadcaster();
        var source = new FixedDataSource(new DiagnosticRecord[] { MakeFastState(1) });
        var pipeline = new ObserverIngestionPipeline(
            [new NamedDataSource("src", source)], _rotator, broadcaster, state,
            NullLogger<ObserverIngestionPipeline>.Instance);

        await pipeline.RunAsync(default);

        state.Snapshot().IngestedTotal.Should().Be(1);
        state.Snapshot().DroppedTotal.Should().Be(0);
    }

    [Fact]
    public async Task Cancellation_PropagatesCleanly()
    {
        await _rotator.OpenCurrentAsync(default);
        var state = new ObserverStateReporter();
        var broadcaster = new CountingBroadcaster();
        var source = new BlockingDataSource();
        var pipeline = new ObserverIngestionPipeline(
            [new NamedDataSource("src", source)], _rotator, broadcaster, state,
            NullLogger<ObserverIngestionPipeline>.Instance);

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(100);

        // Should complete without throwing OperationCanceledException (pipeline catches it internally)
        Func<Task> act = () => pipeline.RunAsync(cts.Token);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task WriteFailure_IncrementsDropCounter_PipelineContinues()
    {
        await _rotator.OpenCurrentAsync(default);
        // Inject a faulting writer that throws on the first AppendEventAsync
        _rotator.CurrentWriter = new FaultingWriter(realWriter: _rotator.CurrentWriter!);

        var state = new ObserverStateReporter();
        var broadcaster = new CountingBroadcaster();
        var source = new FixedDataSource(Enumerable.Range(1, 3).Select(i => (DiagnosticRecord)MakeEvent(i)));
        var pipeline = new ObserverIngestionPipeline(
            [new NamedDataSource("src", source)], _rotator, broadcaster, state,
            NullLogger<ObserverIngestionPipeline>.Instance);

        await pipeline.RunAsync(default);

        // First write fails → dropped; second and third succeed
        state.Snapshot().DroppedTotal.Should().Be(1);
        state.Snapshot().IngestedTotal.Should().Be(2);
    }

    private static readonly AgentId Node1 = new AgentId("node-1");
    private static readonly WallclockTime Now = WallclockTime.FromDateTimeOffset(DateTimeOffset.UtcNow);

    private static EventRecord MakeEvent(int seq) => new EventRecord
    {
        SequenceNumber = (ulong)seq,
        PublishWallclock = WallclockTime.FromDateTimeOffset(DateTimeOffset.UtcNow.AddSeconds(seq)),
        ReceiveWallclock = Now,
        PublisherNode = Node1,
        SubscriberNode = Node1,
        Topic = new TopicName("test.topic"),
        EventId = new EventId((ulong)seq),
        TraceId = new TraceId((ulong)seq),
        PayloadJson = "{}",
    };

    private static StateSampleRecord MakeSlowState(int seq) => new StateSampleRecord
    {
        SequenceNumber = (ulong)seq,
        PublishWallclock = Now,
        ReceiveWallclock = Now,
        PublisherNode = Node1,
        SubscriberNode = Node1,
        Topic = new TopicName("test.state"),
        InstanceKey = "inst-1",
        Rate = StateSampleRate.Slow,
        PayloadJson = "{}",
    };

    private static StateSampleRecord MakeFastState(int seq) => new StateSampleRecord
    {
        SequenceNumber = (ulong)seq,
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

    // ── Test doubles ────────────────────────────────────────────────────────

    private sealed class NoOpUploadService : ITelemetryUploadService
    {
        public Task<UploadIntentId> RequestUploadAsync(UploadRequest request, CancellationToken ct)
            => Task.FromResult(new UploadIntentId(Guid.NewGuid().ToString()));
        public Task<UploadStatus> GetStatusAsync(UploadIntentId intentId, CancellationToken ct)
            => Task.FromResult(UploadStatus.Complete);
    }

    /// <summary>Counts Publish calls without requiring a full broadcaster stack.</summary>
    private sealed class CountingBroadcaster : LiveEventBroadcaster
    {
        private int _count;
        public int PublishCount => _count;
        public override void Publish(EventRecord ev) => Interlocked.Increment(ref _count);
    }

    /// <summary>Yields a fixed set of records then completes.</summary>
    private sealed class FixedDataSource(IEnumerable<DiagnosticRecord> records) : IDiagnosticDataSource
    {
        private readonly IReadOnlyList<DiagnosticRecord> _records = records.ToList();

        public async IAsyncEnumerable<DiagnosticRecord> ReadAsync(
            [EnumeratorCancellation] CancellationToken ct)
        {
            foreach (var r in _records)
            {
                ct.ThrowIfCancellationRequested();
                yield return r;
                await Task.Yield();
            }
        }
    }

    /// <summary>Yields one event then blocks until cancellation.</summary>
    private sealed class BlockingDataSource : IDiagnosticDataSource
    {
        public async IAsyncEnumerable<DiagnosticRecord> ReadAsync(
            [EnumeratorCancellation] CancellationToken ct)
        {
            yield return MakeEvent(1);
            await Task.Delay(Timeout.Infinite, ct);
        }
    }

    /// <summary>Throws on the first AppendEventAsync call to test drop-counter logic.</summary>
    private sealed class FaultingWriter(IDiagnosticStorageWriter realWriter) : IDiagnosticStorageWriter
    {
        private int _calls;

        public Task AppendEventAsync(EventRecord record, CancellationToken ct)
        {
            if (Interlocked.Increment(ref _calls) == 1)
                throw new InvalidOperationException("Simulated write failure");
            return realWriter.AppendEventAsync(record, ct);
        }

        public Task AppendStateAsync(StateSampleRecord record, CancellationToken ct)
            => realWriter.AppendStateAsync(record, ct);

        public Task AppendFastStateAsync(StateSampleRecord record, CancellationToken ct)
            => realWriter.AppendFastStateAsync(record, ct);

        public Task AppendBatchAsync(IReadOnlyList<DiagnosticRecord> records, CancellationToken ct)
            => realWriter.AppendBatchAsync(records, ct);

        public Task FlushAsync(CancellationToken ct)
            => realWriter.FlushAsync(ct);

        public ValueTask DisposeAsync()
            => realWriter.DisposeAsync();
    }
}
