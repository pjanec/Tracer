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

public sealed class LatencyOutlierServiceTests : IAsyncDisposable
{
    private readonly ObserverFixture _fixture;
    private readonly LatencyOutlierService _svc;

    private static readonly DateTimeOffset BaseTime =
        new DateTimeOffset(2026, 1, 10, 12, 0, 0, TimeSpan.Zero);

    private static ulong _nextId = 7_000_000;

    private static WallclockTime At(DateTimeOffset dto) =>
        WallclockTime.FromUnixNanoseconds(dto.ToUnixTimeMilliseconds() * 1_000_000L);

    private static EventRecord MakeEvent(
        double latencyMs, string topic = "out.topic",
        string pub = "pub-a", string sub = "sub-b",
        DateTimeOffset? at = null)
    {
        var id = _nextId++;
        var publishAt = at ?? BaseTime.AddMilliseconds((long)id % 10_000);
        return new EventRecord
        {
            SequenceNumber = id,
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

    public LatencyOutlierServiceTests()
    {
        _fixture = ObserverFixture.CreateAsync().GetAwaiter().GetResult();
        _svc = _fixture.App.Services.GetRequiredService<LatencyOutlierService>();
    }

    private LatencyOutlierQuery MakeQuery(
        double? threshold = null, string? topic = null, int limit = 100,
        DateTimeOffset? from = null, DateTimeOffset? to = null) =>
        new()
        {
            From = At(from ?? BaseTime.AddSeconds(-1)),
            To = At(to ?? BaseTime.AddHours(2)),
            Topic = topic,
            ThresholdMs = threshold,
            Limit = limit,
        };

    [Fact]
    public async Task ExplicitThreshold_NoViolations_EmptyResult()
    {
        var topic = $"out.none.{_nextId}";
        await _fixture.PushAsync(MakeEvent(5.0, topic));

        var result = await _svc.GetOutliersAsync(
            MakeQuery(threshold: 100.0, topic: topic), CancellationToken.None);

        result.Outliers.Should().BeEmpty();
    }

    [Fact]
    public async Task ExplicitThreshold_Violations_Returned()
    {
        var topic = $"out.vio.{_nextId}";
        await _fixture.PushAsync(MakeEvent(200.0, topic));
        await _fixture.PushAsync(MakeEvent(5.0, topic));

        var result = await _svc.GetOutliersAsync(
            MakeQuery(threshold: 50.0, topic: topic), CancellationToken.None);

        result.Outliers.Should().HaveCount(1);
        result.Outliers[0].LatencyMs.Should().BeGreaterThan(50.0);
        result.Outliers[0].BudgetSource.Should().Be("budget");
    }

    [Fact]
    public async Task ExplicitThreshold_SortedDescLatency()
    {
        var topic = $"out.sort.{_nextId}";
        await _fixture.PushAsync(MakeEvent(300.0, topic));
        await _fixture.PushAsync(MakeEvent(100.0, topic));
        await _fixture.PushAsync(MakeEvent(500.0, topic));

        var result = await _svc.GetOutliersAsync(
            MakeQuery(threshold: 50.0, topic: topic), CancellationToken.None);

        result.Outliers.Should().HaveCount(3);
        result.Outliers[0].LatencyMs.Should().BeGreaterOrEqualTo(result.Outliers[1].LatencyMs);
        result.Outliers[1].LatencyMs.Should().BeGreaterOrEqualTo(result.Outliers[2].LatencyMs);
    }

    [Fact]
    public async Task LimitRespected()
    {
        var topic = $"out.limit.{_nextId}";
        for (var i = 0; i < 20; i++)
            await _fixture.PushAsync(MakeEvent(200.0 + i, topic));

        var result = await _svc.GetOutliersAsync(
            MakeQuery(threshold: 50.0, topic: topic, limit: 5), CancellationToken.None);

        result.Outliers.Should().HaveCount(5);
    }

    [Fact]
    public async Task NoThreshold_NoBudget_FallbackToP999()
    {
        var topic = $"out.p999.{_nextId}";
        // 50 events with low latency + 1 spike; the spike should be flagged as outlier
        for (var i = 0; i < 49; i++)
            await _fixture.PushAsync(MakeEvent(5.0, topic));
        // One spike
        await _fixture.PushAsync(MakeEvent(10000.0, topic));

        var result = await _svc.GetOutliersAsync(
            MakeQuery(topic: topic), CancellationToken.None);

        // The spike should show up in results
        result.Outliers.Should().NotBeEmpty();
        result.Outliers[0].LatencyMs.Should().BeGreaterThan(100.0);
    }

    [Fact]
    public async Task SelfSubscribers_Excluded()
    {
        var topic = $"out.self.{_nextId}";
        // Self subscriber with huge latency
        var id = _nextId++;
        var selfEv = new EventRecord
        {
            SequenceNumber = id,
            PublishWallclock = At(BaseTime),
            ReceiveWallclock = At(BaseTime.AddMilliseconds(9999)),
            PublisherNode = new AgentId("nodeX"),
            SubscriberNode = new AgentId("nodeX"),
            Topic = new TopicName(topic),
            EventId = new EventId(id),
            TraceId = new TraceId(id),
            PayloadJson = "{}",
        };
        await _fixture.PushAsync(selfEv);

        var result = await _svc.GetOutliersAsync(
            MakeQuery(threshold: 100.0, topic: topic), CancellationToken.None);

        result.Outliers.Should().BeEmpty();
    }

    [Fact]
    public async Task TimeRange_Filters_Outliers()
    {
        var topic = $"out.time.{_nextId}";
        await _fixture.PushAsync(MakeEvent(200.0, topic, at: BaseTime.AddHours(-10)));
        await _fixture.PushAsync(MakeEvent(200.0, topic, at: BaseTime));

        var q = MakeQuery(threshold: 100.0, topic: topic,
            from: BaseTime.AddMinutes(-1), to: BaseTime.AddHours(1));
        var result = await _svc.GetOutliersAsync(q, CancellationToken.None);

        result.Outliers.Should().HaveCount(1);
    }

    [Fact]
    public async Task FieldsPopulated_Correctly()
    {
        var topic = $"out.fields.{_nextId}";
        var ev = MakeEvent(200.0, topic, pub: "pub-x", sub: "sub-y");
        await _fixture.PushAsync(ev);

        var result = await _svc.GetOutliersAsync(
            MakeQuery(threshold: 100.0, topic: topic), CancellationToken.None);

        result.Outliers.Should().HaveCount(1);
        var o = result.Outliers[0];
        o.Topic.Should().Be(topic);
        o.PublisherNode.Should().Be("pub-x");
        o.SubscriberNode.Should().Be("sub-y");
        o.LatencyMs.Should().BeApproximately(200.0, 1.0);
    }

    public async ValueTask DisposeAsync() => await _fixture.DisposeAsync();
}
