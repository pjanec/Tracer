using FluentAssertions;
using Tracer.Adapters.DDS;
using Tracer.Core.Identity;
using Xunit;

namespace Tracer.Tests.Unit.Adapters.DDS;

public sealed class DdsTraceContextExtractorTests
{
    // ── Fakes ────────────────────────────────────────────────────────────────

    private sealed class FakeEventSample : IDdsSample
    {
        private readonly object _payload;
        public DateTimeOffset SourceTimestamp => DateTimeOffset.UtcNow;
        public ulong SequenceNumber => 1;
        public FakeEventSample(object payload) => _payload = payload;
        public object GetPayload() => _payload;
    }

    private sealed class FakeEventPayload
    {
        public ulong traceId { get; set; }
        public ulong eventId { get; set; }
        public ulong parentEventId { get; set; }
    }

    private sealed class FakePascalCasePayload
    {
        public ulong TraceId { get; set; }
        public ulong EventId { get; set; }
        public ulong ParentEventId { get; set; }
    }

    private sealed class FakeMissingFieldPayload
    {
        // Missing trace fields on purpose — extractor should throw.
    }

    private static DdsTopicMetadata MakeEventMeta(Type sampleType) => new()
    {
        TopicName = "topic.event",
        SampleType = sampleType,
        Kind = DdsTopicKind.Event,
        EntityIdField = null,
    };

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Extract_NonEventTopic_ReturnsEmpty()
    {
        var extractor = new DdsTraceContextExtractor();
        var sample = new FakeEventSample(new FakeEventPayload());
        var meta = new DdsTopicMetadata
        {
            TopicName = "topic.slow",
            SampleType = typeof(FakeEventPayload),
            Kind = DdsTopicKind.SlowState,
            EntityIdField = null,
        };

        var result = extractor.Extract(sample, meta);

        result.Should().Be(TraceContext.Empty);
    }

    [Fact]
    public void Extract_CamelCaseFields_ExtractsCorrectValues()
    {
        var extractor = new DdsTraceContextExtractor();
        var payload = new FakeEventPayload { traceId = 100UL, eventId = 200UL, parentEventId = 300UL };
        var sample = new FakeEventSample(payload);
        var meta = MakeEventMeta(typeof(FakeEventPayload));

        var result = extractor.Extract(sample, meta);

        result.TraceId.Should().Be(100UL);
        result.EventId.Value.Should().Be(200UL);
        result.ParentEventId.Value.Should().Be(300UL);
    }

    [Fact]
    public void Extract_PascalCaseFields_ExtractsCorrectValues()
    {
        var extractor = new DdsTraceContextExtractor();
        var payload = new FakePascalCasePayload { TraceId = 11UL, EventId = 22UL, ParentEventId = 33UL };
        var sample = new FakeEventSample(payload);
        var meta = MakeEventMeta(typeof(FakePascalCasePayload));

        var result = extractor.Extract(sample, meta);

        result.TraceId.Should().Be(11UL);
        result.EventId.Value.Should().Be(22UL);
        result.ParentEventId.Value.Should().Be(33UL);
    }

    [Fact]
    public void Extract_MissingTraceFields_ThrowsInvalidOperationException()
    {
        var extractor = new DdsTraceContextExtractor();
        var sample = new FakeEventSample(new FakeMissingFieldPayload());
        var meta = MakeEventMeta(typeof(FakeMissingFieldPayload));

        var act = () => extractor.Extract(sample, meta);

        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*FakeMissingFieldPayload*");
    }
}
