using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Tracer.Agent.Ingestion;
using Tracer.Core.Abstractions;
using Tracer.Core.Domain;
using Tracer.Core.Identity;
using Tracer.Core.Records;
using Tracer.Core.Time;
using Xunit;

namespace Tracer.Tests.Unit.Agent;

public sealed class RecordRouterTests
{
    // ── Fakes ────────────────────────────────────────────────────────────────

    private sealed class FakeWriter : IDiagnosticStorageWriter
    {
        public int EventAppendCount;
        public int SlowStateAppendCount;
        public int FastStateAppendCount;

        public Task AppendEventAsync(EventRecord record, CancellationToken ct)
        {
            EventAppendCount++;
            return Task.CompletedTask;
        }

        public Task AppendStateAsync(StateSampleRecord record, CancellationToken ct)
        {
            SlowStateAppendCount++;
            return Task.CompletedTask;
        }

        public Task AppendFastStateAsync(StateSampleRecord record, CancellationToken ct)
        {
            FastStateAppendCount++;
            return Task.CompletedTask;
        }

        public Task AppendBatchAsync(IReadOnlyList<DiagnosticRecord> records, CancellationToken ct)
            => Task.CompletedTask;

        public Task FlushAsync(CancellationToken ct) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeIntervalContext : IIntervalContext
    {
        private readonly IDiagnosticStorageWriter? _writer;
        public int NotifyCount;

        public FakeIntervalContext(IDiagnosticStorageWriter? writer) => _writer = writer;
        public IDiagnosticStorageWriter? CurrentWriter => _writer;
        public void NotifyRecordWritten(DiagnosticRecord record) => NotifyCount++;
        public void NotifyCaptureGap(CaptureGap gap) { }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static EventRecord MakeEvent() => new()
    {
        SequenceNumber = 1,
        PublishWallclock = WallclockTime.Zero,
        ReceiveWallclock = WallclockTime.Zero,
        PublisherNode = new AgentId("n"),
        SubscriberNode = new AgentId("n"),
        Topic = new TopicName("test"),
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
        Topic = new TopicName("state"),
        InstanceKey = "k",
        PayloadJson = "{}",
        Rate = StateSampleRate.Slow,
    };

    private static StateSampleRecord MakeFast() => new()
    {
        SequenceNumber = 3,
        PublishWallclock = WallclockTime.Zero,
        ReceiveWallclock = WallclockTime.Zero,
        PublisherNode = new AgentId("n"),
        SubscriberNode = new AgentId("n"),
        Topic = new TopicName("fast"),
        InstanceKey = "k",
        PayloadJson = "{}",
        Rate = StateSampleRate.Fast,
    };

    // ── Tests ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RecordRouter_EventRecord_CallsAppendEventAsync()
    {
        var writer = new FakeWriter();
        var context = new FakeIntervalContext(writer);
        var router = new RecordRouter(context, NullLogger<RecordRouter>.Instance);

        await router.RouteAsync(MakeEvent(), CancellationToken.None);

        writer.EventAppendCount.Should().Be(1);
        context.NotifyCount.Should().Be(1);
    }

    [Fact]
    public async Task RecordRouter_SlowStateSample_CallsAppendStateAsync()
    {
        var writer = new FakeWriter();
        var context = new FakeIntervalContext(writer);
        var router = new RecordRouter(context, NullLogger<RecordRouter>.Instance);

        await router.RouteAsync(MakeSlow(), CancellationToken.None);

        writer.SlowStateAppendCount.Should().Be(1);
        context.NotifyCount.Should().Be(1);
    }

    [Fact]
    public async Task RecordRouter_FastStateSample_CallsAppendFastStateAsync()
    {
        var writer = new FakeWriter();
        var context = new FakeIntervalContext(writer);
        var router = new RecordRouter(context, NullLogger<RecordRouter>.Instance);

        await router.RouteAsync(MakeFast(), CancellationToken.None);

        writer.FastStateAppendCount.Should().Be(1);
        context.NotifyCount.Should().Be(1);
    }

    // DT-008 fix ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task RecordRouter_AfterWrite_NotifiesIntervalContext()
    {
        var writer = new FakeWriter();
        var context = new FakeIntervalContext(writer);
        var router = new RecordRouter(context, NullLogger<RecordRouter>.Instance);

        await router.RouteAsync(MakeEvent(), CancellationToken.None);

        context.NotifyCount.Should().Be(1,
            because: "NotifyRecordWritten must be called exactly once per routed record");
    }
}
