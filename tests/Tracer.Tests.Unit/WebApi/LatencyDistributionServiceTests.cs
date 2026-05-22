using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Tracer.Core.Domain;
using Tracer.Core.Identity;
using Tracer.Core.Records;
using Tracer.Core.Time;
using Tracer.TestHarness.Observer;
using Tracer.WebApi.Queries;
using Xunit;

namespace Tracer.Tests.Unit.WebApi;

public sealed class LatencyDistributionServiceTests : IAsyncDisposable
{
    private readonly ObserverFixture _fixture;
    private readonly LatencyDistributionService _svc;

    private static readonly DateTimeOffset BaseTime =
        new DateTimeOffset(2026, 1, 10, 8, 0, 0, TimeSpan.Zero);

    private static ulong _nextId = 5_000_000;

    private static WallclockTime At(DateTimeOffset dto) =>
        WallclockTime.FromUnixNanoseconds(dto.ToUnixTimeMilliseconds() * 1_000_000L);

    private static EventRecord MakeLatencyEvent(
        string pub, string sub, DateTimeOffset publishAt, double latencyMs,
        string topic = "test.topic", ulong? seqNo = null)
    {
        var id = _nextId++;
        return new EventRecord
        {
            SequenceNumber = seqNo ?? id,
            PublishWallclock = At(publishAt),
            ReceiveWallclock = At(publishAt.AddMilliseconds(latencyMs)),
            PublisherNode = new AgentId(pub),
            SubscriberNode = new AgentId(sub),
            Topic = new TopicName(topic),
            EventId = new EventId(id),
            TraceId = new TraceId(id),
            PayloadJson = "{}",
        };
    }

    public LatencyDistributionServiceTests()
    {
        _fixture = ObserverFixture.CreateAsync().GetAwaiter().GetResult();
        _svc = _fixture.App.Services.GetRequiredService<LatencyDistributionService>();
    }

    private LatencyQuery MakeQuery(
        DateTimeOffset? from = null, DateTimeOffset? to = null,
        string? topic = null, string? pub = null, string? sub = null,
        bool excludeSelf = true) =>
        new()
        {
            From = At(from ?? BaseTime.AddSeconds(-1)),
            To = At(to ?? BaseTime.AddMinutes(60)),
            Topic = topic,
            PublisherNode = pub,
            SubscriberNode = sub,
            ExcludeSelfSubscribe = excludeSelf,
        };

    [Fact]
    public async Task EmptyBundle_ZeroCount()
    {
        var result = await _svc.GetAsync(MakeQuery(), CancellationToken.None);
        result.SampleCount.Should().Be(0);
    }

    [Fact]
    public async Task SingleSample_AllPercentilesEqual()
    {
        var ev = MakeLatencyEvent("pub-a", "sub-b", BaseTime, latencyMs: 5.0, topic: "single.test");
        await _fixture.PushAsync(ev);

        var q = MakeQuery(topic: "single.test");
        var result = await _svc.GetAsync(q, CancellationToken.None);

        result.SampleCount.Should().Be(1);
        result.P50Ms.Should().BeApproximately(5.0, 0.5);
        result.P99Ms.Should().BeApproximately(5.0, 0.5);
    }

    [Fact]
    public async Task ExcludeSelf_Filters()
    {
        var topic = $"excl.self.{_nextId}";
        // 2 same-node rows
        await _fixture.PushAsync(MakeLatencyEvent("nodeX", "nodeX", BaseTime, 1.0, topic));
        await _fixture.PushAsync(MakeLatencyEvent("nodeX", "nodeX", BaseTime.AddSeconds(1), 1.0, topic));
        // 2 different-node rows
        await _fixture.PushAsync(MakeLatencyEvent("nodeX", "nodeY", BaseTime.AddSeconds(2), 2.0, topic));
        await _fixture.PushAsync(MakeLatencyEvent("nodeX", "nodeZ", BaseTime.AddSeconds(3), 2.0, topic));

        var q = MakeQuery(topic: topic, excludeSelf: true);
        var result = await _svc.GetAsync(q, CancellationToken.None);

        result.SampleCount.Should().Be(2);
    }

    [Fact]
    public async Task TopicFilter_Isolates()
    {
        await _fixture.PushAsync(MakeLatencyEvent("p", "s", BaseTime, 3.0, "topic.alpha"));
        await _fixture.PushAsync(MakeLatencyEvent("p", "s", BaseTime, 4.0, "topic.beta"));

        var q = MakeQuery(topic: "topic.alpha");
        var result = await _svc.GetAsync(q, CancellationToken.None);

        result.SampleCount.Should().Be(1);
    }

