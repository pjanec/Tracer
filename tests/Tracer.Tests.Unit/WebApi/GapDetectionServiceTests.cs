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

public sealed class GapDetectionServiceTests : IAsyncDisposable
{
    private readonly ObserverFixture _fixture;
    private readonly GapDetectionService _svc;

    private static readonly DateTimeOffset BaseTime =
        new DateTimeOffset(2026, 1, 10, 14, 0, 0, TimeSpan.Zero);

    private static WallclockTime At(DateTimeOffset dto) =>
        WallclockTime.FromUnixNanoseconds(dto.ToUnixTimeMilliseconds() * 1_000_000L);

    private static ulong _baseSeqId = 8_000_000;

    private static EventRecord MakeEvent(
        ulong seqNo, string topic = "gap.topic",
        string pub = "pub-a", string sub = "sub-b",
        DateTimeOffset? at = null)
    {
        var id = _baseSeqId++;
        var publishAt = at ?? BaseTime.AddMilliseconds((long)(seqNo * 10));
        return new EventRecord
        {
            SequenceNumber = seqNo,
            PublishWallclock = At(publishAt),
            ReceiveWallclock = At(publishAt.AddMilliseconds(1.0)),
            PublisherNode = new AgentId(pub),
            SubscriberNode = new AgentId(sub),
            Topic = new TopicName(topic),
            EventId = new EventId(id),
            TraceId = new TraceId(id),
            PayloadJson = "{}",
        };
    }

    public GapDetectionServiceTests()
    {
        _fixture = ObserverFixture.CreateAsync().GetAwaiter().GetResult();
        _svc = _fixture.App.Services.GetRequiredService<GapDetectionService>();
    }

    private GapDetectionQuery MakeQuery(
        string? topic = null, string? pub = null, string? sub = null, int limit = 100) =>
        new()
        {
            From = At(BaseTime.AddSeconds(-1)),
            To = At(BaseTime.AddHours(2)),
            Topic = topic,
            PublisherNode = pub,
            SubscriberNode = sub,
            Limit = limit,
        };

    [Fact]
    public async Task NoGaps_EmptyResult()
    {
        var topic = $"gap.none.{_baseSeqId}";
        // Consecutive sequence numbers: 1, 2, 3, 4, 5
        for (ulong i = 1; i <= 5; i++)
            await _fixture.PushAsync(MakeEvent(i, topic));

        var result = await _svc.GetGapsAsync(MakeQuery(topic: topic), CancellationToken.None);

        result.Gaps.Should().BeEmpty();
        result.TotalGaps.Should().Be(0);
    }

    [Fact]
    public async Task SingleGap_Detected()
    {
        var topic = $"gap.single.{_baseSeqId}";
        // Sequence: 1, 2, 5 (gap: 3-4 missing)
        await _fixture.PushAsync(MakeEvent(1, topic));
        await _fixture.PushAsync(MakeEvent(2, topic));
        await _fixture.PushAsync(MakeEvent(5, topic));

        var result = await _svc.GetGapsAsync(MakeQuery(topic: topic), CancellationToken.None);

        result.Gaps.Should().HaveCount(1);
        result.Gaps[0].ResumedAtSequence.Should().Be(5UL);
        result.Gaps[0].PreviousSequence.Should().Be(2UL);
        result.Gaps[0].MissingCount.Should().Be(2UL); // 3 and 4 are missing
    }

    [Fact]
    public async Task MultipleGaps_SortedByMissingCountDesc()
    {
        var topic = $"gap.multi.{_baseSeqId}";
        // Sequence: 1, 2, 10 (big gap of 7), 11, 15 (gap of 3)
        await _fixture.PushAsync(MakeEvent(1, topic));
        await _fixture.PushAsync(MakeEvent(2, topic));
        await _fixture.PushAsync(MakeEvent(10, topic));
        await _fixture.PushAsync(MakeEvent(11, topic));
        await _fixture.PushAsync(MakeEvent(15, topic));

        var result = await _svc.GetGapsAsync(MakeQuery(topic: topic), CancellationToken.None);

        result.Gaps.Should().HaveCount(2);
        result.Gaps[0].MissingCount.Should().BeGreaterThan(result.Gaps[1].MissingCount);
        result.Gaps[0].MissingCount.Should().Be(7); // gap at 3-9
        result.Gaps[1].MissingCount.Should().Be(3); // gap at 12-14
    }

