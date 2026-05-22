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

/// <summary>Tests for <see cref="EntityDiscoveryService"/> using real DuckDB storage.</summary>
public sealed class EntityDiscoveryServiceTests : IAsyncDisposable
{
    private readonly ObserverFixture _fixture;
    private readonly EntityDiscoveryService _svc;

    private static readonly DateTimeOffset BaseTime =
        new DateTimeOffset(2026, 7, 1, 10, 0, 0, TimeSpan.Zero);

    private static ulong _nextId = 70000;

    private static WallclockTime At(DateTimeOffset dto) =>
        WallclockTime.FromUnixNanoseconds(dto.ToUnixTimeMilliseconds() * 1_000_000L);

    private EventRecord MakeEvent(
        string? entityId = null,
        string topic = "game.tick",
        string? playerId = null,
        DateTimeOffset? at = null)
    {
        var id = _nextId++;
        return new EventRecord
        {
            SequenceNumber = id,
            PublishWallclock = At(at ?? BaseTime),
            ReceiveWallclock = At(at ?? BaseTime),
            PublisherNode = new AgentId("node-a"),
            SubscriberNode = new AgentId("node-a"),
            Topic = new TopicName(topic),
            EventId = new EventId(id),
            TraceId = new TraceId(id),
            PayloadJson = "{}",
            EntityId = entityId is not null ? new EntityId(entityId) : null,
            OwningPlayerId = playerId,
        };
    }

    public EntityDiscoveryServiceTests()
    {
        _fixture = ObserverFixture.CreateAsync().GetAwaiter().GetResult();
        _svc = _fixture.App.Services.GetRequiredService<EntityDiscoveryService>();
    }

    public async ValueTask DisposeAsync() => await _fixture.DisposeAsync();

    [Fact]
    public async Task DiscoverAsync_ThreeEntities_ReturnedOrderedByEventCount()
    {
        var events = new List<EventRecord>();
        for (int i = 0; i < 20; i++) events.Add(MakeEvent("ent-A-1"));
        for (int i = 0; i < 10; i++) events.Add(MakeEvent("ent-B-1"));
        for (int i = 0; i < 5; i++) events.Add(MakeEvent("ent-C-1"));
        for (int i = 0; i < 5; i++) events.Add(MakeEvent(entityId: null)); // null entity, excluded
        await _fixture.PushAsync(events);

        var result = await _svc.DiscoverAsync(
            "s1", At(BaseTime.AddSeconds(-1)), At(BaseTime.AddSeconds(10)),
            null, null, 100, CancellationToken.None);

        result.Should().HaveCountGreaterThanOrEqualTo(3);
        var a = result.First(e => e.EntityId == "ent-A-1");
        var b = result.First(e => e.EntityId == "ent-B-1");
        var c = result.First(e => e.EntityId == "ent-C-1");
        result.Should().NotContain(e => e.EntityId == null);
        var ordered = result.ToList();
        ordered.IndexOf(a).Should().BeLessThan(ordered.IndexOf(b));
        ordered.IndexOf(b).Should().BeLessThan(ordered.IndexOf(c));
        a.EventCount.Should().BeGreaterThanOrEqualTo(20);
    }

    [Fact]
    public async Task DiscoverAsync_TopicFilter_ExcludesOtherEntities()
    {
        var suffix = _nextId.ToString();
        var events = new List<EventRecord>();
        for (int i = 0; i < 10; i++) events.Add(MakeEvent($"ent-A-tf-{suffix}", topic: "pos.update"));
        for (int i = 0; i < 10; i++) events.Add(MakeEvent($"ent-B-tf-{suffix}", topic: "vel.update"));
        await _fixture.PushAsync(events);

        var result = await _svc.DiscoverAsync(
            "s1", At(BaseTime.AddSeconds(-1)), At(BaseTime.AddSeconds(10)),
            "pos.update", null, 100, CancellationToken.None);

        result.Should().Contain(e => e.EntityId == $"ent-A-tf-{suffix}");
        result.Should().NotContain(e => e.EntityId == $"ent-B-tf-{suffix}");
    }

