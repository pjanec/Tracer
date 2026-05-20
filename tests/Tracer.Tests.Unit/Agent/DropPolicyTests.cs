using FluentAssertions;
using Tracer.Agent.Ingestion;
using Tracer.Core.Domain;
using Tracer.Core.Identity;
using Tracer.Core.Records;
using Tracer.Core.Time;
using Xunit;

namespace Tracer.Tests.Unit.Agent;

public sealed class DropPolicyTests
{
    private readonly DropPolicy _policy = new();

    private static EventRecord MakeEvent() => new()
    {
        SequenceNumber = 1,
        PublishWallclock = WallclockTime.Zero,
        ReceiveWallclock = WallclockTime.Zero,
        PublisherNode = new AgentId("n"),
        SubscriberNode = new AgentId("n"),
        Topic = new TopicName("evt"),
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

    [Fact]
    public void DropPolicy_Healthy_DoesNotDropAnything()
    {
        _policy.ShouldDrop(MakeEvent(), BackpressureLevel.Healthy, out _).Should().BeFalse();
        _policy.ShouldDrop(MakeSlow(), BackpressureLevel.Healthy, out _).Should().BeFalse();
        _policy.ShouldDrop(MakeFast(), BackpressureLevel.Healthy, out _).Should().BeFalse();
    }

    [Fact]
    public void DropPolicy_FastStateAtRisk_DropsFastStateOnly()
    {
        _policy.ShouldDrop(MakeFast(), BackpressureLevel.FastStateAtRisk, out _).Should().BeTrue();
        _policy.ShouldDrop(MakeSlow(), BackpressureLevel.FastStateAtRisk, out _).Should().BeFalse();
        _policy.ShouldDrop(MakeEvent(), BackpressureLevel.FastStateAtRisk, out _).Should().BeFalse();
    }

    [Fact]
    public void DropPolicy_SlowStateAtRisk_DropsSlowAndFast()
    {
        _policy.ShouldDrop(MakeFast(), BackpressureLevel.SlowStateAtRisk, out _).Should().BeTrue();
        _policy.ShouldDrop(MakeSlow(), BackpressureLevel.SlowStateAtRisk, out _).Should().BeTrue();
        _policy.ShouldDrop(MakeEvent(), BackpressureLevel.SlowStateAtRisk, out _).Should().BeFalse();
    }

    [Fact]
    public void DropPolicy_Saturated_DropsAll()
    {
        _policy.ShouldDrop(MakeFast(), BackpressureLevel.Saturated, out _).Should().BeTrue();
        _policy.ShouldDrop(MakeSlow(), BackpressureLevel.Saturated, out _).Should().BeTrue();
        _policy.ShouldDrop(MakeEvent(), BackpressureLevel.Saturated, out _).Should().BeTrue();
    }

    [Fact]
    public void DropPolicy_FastStateAtRisk_ReasonIsFastStateDropped()
    {
        _policy.ShouldDrop(MakeFast(), BackpressureLevel.FastStateAtRisk, out var reason);
        reason.Should().Be(CaptureGapReason.BackpressureFastStateDropped);
    }
}
