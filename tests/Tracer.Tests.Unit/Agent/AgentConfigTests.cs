using FluentAssertions;
using Tracer.Adapters.Mock.Transport;
using Tracer.Agent.Configuration;
using Tracer.Core.Domain;
using Tracer.Core.Identity;
using Tracer.Core.Records;
using Tracer.Core.Time;
using Xunit;

namespace Tracer.Tests.Unit.Agent;

public sealed class AgentConfigTests
{
    private static AgentConfig ValidConfig() => new()
    {
        NodeId = "test-node",
        DataRoot = @"C:\tracer\data",
        LogsRoot = @"C:\tracer\logs",
        IntervalDuration = TimeSpan.FromHours(1),
    };

    [Fact]
    public void ConfigValidation_MissingNodeId_Throws()
    {
        var config = ValidConfig();
        config.NodeId = "";
        var act = () => ConfigValidation.Validate(config);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*NodeId*");
    }

    [Fact]
    public void ConfigValidation_RelativeDataRoot_Throws()
    {
        var config = ValidConfig();
        config.DataRoot = "relative/path";
        var act = () => ConfigValidation.Validate(config);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*DataRoot*");
    }

    [Fact]
    public void ConfigValidation_IntervalTooShort_Throws()
    {
        var config = ValidConfig();
        config.IntervalDuration = TimeSpan.FromSeconds(30);
        var act = () => ConfigValidation.Validate(config);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*IntervalDuration*");
    }

    [Fact]
    public void ConfigValidation_NonDivisibleInterval_Throws()
    {
        var config = ValidConfig();
        config.IntervalDuration = TimeSpan.FromMinutes(11); // 11 does not divide 1440
        var act = () => ConfigValidation.Validate(config);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*IntervalDuration*");
    }

    [Fact]
    public void ConfigValidation_ValidConfig_DoesNotThrow()
    {
        var act = () => ConfigValidation.Validate(ValidConfig());
        act.Should().NotThrow();
    }

    [Fact]
    public async Task InProcessChannelTransport_WriteAndRead_RoundTrips()
    {
        // Arrange
        await using var transport = new InProcessChannelTransport(capacity: 10);
        var node = new AgentId("node-a");
        var record = new EventRecord
        {
            SequenceNumber = 1,
            PublishWallclock = WallclockTime.Zero,
            ReceiveWallclock = WallclockTime.Zero,
            PublisherNode = node,
            SubscriberNode = node,
            Topic = new TopicName("test.topic"),
            EventId = new EventId(1),
            TraceId = TraceId.None,
            PayloadJson = "{}",
        };

        // Act
        await transport.WriteAsync(record, CancellationToken.None);
        transport.Complete(); // signal end so ReadAsync terminates

        var received = new List<DiagnosticRecord>();
        await foreach (var r in transport.ReadAsync(CancellationToken.None))
            received.Add(r);

        // Assert
        received.Should().HaveCount(1);
        received[0].Should().BeSameAs(record);
        transport.GetHealth().TotalReceived.Should().Be(1);
    }
}