    [Fact]
    public async Task DiscoverAsync_PlayerFilter_ExcludesOtherEntities()
    {
        var suffix = _nextId.ToString();
        var events = new List<EventRecord>();
        for (int i = 0; i < 10; i++) events.Add(MakeEvent($"ent-A-pf-{suffix}", playerId: "p1"));
        for (int i = 0; i < 10; i++) events.Add(MakeEvent($"ent-B-pf-{suffix}", playerId: "p2"));
        await _fixture.PushAsync(events);

        var result = await _svc.DiscoverAsync(
            "s1", At(BaseTime.AddSeconds(-1)), At(BaseTime.AddSeconds(10)),
            null, "p1", 100, CancellationToken.None);

        result.Should().Contain(e => e.EntityId == $"ent-A-pf-{suffix}");
        result.Should().NotContain(e => e.EntityId == $"ent-B-pf-{suffix}");
    }

    [Fact]
    public async Task DiscoverAsync_FirstAndLastSeen_CorrectBounds()
    {
        var suffix = _nextId.ToString();
        var t1 = BaseTime.AddMinutes(1);
        var t2 = BaseTime.AddMinutes(2);
        var t3 = BaseTime.AddMinutes(3);

        await _fixture.PushAsync([
            MakeEvent($"ent-X-bounds-{suffix}", at: t1),
            MakeEvent($"ent-X-bounds-{suffix}", at: t2),
            MakeEvent($"ent-X-bounds-{suffix}", at: t3),
        ]);

        var result = await _svc.DiscoverAsync(
            "s1", At(t1.AddSeconds(-1)), At(t3.AddSeconds(1)),
            null, null, 100, CancellationToken.None);

        var entity = result.FirstOrDefault(e => e.EntityId == $"ent-X-bounds-{suffix}");
        entity.Should().NotBeNull();
        entity!.FirstSeenUtc.Should().BeCloseTo(t1, TimeSpan.FromMilliseconds(100));
        entity.LastSeenUtc.Should().BeCloseTo(t3, TimeSpan.FromMilliseconds(100));
    }

    [Fact]
    public async Task DiscoverAsync_TopicsArray_DeduplicatedAndSorted()
    {
        var suffix = _nextId.ToString();
        await _fixture.PushAsync([
            MakeEvent($"ent-topics-{suffix}", topic: "b.ev"),
            MakeEvent($"ent-topics-{suffix}", topic: "a.ev"),
            MakeEvent($"ent-topics-{suffix}", topic: "a.ev"),
        ]);

        var result = await _svc.DiscoverAsync(
            "s1", At(BaseTime.AddSeconds(-1)), At(BaseTime.AddSeconds(10)),
            null, null, 100, CancellationToken.None);

        var entity = result.FirstOrDefault(e => e.EntityId == $"ent-topics-{suffix}");
        entity.Should().NotBeNull();
        entity!.Topics.Should().BeEquivalentTo(["a.ev", "b.ev"], opts => opts.WithStrictOrdering());
    }

    [Fact]
    public async Task DiscoverAsync_EmptySession_ReturnsEmptyList()
    {
        // Query a far-future time range with no data
        var result = await _svc.DiscoverAsync(
            "s1",
            At(new DateTimeOffset(2099, 1, 1, 0, 0, 0, TimeSpan.Zero)),
            At(new DateTimeOffset(2099, 1, 2, 0, 0, 0, TimeSpan.Zero)),
            null, null, 100, CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task DiscoverAsync_LimitRespected_ReturnsTruncatedCount()
    {
        var suffix = _nextId.ToString();
        var events = new List<EventRecord>();
        for (int i = 0; i < 10; i++)
            events.Add(MakeEvent($"ent-lim-{i}-{suffix}"));
        await _fixture.PushAsync(events);

        var result = await _svc.DiscoverAsync(
            "s1", At(BaseTime.AddSeconds(-1)), At(BaseTime.AddSeconds(10)),
            null, null, 3, CancellationToken.None);

        result.Count.Should().BeLessOrEqualTo(3);
    }

    [Fact]
    public async Task DiscoverAsync_TopicFilterSqlInjection_IsParameterized()
    {
        // Should not throw; events table still queryable afterward
        var act = async () => await _svc.DiscoverAsync(
            "s1", At(BaseTime.AddSeconds(-1)), At(BaseTime.AddSeconds(10)),
            "'; DROP TABLE events; --", null, 100, CancellationToken.None);

        await act.Should().NotThrowAsync();

        // events table still usable
        var result = await _svc.DiscoverAsync(
            "s1", At(BaseTime.AddSeconds(-1)), At(BaseTime.AddSeconds(10)),
            null, null, 100, CancellationToken.None);
        result.Should().NotBeNull();
    }
}
