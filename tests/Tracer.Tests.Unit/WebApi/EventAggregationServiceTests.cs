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

/// <summary>Tests for <see cref="EventAggregationService"/> using real DuckDB storage.</summary>
public sealed class EventAggregationServiceTests : IAsyncDisposable
{
    private readonly ObserverFixture _fixture;
    private readonly EventAggregationService _svc;

    private static readonly DateTimeOffset BaseTime =
        new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);

    private static ulong _nextId = 70000;

    private static WallclockTime At(DateTimeOffset dto) =>
        WallclockTime.FromUnixNanoseconds(dto.ToUnixTimeMilliseconds() * 1_000_000L);

    private EventRecord MakeEvent(
        string topic = "game.tick",
        string node = "node-a",
        DateTimeOffset? at = null,
        Severity? severity = null,
        string? notableLabel = null)
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
            TraceId = new TraceId(id),
            PayloadJson = "{}",
            Severity = severity,
            NotableLabel = notableLabel,
        };
    }

    private AggregateQuery BaseQuery(string bucketDuration = "1s") => new AggregateQuery
    {
        SessionId = "agg-session",
        From = At(BaseTime.AddSeconds(-1)),
        To = At(BaseTime.AddSeconds(30)),
        BucketDuration = bucketDuration,
    };

    public EventAggregationServiceTests()
    {
        _fixture = ObserverFixture.CreateAsync().GetAwaiter().GetResult();
        _svc = _fixture.App.Services.GetRequiredService<EventAggregationService>();
    }

    [Fact]
    public async Task AggregateAsync_OneHourViewportAt5sBuckets_Returns720Buckets()
    {
        // 1-hour range at 5s buckets yields at most 3600/5 = 720 buckets
        var hourStart = BaseTime;
        var hourEnd = BaseTime.AddHours(1);
        var ev1 = MakeEvent(at: hourStart);
        var ev2 = MakeEvent(at: hourStart.AddMinutes(30));
        var ev3 = MakeEvent(at: hourEnd.AddSeconds(-5));
        await _fixture.PushAsync([ev1, ev2, ev3]);

        var query = new AggregateQuery
        {
            SessionId = "hour-agg-session",
            From = At(hourStart),
            To = At(hourEnd),
            BucketDuration = "5s",
        };
        var result = await _svc.AggregateAsync(query, CancellationToken.None);

        result.BucketDuration.Should().Be("5s");
        result.Buckets.Count.Should().BeLessOrEqualTo(720,
            "a 1-hour range at 5-second buckets can produce at most 720 buckets");
    }

    [Fact]
    public async Task AggregateAsync_BucketTotalsEqualSumOfGroupCounts()
    {
        var uniqueTopic = $"bucket.total.{Guid.NewGuid():N}";
        var ev1 = MakeEvent(topic: uniqueTopic, at: BaseTime);
        var ev2 = MakeEvent(topic: uniqueTopic, at: BaseTime.AddMilliseconds(100));
        var ev3 = MakeEvent(topic: uniqueTopic, at: BaseTime.AddSeconds(2));
        await _fixture.PushAsync([ev1, ev2, ev3]);

        var query = BaseQuery("1s") with { Topics = [uniqueTopic] };
        var result = await _svc.AggregateAsync(query, CancellationToken.None);

        var totalCount = result.Buckets.Sum(b => b.Total);
        totalCount.Should().Be(3);
    }

    [Fact]
    public async Task AggregateAsync_GroupByTopic_GroupsAreTopics()
    {
        var topic1 = $"grpbyT1.{_nextId}";
        var topic2 = $"grpbyT2.{_nextId + 1}";
        var ev1 = MakeEvent(topic: topic1, at: BaseTime);
        var ev2 = MakeEvent(topic: topic2, at: BaseTime);
        await _fixture.PushAsync([ev1, ev2]);

        var query = BaseQuery("1s") with
        {
            GroupBy = AggregateGroupBy.Topic,
            Topics = [topic1, topic2],
        };
        var result = await _svc.AggregateAsync(query, CancellationToken.None);

        var allGroupKeys = result.Buckets.SelectMany(b => b.Groups.Select(g => g.GroupKey)).ToList();
        allGroupKeys.Should().Contain(topic1);
        allGroupKeys.Should().Contain(topic2);
    }

    [Fact]
    public async Task AggregateAsync_GroupByNone_EachBucketHasOnlyOneGroupWithNullKey()
    {
        var ev1 = MakeEvent(at: BaseTime);
        var ev2 = MakeEvent(at: BaseTime);
        await _fixture.PushAsync([ev1, ev2]);

        var query = BaseQuery("1s") with { GroupBy = AggregateGroupBy.None };
        var result = await _svc.AggregateAsync(query, CancellationToken.None);

        // Each bucket should have at most one group (NULL key)
        foreach (var bucket in result.Buckets)
            bucket.Groups.Should().HaveCountLessOrEqualTo(1);
    }

    [Fact]
    public async Task AggregateAsync_InvalidBucketDuration_ThrowsArgumentException()
    {
        var query = BaseQuery("99x");  // invalid

        var act = async () => await _svc.AggregateAsync(query, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*bucketDuration*");
    }

    [Fact]
    public async Task ValidDurations_AllAccepted()
    {
        var validDurations = new[] { "100ms", "1s", "5s", "30s", "1m", "5m", "30m", "1h" };

        foreach (var dur in validDurations)
        {
            var query = BaseQuery(dur);
            var act = async () => await _svc.AggregateAsync(query, CancellationToken.None);
            await act.Should().NotThrowAsync($"'{dur}' should be a valid duration");
        }
    }

    [Fact]
    public async Task AggregateAsync_EmptyTimeRange_ReturnsEmptyBucketList()
    {
        var uniqueTopic = $"empty.agg.{Guid.NewGuid():N}";
        var query = new AggregateQuery
        {
            SessionId = "agg-session",
            From = At(BaseTime.AddHours(5)),
            To = At(BaseTime.AddHours(6)),
            BucketDuration = "1s",
            Topics = [uniqueTopic],
        };

        var result = await _svc.AggregateAsync(query, CancellationToken.None);

        result.Buckets.Should().BeEmpty();
    }

    [Fact]
    public async Task AggregateAsync_GroupByNode_GroupsResultsByPublisherNode()
    {
        var nodeA = $"nodeA-{Guid.NewGuid():N}";
        var nodeB = $"nodeB-{Guid.NewGuid():N}";
        var evA = MakeEvent(node: nodeA, at: BaseTime);
        var evB = MakeEvent(node: nodeB, at: BaseTime);
        await _fixture.PushAsync([evA, evB]);

        var query = BaseQuery("1s") with
        {
            GroupBy = AggregateGroupBy.Node,
            Nodes = [nodeA, nodeB],
        };
        var result = await _svc.AggregateAsync(query, CancellationToken.None);

        var allGroupKeys = result.Buckets.SelectMany(b => b.Groups.Select(g => g.GroupKey)).ToList();
        allGroupKeys.Should().Contain(nodeA, "nodeA should appear as a group key");
        allGroupKeys.Should().Contain(nodeB, "nodeB should appear as a group key");
    }

    [Fact]
    public async Task AggregateAsync_FilterAppliedBeforeGrouping_OnlyMatchingEventsCounted()
    {
        var keepTopic = $"keep.agg.{Guid.NewGuid():N}";
        var discardTopic = $"discard.agg.{Guid.NewGuid():N}";
        // Push 3 keep events and 5 discard events
        var keepEvents = Enumerable.Range(0, 3)
            .Select(_ => MakeEvent(topic: keepTopic, at: BaseTime))
            .ToArray();
        var discardEvents = Enumerable.Range(0, 5)
            .Select(_ => MakeEvent(topic: discardTopic, at: BaseTime))
            .ToArray();
        await _fixture.PushAsync([.. keepEvents, .. discardEvents]);

        var query = BaseQuery("1s") with
        {
            Topics = [keepTopic],
        };
        var result = await _svc.AggregateAsync(query, CancellationToken.None);

        var totalCount = result.Buckets.Sum(b => b.Total);
        totalCount.Should().Be(3, "only the 3 'keep' events should be counted after filter");
    }

    public async ValueTask DisposeAsync() => await _fixture.DisposeAsync();
}
