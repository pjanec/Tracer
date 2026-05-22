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

/// <summary>Tests for <see cref="EntitySlowStateService"/> using real DuckDB storage.</summary>
public sealed class EntitySlowStateServiceTests : IAsyncLifetime
{
    private ObserverFixture _fixture = null!;
    private EntitySlowStateService _svc = null!;

    private static readonly DateTimeOffset BaseTime =
        new DateTimeOffset(2026, 7, 3, 10, 0, 0, TimeSpan.Zero);

    private static ulong _nextId = 90000;

    private static WallclockTime At(DateTimeOffset dto) =>
        WallclockTime.FromUnixNanoseconds(dto.ToUnixTimeMilliseconds() * 1_000_000L);

    private StateSampleRecord MakeState(
        string instanceKey,
        string topic,
        DateTimeOffset? at = null,
        ulong? traceId = null)
    {
        var id = _nextId++;
        var t = at ?? BaseTime;
        return new StateSampleRecord
        {
            SequenceNumber = id,
            PublishWallclock = At(t),
            ReceiveWallclock = At(t),
            PublisherNode = new AgentId("node-a"),
            SubscriberNode = new AgentId("node-a"),
            Topic = new TopicName(topic),
            InstanceKey = instanceKey,
            PayloadJson = "{}",
            Rate = StateSampleRate.Slow,
            TraceId = traceId.HasValue ? new TraceId(traceId.Value) : null,
        };
    }

    public async Task InitializeAsync()
    {
        _fixture = await ObserverFixture.CreateAsync();
        _svc = _fixture.App.Services.GetRequiredService<EntitySlowStateService>();
    }

    public async Task DisposeAsync() => await _fixture.DisposeAsync();

    [Fact]
    public async Task GetAsync_TwoTopics_ResultsGroupedCorrectly()
    {
        var suffix = _nextId.ToString();
        var entityId = $"ent-twotopics-{suffix}";
        for (int i = 0; i < 5; i++) await _fixture.PushStateAsync(MakeState(entityId, "pose"));
        for (int i = 0; i < 3; i++) await _fixture.PushStateAsync(MakeState(entityId, "health"));

        var result = await _svc.GetAsync(
            entityId,
            At(BaseTime.AddSeconds(-1)),
            At(BaseTime.AddSeconds(10)),
            null,
            CancellationToken.None);

        result.ByTopic.Keys.Should().BeEquivalentTo(["health", "pose"], opts => opts.WithStrictOrdering());
        result.ByTopic["pose"].Should().HaveCount(5);
        result.ByTopic["health"].Should().HaveCount(3);
    }

    [Fact]
    public async Task GetAsync_TopicFilter_ExcludesOtherTopics()
    {
        var suffix = _nextId.ToString();
        var entityId = $"ent-topicf-{suffix}";
        await _fixture.PushStateAsync(MakeState(entityId, "pose"));
        await _fixture.PushStateAsync(MakeState(entityId, "health"));

        var result = await _svc.GetAsync(
            entityId,
            At(BaseTime.AddSeconds(-1)),
            At(BaseTime.AddSeconds(10)),
            ["pose"],
            CancellationToken.None);

        result.ByTopic.Should().ContainKey("pose");
        result.ByTopic.Should().NotContainKey("health");
    }

    [Fact]
    public async Task GetAsync_SamplesOrderedByWallclockWithinTopic()
    {
        var suffix = _nextId.ToString();
        var entityId = $"ent-order-{suffix}";
        await _fixture.PushStateAsync(MakeState(entityId, "pose", at: BaseTime.AddSeconds(3)));
        await _fixture.PushStateAsync(MakeState(entityId, "pose", at: BaseTime.AddSeconds(1)));
        await _fixture.PushStateAsync(MakeState(entityId, "pose", at: BaseTime.AddSeconds(2)));

        var result = await _svc.GetAsync(
            entityId,
            At(BaseTime),
            At(BaseTime.AddSeconds(10)),
            null,
            CancellationToken.None);

        result.ByTopic.Should().ContainKey("pose");
        var samples = result.ByTopic["pose"];
        samples.Should().HaveCount(3);
        for (int i = 1; i < samples.Count; i++)
            samples[i].PublishWallclock.Should().BeGreaterThanOrEqualTo(samples[i - 1].PublishWallclock);
    }

    [Fact]
    public async Task GetAsync_EntityNotFound_ReturnsEmptyDictionary()
    {
        var result = await _svc.GetAsync(
            "nonexistent-entity-slow-xyz",
            At(BaseTime.AddSeconds(-1)),
            At(BaseTime.AddSeconds(10)),
            null,
            CancellationToken.None);

        result.ByTopic.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAsync_TraceIdZero_MappedAs0UL()
    {
        var suffix = _nextId.ToString();
        var entityId = $"ent-traceid0-{suffix}";
        await _fixture.PushStateAsync(MakeState(entityId, "pose", traceId: 0));

        var result = await _svc.GetAsync(
            entityId,
            At(BaseTime.AddSeconds(-1)),
            At(BaseTime.AddSeconds(10)),
            null,
            CancellationToken.None);

        result.ByTopic.Should().ContainKey("pose");
        result.ByTopic["pose"].Should().HaveCount(1);
        result.ByTopic["pose"][0].TraceId.Should().Be(0UL);
    }

    [Fact]
    public async Task GetAsync_TopicFilterSqlInjection_IsParameterized()
    {
        var act = async () => await _svc.GetAsync(
            "any-entity",
            At(BaseTime.AddSeconds(-1)),
            At(BaseTime.AddSeconds(10)),
            ["'; DROP TABLE slow_state; --"],
            CancellationToken.None);

        await act.Should().NotThrowAsync();

        // Confirm slow_state still works by doing a normal query
        var suffix = _nextId.ToString();
        var entityId = $"ent-inject-check-{suffix}";
        await _fixture.PushStateAsync(MakeState(entityId, "health"));
        var result = await _svc.GetAsync(
            entityId,
            At(BaseTime.AddSeconds(-1)),
            At(BaseTime.AddSeconds(10)),
            null,
            CancellationToken.None);
        result.ByTopic.Should().ContainKey("health");
    }
}
