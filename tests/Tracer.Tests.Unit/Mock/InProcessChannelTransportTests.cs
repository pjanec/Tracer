using FluentAssertions;
using Tracer.Adapters.Mock.Transport;
using Tracer.Core.Domain;
using Tracer.Core.Identity;
using Tracer.Core.Records;
using Tracer.Core.Time;
using Xunit;

namespace Tracer.Tests.Unit.Mock;

public sealed class InProcessChannelTransportTests
{
    private static EventRecord MakeEvent(ulong seq) => new()
    {
        SequenceNumber = seq,
        PublishWallclock = WallclockTime.Zero,
        ReceiveWallclock = WallclockTime.Zero,
        PublisherNode = new AgentId("n"),
        SubscriberNode = new AgentId("n"),
        Topic = new TopicName("t"),
        EventId = new EventId(seq),
        TraceId = TraceId.None,
        PayloadJson = "{}",
    };

    [Fact]
    public async Task InProcessChannelTransport_CapacityOne_SecondWriteDropsOldest()
    {
        var transport = new InProcessChannelTransport(capacity: 1);

        await transport.WriteAsync(MakeEvent(1));
        await transport.WriteAsync(MakeEvent(2)); // oldest (seq=1) is dropped

        transport.Complete();

        var received = new List<ulong>();
        await foreach (var r in transport.ReadAsync(CancellationToken.None))
            received.Add(r.SequenceNumber);

        // Only the second record should be present since the first was dropped
        received.Should().ContainSingle().Which.Should().Be(2UL);

        var health = transport.GetHealth();
        health.TotalDropped.Should().Be(1);
    }

    [Fact]
    public async Task InProcessChannelTransport_Complete_ReadAsyncCompletes()
    {
        var transport = new InProcessChannelTransport(capacity: 10);
        await transport.WriteAsync(MakeEvent(1));
        transport.Complete();

        var count = 0;
        await foreach (var _ in transport.ReadAsync(CancellationToken.None))
            count++;

        count.Should().Be(1);
    }

    [Fact]
    public async Task InProcessChannelTransport_GetHealth_ReflectsDrops()
    {
        var transport = new InProcessChannelTransport(capacity: 2);

        await transport.WriteAsync(MakeEvent(1));
        await transport.WriteAsync(MakeEvent(2));
        await transport.WriteAsync(MakeEvent(3)); // one drop

        var health = transport.GetHealth();
        health.TotalReceived.Should().Be(3);
        health.TotalDropped.Should().Be(1);
        health.Capacity.Should().Be(2);
    }
}
