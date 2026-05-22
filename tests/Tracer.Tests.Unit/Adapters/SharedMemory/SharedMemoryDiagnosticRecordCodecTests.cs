using FluentAssertions;
using Tracer.Adapters.SharedMemory;
using Tracer.Core.Domain;
using Tracer.Core.Identity;
using Tracer.Core.Records;
using Tracer.Core.Time;
using Xunit;

namespace Tracer.Tests.Unit.Adapters.SharedMemory;

public sealed class SharedMemoryDiagnosticRecordCodecTests
{
    private readonly SharedMemoryDiagnosticRecordCodec _codec = new();

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static EventRecord MakeEventRecord() => new()
    {
        SequenceNumber = 1UL,
        PublishWallclock = WallclockTime.FromDateTimeOffset(
            new DateTimeOffset(2026, 5, 19, 14, 0, 0, TimeSpan.Zero)),
        ReceiveWallclock = WallclockTime.FromDateTimeOffset(
            new DateTimeOffset(2026, 5, 19, 14, 0, 0, 100, TimeSpan.Zero)),
        PublisherNode = new AgentId("pub"),
        SubscriberNode = new AgentId("sub"),
        Topic = new TopicName("topic.event"),
        EventId = new EventId(42UL),
        TraceId = new TraceId(99UL),
        ParentEventId = null,
        EntityId = new EntityId("entity-1"),
        PayloadJson = """{"value":7}""",
    };

    private static StateSampleRecord MakeSlowStateRecord() => new()
    {
        SequenceNumber = 2UL,
        PublishWallclock = WallclockTime.Zero,
        ReceiveWallclock = WallclockTime.Zero,
        PublisherNode = new AgentId("pub"),
        SubscriberNode = new AgentId("sub"),
        Topic = new TopicName("topic.slow"),
        InstanceKey = "inst-1",
        PayloadJson = """{"x":1.0}""",
        Rate = StateSampleRate.Slow,
    };

    private static StateSampleRecord MakeFastStateRecord() => new()
    {
        SequenceNumber = 3UL,
        PublishWallclock = WallclockTime.Zero,
        ReceiveWallclock = WallclockTime.Zero,
        PublisherNode = new AgentId("pub"),
        SubscriberNode = new AgentId("sub"),
        Topic = new TopicName("topic.fast"),
        InstanceKey = "inst-fast",
        PayloadJson = """{"y":2.0}""",
        Rate = StateSampleRate.Fast,
    };

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Encode_ThenDecode_EventRecord_RoundTrips()
    {
        var original = MakeEventRecord();

        var bytes = _codec.Encode(original);
        var decoded = _codec.Decode(bytes) as EventRecord;

        decoded.Should().NotBeNull();
        decoded!.SequenceNumber.Should().Be(original.SequenceNumber);
        decoded.TraceId.Should().Be(original.TraceId);
        decoded.EventId.Should().Be(original.EventId);
        decoded.EntityId.Should().Be(original.EntityId);
        decoded.Topic.Value.Should().Be(original.Topic.Value);
        decoded.PublisherNode.Value.Should().Be(original.PublisherNode.Value);
        decoded.PayloadJson.Should().Be(original.PayloadJson);
    }

    [Fact]
    public void Encode_ThenDecode_SlowStateSampleRecord_RoundTrips()
    {
        var original = MakeSlowStateRecord();

        var bytes = _codec.Encode(original);
        var decoded = _codec.Decode(bytes) as StateSampleRecord;

        decoded.Should().NotBeNull();
        decoded!.Rate.Should().Be(StateSampleRate.Slow);
        decoded.InstanceKey.Should().Be(original.InstanceKey);
        decoded.Topic.Value.Should().Be(original.Topic.Value);
        decoded.PayloadJson.Should().Be(original.PayloadJson);
    }

    [Fact]
    public void Encode_ThenDecode_FastStateSampleRecord_RoundTrips()
    {
        var original = MakeFastStateRecord();

        var bytes = _codec.Encode(original);
        var decoded = _codec.Decode(bytes) as StateSampleRecord;

        decoded.Should().NotBeNull();
        decoded!.Rate.Should().Be(StateSampleRate.Fast);
        decoded.InstanceKey.Should().Be(original.InstanceKey);
    }

    [Fact]
    public void Decode_CorruptBytes_ReturnsNull()
    {
        var corrupt = System.Text.Encoding.UTF8.GetBytes("{totally invalid json}");

        var result = _codec.Decode(corrupt);

        result.Should().BeNull();
    }
}
