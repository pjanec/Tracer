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

/// <summary>Tests for <see cref="EntityEventsService"/> using real DuckDB storage.</summary>
public sealed class EntityEventsServiceTests : IAsyncLifetime
{
    private ObserverFixture _fixture = null!;
    private EntityEventsService _svc = null!;

    private static readonly DateTimeOffset BaseTime =
        new DateTimeOffset(2026, 7, 2, 10, 0, 0, TimeSpan.Zero);

    private static ulong _nextId = 80000;

    private static WallclockTime At(DateTimeOffset dto) =>
        WallclockTime.FromUnixNanoseconds(dto.ToUnixTimeMilliseconds() * 1_000_000L);

    private EventRecord MakeEvent(string entityId, DateTimeOffset? at = null)
    {
        var id = _nextId++;
        var t = at ?? BaseTime;
        return new EventRecord
        {
            SequenceNumber = id,
            PublishWallclock = At(t),
            ReceiveWallclock = At(t),
            PublisherNode = new AgentId("node-a"),
            SubscriberNode = new AgentId("node-a"),
            Topic = new TopicName("game.tick"),
            EventId = new EventId(id),
            TraceId = new TraceId(id),
            PayloadJson = "{}",
            EntityId = new EntityId(entityId),
        };
    }

    public async Task InitializeAsync()
    {
        _fixture = await ObserverFixture.CreateAsync();
        _svc = _fixture.App.Services.GetRequiredService<EntityEventsService>();
    }

    public async Task DisposeAsync() => await _fixture.DisposeAsync();

    [Fact]
    public async Task GetEventsAsync_FiveEventsForEntity_ReturnsAll()
    {
        var suffix = _nextId.ToString();
        var events = new List<EventRecord>();
        for (int i = 0; i < 5; i++) events.Add(MakeEvent($"ent-A-{suffix}"));
        for (int i = 0; i < 5; i++) events.Add(MakeEvent($"ent-B-{suffix}"));
        await _fixture.PushAsync(events);

        var result = await _svc.GetEventsAsync(
            $"ent-A-{suffix}",
            At(BaseTime.AddSeconds(-1)),
            At(BaseTime.AddSeconds(10)),
            100,
            CancellationToken.None);

        result.Events.Should().HaveCount(5);
        result.Events.Should().OnlyContain(e => e.EntityId!.Value.Value == $"ent-A-{suffix}");
        result.Truncated.Should().BeFalse();
    }

    [Fact]
    public async Task GetEventsAsync_ExceedsLimit_TruncatesAndSetsFlag()
    {
        var suffix = _nextId.ToString();
        var events = new List<EventRecord>();
        for (int i = 0; i < 11; i++) events.Add(MakeEvent($"ent-trunc-{suffix}"));
        await _fixture.PushAsync(events);

        var result = await _svc.GetEventsAsync(
            $"ent-trunc-{suffix}",
            At(BaseTime.AddSeconds(-1)),
            At(BaseTime.AddSeconds(10)),
            10,
            CancellationToken.None);

        result.Events.Count.Should().Be(10);
        result.Truncated.Should().BeTrue();
    }

    [Fact]
    public async Task GetEventsAsync_ExactlyAtLimit_NotTruncated()
    {
        var suffix = _nextId.ToString();
        var events = new List<EventRecord>();
        for (int i = 0; i < 10; i++) events.Add(MakeEvent($"ent-exact-{suffix}"));
        await _fixture.PushAsync(events);

        var result = await _svc.GetEventsAsync(
            $"ent-exact-{suffix}",
            At(BaseTime.AddSeconds(-1)),
            At(BaseTime.AddSeconds(10)),
            10,
            CancellationToken.None);

        result.Events.Count.Should().Be(10);
        result.Truncated.Should().BeFalse();
    }

    [Fact]
    public async Task GetEventsAsync_OrderedByWallclockAscending()
    {
        var suffix = _nextId.ToString();
        var events = new List<EventRecord>
        {
            MakeEvent($"ent-ord-{suffix}", at: BaseTime.AddSeconds(3)),
            MakeEvent($"ent-ord-{suffix}", at: BaseTime.AddSeconds(1)),
            MakeEvent($"ent-ord-{suffix}", at: BaseTime.AddSeconds(2)),
        };
        await _fixture.PushAsync(events);

        var result = await _svc.GetEventsAsync(
            $"ent-ord-{suffix}",
            At(BaseTime),
            At(BaseTime.AddSeconds(10)),
            100,
            CancellationToken.None);

        result.Events.Should().HaveCount(3);
        for (int i = 1; i < result.Events.Count; i++)
            result.Events[i].PublishWallclock.Should().BeGreaterThanOrEqualTo(result.Events[i - 1].PublishWallclock);
    }

    [Fact]
    public async Task GetEventsAsync_EntityNotFound_ReturnsEmptyNotTruncated()
    {
        var result = await _svc.GetEventsAsync(
            "nonexistent-entity-xyz-12345",
            At(BaseTime.AddSeconds(-1)),
            At(BaseTime.AddSeconds(10)),
            100,
            CancellationToken.None);

        result.Events.Should().BeEmpty();
        result.Truncated.Should().BeFalse();
    }

    [Fact]
    public async Task GetEventsAsync_EmptyTimeRange_ReturnsEmpty()
    {
        var result = await _svc.GetEventsAsync(
            "any-entity",
            At(BaseTime),
            At(BaseTime), // from == to
            100,
            CancellationToken.None);

        result.Events.Should().BeEmpty();
        result.Truncated.Should().BeFalse();
    }

    [Fact]
    public async Task GetEventsAsync_EntityIdIsParameter_NeverInterpolated()
    {
        // Ensure injection attempt doesn't throw and the events table remains intact
        var act = async () => await _svc.GetEventsAsync(
            "'; DROP TABLE events; --",
            At(BaseTime.AddSeconds(-1)),
            At(BaseTime.AddSeconds(10)),
            10,
            CancellationToken.None);

        await act.Should().NotThrowAsync();

        // Normal query still works
        var r2 = await _svc.GetEventsAsync(
            "nonexistent",
            At(BaseTime.AddSeconds(-1)),
            At(BaseTime.AddSeconds(10)),
            10,
            CancellationToken.None);
        r2.Events.Should().BeEmpty();
    }
}
