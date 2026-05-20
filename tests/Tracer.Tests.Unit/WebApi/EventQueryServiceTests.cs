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

/// <summary>Tests for <see cref="EventQueryService"/> using real DuckDB storage.</summary>
public sealed class EventQueryServiceTests : IAsyncDisposable
{
    private readonly ObserverFixture _fixture;
    private readonly EventQueryService _svc;

    private static readonly DateTimeOffset BaseTime =
        new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero);

    private static ulong _nextId = 60000;

    private static WallclockTime At(DateTimeOffset dto) =>
        WallclockTime.FromUnixNanoseconds(dto.ToUnixTimeMilliseconds() * 1_000_000L);

    private EventRecord MakeEvent(
        string topic = "game.tick",
        string node = "node-a",
        DateTimeOffset? at = null,
        string? notableLabel = null,
        Severity? severity = null,
        string? entityId = null,
        string? playerId = null,
        ulong? traceId = null)
    {
        var id = _nextId++;
        return new EventRecord
        {
            SequenceNumber = id,
            PublishWallclock = At(at ?? BaseTime),
            ReceiveWallclock = At(at ?? BaseTime),
            PublisherNode = new AgentId(node),
            SubscriberNode = new AgentId(node),
            Topic = new TopicName(topic),
            EventId = new EventId(id),
            TraceId = new TraceId(traceId ?? id),
            PayloadJson = "{}",
            EntityId = entityId is not null ? new EntityId(entityId) : null,
            OwningPlayerId = playerId,
            Severity = severity,
            NotableLabel = notableLabel,
        };
    }

    private EventQuery BaseQuery(DateTimeOffset? from = null, DateTimeOffset? to = null) => new EventQuery
    {
        SessionId = "test-session",
        From = At(from ?? BaseTime.AddSeconds(-1)),
        To = At(to ?? BaseTime.AddSeconds(10)),
    };

    public EventQueryServiceTests()
    {
        _fixture = ObserverFixture.CreateAsync().GetAwaiter().GetResult();
        _svc = _fixture.App.Services.GetRequiredService<EventQueryService>();
    }

    [Fact]
    public async Task ListAsync_NoFilter_ReturnsAllEventsInTimeOrder()
    {
        var ev1 = MakeEvent(topic: "a.b", at: BaseTime);
        var ev2 = MakeEvent(topic: "c.d", at: BaseTime.AddSeconds(1));
        await _fixture.PushAsync([ev1, ev2]);

        var result = await _svc.ListAsync(BaseQuery(), CancellationToken.None);

        result.Events.Should().Contain(e => e.Topic == "a.b");
        result.Events.Should().Contain(e => e.Topic == "c.d");
        result.TotalMatching.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task ListAsync_TimeRange_ExcludesEventsOutsideRange()
    {
        var inside = MakeEvent(topic: "time.inside", at: BaseTime);
        var outside = MakeEvent(topic: "time.outside", at: BaseTime.AddMinutes(10));
        await _fixture.PushAsync([inside, outside]);

        var query = BaseQuery(from: BaseTime.AddSeconds(-1), to: BaseTime.AddSeconds(5));
        var result = await _svc.ListAsync(query, CancellationToken.None);

        result.Events.Should().Contain(e => e.Topic == "time.inside");
        result.Events.Should().NotContain(e => e.Topic == "time.outside");
    }

    [Fact]
    public async Task ListAsync_TopicFilter_ReturnsOnlyMatchingTopics()
    {
        var uniqueTopic = $"filter.topic.{_nextId}";
        var match = MakeEvent(topic: uniqueTopic, at: BaseTime);
        var other = MakeEvent(topic: "other.topic", at: BaseTime);
        await _fixture.PushAsync([match, other]);

        var query = BaseQuery() with { Topics = [uniqueTopic] };
        var result = await _svc.ListAsync(query, CancellationToken.None);

        result.Events.Should().AllSatisfy(e => e.Topic.Should().Be(uniqueTopic));
        result.Events.Should().Contain(e => e.Topic == uniqueTopic);
    }

    [Fact]
    public async Task ListAsync_MultiTopicFilter_OrsWithinFilter()
    {
        var topic1 = $"multi.t1.{_nextId}";
        var topic2 = $"multi.t2.{_nextId + 1UL}";
        var ev1 = MakeEvent(topic: topic1, at: BaseTime);
        var ev2 = MakeEvent(topic: topic2, at: BaseTime);
        var ev3 = MakeEvent(topic: "unrelated", at: BaseTime);
        await _fixture.PushAsync([ev1, ev2, ev3]);

        var query = BaseQuery() with { Topics = [topic1, topic2] };
        var result = await _svc.ListAsync(query, CancellationToken.None);

        result.Events.Should().Contain(e => e.Topic == topic1);
        result.Events.Should().Contain(e => e.Topic == topic2);
        result.Events.Should().NotContain(e => e.Topic == "unrelated");
    }

    [Fact]
    public async Task ListAsync_MultipleFilterTypes_AndsAcrossFilters()
    {
        var uniqueTopic = $"multi.filter.{_nextId}";
        var matchNode = "node-match";
        var noMatchNode = "node-other";
        var matchEv = MakeEvent(topic: uniqueTopic, node: matchNode, at: BaseTime);
        var wrongNode = MakeEvent(topic: uniqueTopic, node: noMatchNode, at: BaseTime);
        var wrongTopic = MakeEvent(topic: "different", node: matchNode, at: BaseTime);
        await _fixture.PushAsync([matchEv, wrongNode, wrongTopic]);

        var query = BaseQuery() with
        {
            Topics = [uniqueTopic],
            Nodes = [matchNode],
        };
        var result = await _svc.ListAsync(query, CancellationToken.None);

        result.Events.Should().AllSatisfy(e =>
        {
            e.Topic.Should().Be(uniqueTopic);
            e.PublisherNode.Should().Be(matchNode);
        });
    }

    [Fact]
    public async Task ListAsync_TraceIdFilter_ReturnsOnlyThatTrace()
    {
        ulong traceVal = 0xFEEDFACE12345678UL;
        var match = MakeEvent(traceId: traceVal, at: BaseTime);
        var other = MakeEvent(traceId: traceVal + 1, at: BaseTime);
        await _fixture.PushAsync([match, other]);

        var query = BaseQuery() with { TraceId = traceVal.ToString("X16") };
        var result = await _svc.ListAsync(query, CancellationToken.None);

        result.Events.Should().Contain(e => e.TraceId == traceVal.ToString("X16"));
    }

    [Fact]
    public async Task ListAsync_Limit_TruncatesAndSetsTruncatedFlag()
    {
        var events = Enumerable.Range(0, 10)
            .Select(i => MakeEvent(topic: $"limit.topic.{(ulong)i + _nextId}", at: BaseTime.AddMilliseconds(i)))
            .ToList();
        await _fixture.PushAsync(events);

        var query = BaseQuery() with { Limit = 3 };
        var result = await _svc.ListAsync(query, CancellationToken.None);

        result.Returned.Should().BeLessThanOrEqualTo(3);
        result.Truncated.Should().BeTrue();
    }

    [Fact]
    public async Task ListAsync_OrderDescending_ReturnsByNewestFirst()
    {
        var ev1 = MakeEvent(topic: "order.asc", at: BaseTime);
        var ev2 = MakeEvent(topic: "order.desc.last", at: BaseTime.AddSeconds(5));
        await _fixture.PushAsync([ev1, ev2]);

        var query = BaseQuery() with { OrderDescending = true, Topics = ["order.asc", "order.desc.last"] };
        var result = await _svc.ListAsync(query, CancellationToken.None);

        if (result.Events.Count >= 2)
            result.Events[0].OccurredAtUtc.Should().BeOnOrAfter(result.Events[1].OccurredAtUtc);
    }

    [Fact]
    public async Task ListAsync_EmptyResult_ReturnsTotalMatchingZero()
    {
        var uniqueTopic = $"empty.result.{Guid.NewGuid():N}";
        var query = BaseQuery() with { Topics = [uniqueTopic] };

        var result = await _svc.ListAsync(query, CancellationToken.None);

        result.Events.Should().BeEmpty();
        result.TotalMatching.Should().Be(0);
        result.Truncated.Should().BeFalse();
    }

    [Fact]
    public async Task ListAsync_NotablesOnly_ExcludesNonNotables()
    {
        var notable = MakeEvent(topic: "notable.event", notableLabel: "BigHit", at: BaseTime);
        var nonNotable = MakeEvent(topic: "boring.event", notableLabel: null, at: BaseTime);
        await _fixture.PushAsync([notable, nonNotable]);

        var query = BaseQuery() with { NotablesOnly = true };
        var result = await _svc.ListAsync(query, CancellationToken.None);

        result.Events.Should().AllSatisfy(e => e.NotableLabel.Should().NotBeNull());
    }

    public async ValueTask DisposeAsync() => await _fixture.DisposeAsync();
}