    [Fact]
    public async Task LimitRespected()
    {
        var topic = $"gap.limit.{_baseSeqId}";
        // Create 20 gaps: sequences 1, 3, 5, ... (every other)
        for (ulong i = 1; i <= 40; i += 2)
            await _fixture.PushAsync(MakeEvent(i, topic));

        var result = await _svc.GetGapsAsync(MakeQuery(topic: topic, limit: 5), CancellationToken.None);

        result.Gaps.Should().HaveCount(5);
    }

    [Fact]
    public async Task SelfSubscribers_Excluded()
    {
        var topic = $"gap.self.{_baseSeqId}";
        // Self-subscriber with a gap
        var id = _baseSeqId++;
        var selfEv1 = new EventRecord
        {
            SequenceNumber = 1,
            PublishWallclock = At(BaseTime),
            ReceiveWallclock = At(BaseTime.AddMilliseconds(1)),
            PublisherNode = new AgentId("nodeX"),
            SubscriberNode = new AgentId("nodeX"),
            Topic = new TopicName(topic),
            EventId = new EventId(id),
            TraceId = new TraceId(id),
            PayloadJson = "{}",
        };
        id = _baseSeqId++;
        var selfEv2 = new EventRecord
        {
            SequenceNumber = 10,
            PublishWallclock = At(BaseTime.AddSeconds(1)),
            ReceiveWallclock = At(BaseTime.AddSeconds(1).AddMilliseconds(1)),
            PublisherNode = new AgentId("nodeX"),
            SubscriberNode = new AgentId("nodeX"),
            Topic = new TopicName(topic),
            EventId = new EventId(id),
            TraceId = new TraceId(id),
            PayloadJson = "{}",
        };

        await _fixture.PushAsync(selfEv1);
        await _fixture.PushAsync(selfEv2);

        var result = await _svc.GetGapsAsync(MakeQuery(topic: topic), CancellationToken.None);

        result.Gaps.Should().BeEmpty(); // self-subscriber events are excluded
    }

    [Fact]
    public async Task TopicFilter_Isolates()
    {
        var topicA = $"gap.ta.{_baseSeqId}";
        var topicB = $"gap.tb.{_baseSeqId}";
        // Topic A: gap
        await _fixture.PushAsync(MakeEvent(1, topicA));
        await _fixture.PushAsync(MakeEvent(5, topicA));
        // Topic B: no gap
        await _fixture.PushAsync(MakeEvent(1, topicB));
        await _fixture.PushAsync(MakeEvent(2, topicB));

        var resultA = await _svc.GetGapsAsync(MakeQuery(topic: topicA), CancellationToken.None);
        var resultB = await _svc.GetGapsAsync(MakeQuery(topic: topicB), CancellationToken.None);

        resultA.Gaps.Should().HaveCount(1);
        resultB.Gaps.Should().BeEmpty();
    }

    [Fact]
    public async Task FieldsPopulated_Correctly()
    {
        var topic = $"gap.fields.{_baseSeqId}";
        await _fixture.PushAsync(MakeEvent(1, topic, pub: "pub-x", sub: "sub-y"));
        await _fixture.PushAsync(MakeEvent(100, topic, pub: "pub-x", sub: "sub-y"));

        var result = await _svc.GetGapsAsync(MakeQuery(topic: topic), CancellationToken.None);

        result.Gaps.Should().HaveCount(1);
        var g = result.Gaps[0];
        g.Topic.Should().Be(topic);
        g.PublisherNode.Should().Be("pub-x");
        g.SubscriberNode.Should().Be("sub-y");
        g.ResumedAtSequence.Should().Be(100UL);
        g.PreviousSequence.Should().Be(1UL);
        g.MissingCount.Should().Be(98UL);
    }

    [Fact]
    public async Task FirstEvent_NoGap_WhenSeqIsOne()
    {
        var topic = $"gap.first.{_baseSeqId}";
        // First event has sequence 1 (should NOT be a gap because prev_seq would be null and seq==1)
        await _fixture.PushAsync(MakeEvent(1, topic));

        var result = await _svc.GetGapsAsync(MakeQuery(topic: topic), CancellationToken.None);

        result.Gaps.Should().BeEmpty();
    }

    public async ValueTask DisposeAsync() => await _fixture.DisposeAsync();
}
