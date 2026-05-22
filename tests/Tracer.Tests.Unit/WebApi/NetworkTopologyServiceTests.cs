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

public sealed class NetworkTopologyServiceTests : IAsyncDisposable
{
    private readonly ObserverFixture _fixture;
    private readonly NetworkTopologyService _svc;

    private static readonly DateTimeOffset BaseTime =
        new DateTimeOffset(2026, 1, 10, 16, 0, 0, TimeSpan.Zero);

    private static ulong _nextId = 9_000_000;

    private static WallclockTime At(DateTimeOffset dto) =>
        WallclockTime.FromUnixNanoseconds(dto.ToUnixTimeMilliseconds() * 1_000_000L);

    private static EventRecord MakeEvent(
        string pub, string sub,
        string topic = "net.topic",
        DateTimeOffset? at = null)
    {
        var id = _nextId++;
        var publishAt = at ?? BaseTime.AddMilliseconds((long)(id % 60_000));
        return new EventRecord
        {
            SequenceNumber = id,
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

    public NetworkTopologyServiceTests()
    {
        _fixture = ObserverFixture.CreateAsync().GetAwaiter().GetResult();
        _svc = _fixture.App.Services.GetRequiredService<NetworkTopologyService>();
    }

    [Fact]
    public async Task EmptyBundle_EmptyTopology()
    {
        var result = await _svc.GetAsync(
            At(BaseTime.AddHours(-100)),
            At(BaseTime.AddHours(100)),
            CancellationToken.None);

        result.Edges.Should().BeEmpty();
        result.Nodes.Should().BeEmpty();
    }

    [Fact]
    public async Task SingleEdge_NodePopulated()
    {
        var topic = $"net.one.{_nextId}";
        await _fixture.PushAsync(MakeEvent("pub1", "sub1", topic));

        var result = await _svc.GetAsync(
            At(BaseTime.AddSeconds(-1)), At(BaseTime.AddHours(1)), CancellationToken.None);

        result.Edges.Should().ContainSingle(e => e.Topic == topic);
        result.Nodes.Should().Contain("pub1");
        result.Nodes.Should().Contain("sub1");
    }

    [Fact]
    public async Task Nodes_AreSorted()
    {
        var topic = $"net.sort.{_nextId}";
        await _fixture.PushAsync(MakeEvent("zzz", "aaa", topic));
        await _fixture.PushAsync(MakeEvent("mmm", "bbb", topic));

        var result = await _svc.GetAsync(
            At(BaseTime.AddSeconds(-1)), At(BaseTime.AddHours(1)), CancellationToken.None);

        var filtered = result.Nodes.Where(n =>
            n is "aaa" or "bbb" or "mmm" or "zzz").ToList();
        filtered.Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task SelfSubscribers_Excluded()
    {
        var topic = $"net.self.{_nextId}";
        var id = _nextId++;
        var selfEv = new EventRecord
        {
            SequenceNumber = id,
            PublishWallclock = At(BaseTime),
            ReceiveWallclock = At(BaseTime.AddMilliseconds(1)),
            PublisherNode = new AgentId("nodeX"),
            SubscriberNode = new AgentId("nodeX"),
            Topic = new TopicName(topic),
            EventId = new EventId(id),
            TraceId = new TraceId(id),
            PayloadJson = "{}",
        };
        await _fixture.PushAsync(selfEv);

        var result = await _svc.GetAsync(
            At(BaseTime.AddSeconds(-1)), At(BaseTime.AddHours(1)), CancellationToken.None);

        result.Edges.Should().NotContain(e => e.Topic == topic);
    }

    [Fact]
    public async Task MessageCount_Correct()
    {
        var topic = $"net.count.{_nextId}";
        for (var i = 0; i < 7; i++)
            await _fixture.PushAsync(MakeEvent("p", "s", topic));

        var result = await _svc.GetAsync(
            At(BaseTime.AddSeconds(-1)), At(BaseTime.AddHours(1)), CancellationToken.None);

        var edge = result.Edges.FirstOrDefault(e => e.Topic == topic);
        edge.Should().NotBeNull();
        edge!.MessageCount.Should().Be(7);
    }

    [Fact]
    public async Task FirstSeen_LastSeen_Correct()
    {
        var topic = $"net.seen.{_nextId}";
        var t1 = BaseTime;
        var t2 = BaseTime.AddMinutes(5);
        await _fixture.PushAsync(MakeEvent("p", "s", topic, t1));
        await _fixture.PushAsync(MakeEvent("p", "s", topic, t2));

        var result = await _svc.GetAsync(
            At(BaseTime.AddSeconds(-1)), At(BaseTime.AddHours(1)), CancellationToken.None);

        var edge = result.Edges.FirstOrDefault(e => e.Topic == topic);
        edge.Should().NotBeNull();
        edge!.FirstSeenUtc.Should().BeCloseTo(t1.UtcDateTime, TimeSpan.FromSeconds(1));
        edge.LastSeenUtc.Should().BeCloseTo(t2.UtcDateTime, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task MultipleTopics_AllEdgesReturned()
    {
        var topicA = $"net.ma.{_nextId}";
        var topicB = $"net.mb.{_nextId}";
        await _fixture.PushAsync(MakeEvent("p", "s", topicA));
        await _fixture.PushAsync(MakeEvent("p", "s", topicB));

        var result = await _svc.GetAsync(
            At(BaseTime.AddSeconds(-1)), At(BaseTime.AddHours(1)), CancellationToken.None);

        result.Edges.Select(e => e.Topic).Should().Contain(topicA);
        result.Edges.Select(e => e.Topic).Should().Contain(topicB);
    }

    public async ValueTask DisposeAsync() => await _fixture.DisposeAsync();
}
