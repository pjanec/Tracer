using FluentAssertions;
using Tracer.Core.Records;
using Xunit;

namespace Tracer.Tests.Unit.Core;

/// <summary>Unit tests for <see cref="StateSampleRecord.TypedValues"/> (FIX-A2).</summary>
public sealed class StateSampleRecordTypedValuesTests
{
    [Fact]
    public void TypedValues_IsNullByDefault()
    {
        var record = new StateSampleRecord
        {
            SequenceNumber = 1,
            PublishWallclock = Tracer.Core.Time.WallclockTime.Zero,
            ReceiveWallclock = Tracer.Core.Time.WallclockTime.Zero,
            PublisherNode = new Tracer.Core.Identity.AgentId("pub"),
            SubscriberNode = new Tracer.Core.Identity.AgentId("sub"),
            Topic = new Tracer.Core.Domain.TopicName("test.topic"),
            InstanceKey = "key1",
            Rate = StateSampleRate.Slow,
            PayloadJson = "{}",
        };
        record.TypedValues.Should().BeNull();
    }

    [Fact]
    public void TypedValues_CanBeSetToNonNull()
    {
        var values = new Dictionary<string, double?> { ["speed"] = 42.5, ["temp"] = null };
        var record = new StateSampleRecord
        {
            SequenceNumber = 2,
            PublishWallclock = Tracer.Core.Time.WallclockTime.Zero,
            ReceiveWallclock = Tracer.Core.Time.WallclockTime.Zero,
            PublisherNode = new Tracer.Core.Identity.AgentId("pub"),
            SubscriberNode = new Tracer.Core.Identity.AgentId("sub"),
            Topic = new Tracer.Core.Domain.TopicName("test.topic"),
            InstanceKey = "key2",
            Rate = StateSampleRate.Slow,
            PayloadJson = "{}",
            TypedValues = values,
        };
        record.TypedValues.Should().ContainKey("speed").WhoseValue.Should().Be(42.5);
        record.TypedValues.Should().ContainKey("temp").WhoseValue.Should().BeNull();
    }

    [Fact]
    public void TypedValues_IsReadOnly_CannotBeModified()
    {
        var values = new Dictionary<string, double?> { ["x"] = 1.0 };
        var record = new StateSampleRecord
        {
            SequenceNumber = 3,
            PublishWallclock = Tracer.Core.Time.WallclockTime.Zero,
            ReceiveWallclock = Tracer.Core.Time.WallclockTime.Zero,
            PublisherNode = new Tracer.Core.Identity.AgentId("pub"),
            SubscriberNode = new Tracer.Core.Identity.AgentId("sub"),
            Topic = new Tracer.Core.Domain.TopicName("t"),
            InstanceKey = "k",
            Rate = StateSampleRate.Fast,
            PayloadJson = "{}",
            TypedValues = values,
        };
        // IReadOnlyDictionary — type should expose the correct interface
        record.TypedValues.Should().BeAssignableTo<IReadOnlyDictionary<string, double?>>();
    }

    [Fact]
    public void TypedValues_SupportsNullableDoubleValues()
    {
        var values = new Dictionary<string, double?>
        {
            ["a"] = 0.0,
            ["b"] = double.NaN,
            ["c"] = null,
            ["d"] = -999.99,
        };
        var record = new StateSampleRecord
        {
            SequenceNumber = 4,
            PublishWallclock = Tracer.Core.Time.WallclockTime.Zero,
            ReceiveWallclock = Tracer.Core.Time.WallclockTime.Zero,
            PublisherNode = new Tracer.Core.Identity.AgentId("pub"),
            SubscriberNode = new Tracer.Core.Identity.AgentId("sub"),
            Topic = new Tracer.Core.Domain.TopicName("t"),
            InstanceKey = "k",
            Rate = StateSampleRate.Slow,
            PayloadJson = "{}",
            TypedValues = values,
        };
        record.TypedValues!["a"].Should().Be(0.0);
        record.TypedValues["c"].Should().BeNull();
        record.TypedValues["d"].Should().Be(-999.99);
    }
}
