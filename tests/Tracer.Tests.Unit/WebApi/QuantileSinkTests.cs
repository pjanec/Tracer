using FluentAssertions;
using Tracer.WebApi.Util;
using Xunit;

namespace Tracer.Tests.Unit.WebApi;

public sealed class QuantileSinkTests
{
    [Fact]
    public void QuantileSink_Empty_ReturnsNaN()
    {
        var sink = new QuantileSink();
        sink.GetQuantile(0.5).Should().Be(double.NaN);
    }

    [Fact]
    public void QuantileSink_KnownDistribution_P50Accurate()
    {
        var sink = new QuantileSink(reservoirSize: 10_000);
        for (var i = 1; i <= 1000; i++)
            sink.Add(i);

        var p50 = sink.GetQuantile(0.50);
        p50.Should().BeInRange(490, 510);
    }

    [Fact]
    public void QuantileSink_KnownDistribution_P99Accurate()
    {
        var sink = new QuantileSink(reservoirSize: 10_000);
        for (var i = 1; i <= 1000; i++)
            sink.Add(i);

        var p99 = sink.GetQuantile(0.99);
        p99.Should().BeInRange(980, 1000);
    }

    [Fact]
    public void QuantileSink_ReservoirFull_OlderValuesReplaced()
    {
        var sink = new QuantileSink(reservoirSize: 10_000);
        for (var i = 1; i <= 20_000; i++)
            sink.Add(i);

        sink.Count.Should().Be(20_000);
        // Reservoir cannot exceed its size
        var p50 = sink.GetQuantile(0.5);
        p50.Should().BePositive();
    }
}
