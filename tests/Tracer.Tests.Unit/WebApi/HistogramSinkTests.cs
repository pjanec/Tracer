using FluentAssertions;
using Tracer.WebApi.Util;
using Xunit;

namespace Tracer.Tests.Unit.WebApi;

public sealed class HistogramSinkTests
{
    [Fact]
    public void HistogramSink_Empty_ReturnsNoBuckets()
    {
        var sink = new HistogramSink();
        sink.GetBuckets().Should().BeEmpty();
    }

    [Fact]
    public void HistogramSink_SingleValue_OneBucket()
    {
        var sink = new HistogramSink();
        sink.Add(2.0);

        var buckets = sink.GetBuckets();
        buckets.Should().HaveCount(1);
        var b = buckets[0];
        b.Count.Should().Be(1);
        b.LowMs.Should().BeLessOrEqualTo(2.0);
        b.HighMs.Should().BeGreaterOrEqualTo(2.0);
    }

    [Fact]
    public void HistogramSink_BucketBounds_Logarithmic()
    {
        // Values 1.0, 2.0, 4.0, 8.0 should each land in distinct buckets
        var values = new[] { 1.0, 2.0, 4.0, 8.0 };
        var indices = values.Select(HistogramSink.BucketIndex).ToList();

        indices.Distinct().Should().HaveCount(4, "each power-of-two value should be in a different bucket");
    }

    [Fact]
    public void HistogramSink_NegativeAndNearZero_ClampsToMin()
    {
        var sink = new HistogramSink();
        var act = () =>
        {
            sink.Add(-0.5);
            sink.Add(0.0);
            sink.Add(0.0001);
        };

        act.Should().NotThrow();
        sink.GetBuckets().Should().NotBeEmpty();
    }

    [Fact]
    public void HistogramSink_TotalCount_MatchesAdds()
    {
        var sink = new HistogramSink();
        var rng = new Random(1);
        for (var i = 0; i < 500; i++)
            sink.Add(rng.NextDouble() * 100.0);

        var total = sink.GetBuckets().Sum(b => b.Count);
        total.Should().Be(500);
    }
}
