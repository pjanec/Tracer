using FluentAssertions;
using Tracer.Core.Domain;
using Tracer.Core.Identity;
using Tracer.Core.Records;
using Tracer.Core.Time;
using Xunit;

namespace Tracer.Tests.Unit.Core;

public sealed class RecordTests
{
    private static WallclockTime MakeTime(int offsetSeconds = 0) =>
        new WallclockTime(1_700_000_000_000_000_000L + (long)offsetSeconds * 1_000_000_000L);

    private static EventRecord MakeEvent(
        ulong seq = 1,
        EventId? parentId = null,
        EntityId? entityId = null) =>
        new EventRecord
        {
            EventId = new EventId(seq),
            TraceId = new TraceId(42),
            ParentEventId = parentId,
            SequenceNumber = seq,
            PublishWallclock = MakeTime(),
            ReceiveWallclock = MakeTime(1),
            PublisherNode = new AgentId("pub"),
            SubscriberNode = new AgentId("sub"),
            Topic = new TopicName("test.topic"),
            EntityId = entityId,
            PayloadJson = "{}",
        };

    [Fact]
    public void EventRecord_WithNullParentEventId_IsValid()
    {
        var record = MakeEvent(parentId: null);

        record.ParentEventId.Should().BeNull();
        record.EventId.Value.Should().Be(1UL);
    }

    [Fact]
    public void StateSampleRecord_FastRate_CanBeConstructed()
    {
        var record = new StateSampleRecord
        {
            SequenceNumber = 1,
            PublishWallclock = MakeTime(),
            ReceiveWallclock = MakeTime(1),
            PublisherNode = new AgentId("pub"),
            SubscriberNode = new AgentId("sub"),
            Topic = new TopicName("state.topic"),
            InstanceKey = "entity-1",
            Rate = StateSampleRate.Fast,
            PayloadJson = "{}",
        };

        record.Rate.Should().Be(StateSampleRate.Fast);
        record.InstanceKey.Should().Be("entity-1");
    }

    [Fact]
    public void EventRecord_EqualityByValue()
    {
        var a = MakeEvent(1);
        var b = MakeEvent(1);

        a.Should().Be(b);
        a.Should().NotBeSameAs(b);
    }

    [Fact]
    public void WallclockTime_RoundTripDateTimeOffset_LosslessWithinTickResolution()
    {
        var dto = new DateTimeOffset(2025, 6, 1, 12, 0, 0, 0, TimeSpan.Zero);

        var wc = WallclockTime.FromDateTimeOffset(dto);
        var roundTripped = wc.ToDateTimeOffset();

        var diffTicks = Math.Abs((roundTripped - dto).Ticks);
        diffTicks.Should().BeLessThanOrEqualTo(1,
            "round-trip should preserve within 100ns (1 tick)");
    }

    [Fact]
    public void WallclockTime_Subtraction_YieldsTimeSpan()
    {
        var t1 = MakeTime(0);
        var t2 = MakeTime(1); // 1 second later

        var diff = t2 - t1;

        diff.Should().Be(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void WallclockTime_Addition_YieldsCorrectTime()
    {
        var t = MakeTime(0);
        var result = t + TimeSpan.FromSeconds(5);

        result.NanosecondsSinceEpoch.Should().Be(t.NanosecondsSinceEpoch + 5_000_000_000L);
    }
}