    [Fact]
    public async Task TimeRange_Respected()
    {
        var topic = $"timerange.{_nextId}";
        // Events spread over 60 minutes, query only a 10-min window
        for (var i = 0; i < 6; i++)
        {
            await _fixture.PushAsync(
                MakeLatencyEvent("p", "s", BaseTime.AddMinutes(i * 10), 1.0, topic));
        }

        var q = MakeQuery(
            from: BaseTime.AddMinutes(-1),
            to: BaseTime.AddMinutes(10),
            topic: topic);
        var result = await _svc.GetAsync(q, CancellationToken.None);

        // Only the first event (at 0 min) falls in the window
        result.SampleCount.Should().Be(1);
    }

    [Fact]
    public async Task NegativeLatency_Included()
    {
        var topic = $"neg.lat.{_nextId}";
        // receive < publish → negative latency (clock skew)
        var id = _nextId++;
        var ev = new EventRecord
        {
            SequenceNumber = id,
            PublishWallclock = At(BaseTime.AddMilliseconds(5)),
            ReceiveWallclock = At(BaseTime), // received BEFORE published (clock skew)
            PublisherNode = new AgentId("pub-neg"),
            SubscriberNode = new AgentId("sub-neg"),
            Topic = new TopicName(topic),
            EventId = new EventId(id),
            TraceId = new TraceId(id),
            PayloadJson = "{}",
        };
        await _fixture.PushAsync(ev);

        var q = MakeQuery(topic: topic);
        var result = await _svc.GetAsync(q, CancellationToken.None);

        result.SampleCount.Should().Be(1);
        result.MinMs.Should().BeLessThan(0);
    }

    [Fact]
    public async Task BucketBounds_AreLogarithmic()
    {
        var topic = $"buckets.{_nextId}";
        // Push events with varied latencies to populate multiple buckets
        var rng = new Random(1);
        for (var i = 0; i < 50; i++)
        {
            var latMs = Math.Pow(2.0, rng.NextDouble() * 8); // 1ms to 256ms
            await _fixture.PushAsync(
                MakeLatencyEvent("pp", "ss", BaseTime.AddMilliseconds(i), latMs, topic));
        }

        var q = MakeQuery(topic: topic);
        var result = await _svc.GetAsync(q, CancellationToken.None);

        result.Buckets.Should().NotBeEmpty();
        // Each bucket's HighMs/LowMs ratio should be 2^(1/4) ≈ 1.189
        foreach (var b in result.Buckets)
        {
            (b.HighMs / b.LowMs).Should().BeApproximately(Math.Pow(2.0, 0.25), 0.01);
        }
    }

    [Fact]
    public async Task ListByPair_SortedByP99Desc()
    {
        var topic = $"pair.sort.{_nextId}";
        // A→B: low latency
        for (var i = 0; i < 5; i++)
            await _fixture.PushAsync(MakeLatencyEvent("A", "B", BaseTime.AddSeconds(i), 1.0, topic));
        // A→C: high latency
        for (var i = 0; i < 5; i++)
            await _fixture.PushAsync(MakeLatencyEvent("A", "C", BaseTime.AddSeconds(i), 100.0, topic));

        var pairs = await _svc.ListByPairAsync(
            At(BaseTime.AddSeconds(-1)),
            At(BaseTime.AddMinutes(10)),
            minSamples: 1,
            limit: 10,
            CancellationToken.None);

        pairs.Should().HaveCountGreaterOrEqualTo(2);
        // First pair should have higher P99
        pairs[0].P99Ms.Should().BeGreaterThan(pairs[1].P99Ms);
    }

    [Fact]
    public async Task ListByPair_MinSamplesFilter()
    {
        var topic = $"pair.min.{_nextId}";
        // A→B: 1 sample (below minSamples=5)
        await _fixture.PushAsync(MakeLatencyEvent("A", "B", BaseTime, 1.0, topic));
        // A→C: 5 samples
        for (var i = 0; i < 5; i++)
            await _fixture.PushAsync(MakeLatencyEvent("A", "C", BaseTime.AddSeconds(i), 2.0, topic));

        var pairs = await _svc.ListByPairAsync(
            At(BaseTime.AddSeconds(-1)),
            At(BaseTime.AddMinutes(10)),
            minSamples: 5,
            limit: 10,
            CancellationToken.None);

        pairs.Should().OnlyContain(p => p.SampleCount >= 5);
    }

    public async ValueTask DisposeAsync() => await _fixture.DisposeAsync();
}
