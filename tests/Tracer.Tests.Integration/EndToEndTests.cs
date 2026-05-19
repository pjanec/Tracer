using FluentAssertions;
using Tracer.Core.Queries;
using Tracer.Core.Records;
using Tracer.TestHarness;
using Tracer.TestHarness.Assertions;
using Xunit;

namespace Tracer.Tests.Integration;

/// <summary>
/// End-to-end integration tests: full write → flush → read round-trips via
/// <see cref="TracerStackFixture"/>.
/// </summary>
public sealed class EndToEndTests : IAsyncLifetime
{
    private TracerStackFixture _fixture = null!;

    public async Task InitializeAsync()
    {
        _fixture = await TracerStackFixture.CreateAsync(
            "Calm",
            seed: 42,
            duration: TimeSpan.FromMinutes(1));
        await _fixture.RunScenarioAsync();
    }

    public async Task DisposeAsync() => await _fixture.DisposeAsync();

    // ── tests ────────────────────────────────────────────────────────────

    [Fact]
    public async Task CalmScenario_1Minute_QueryReturnsExpectedEventCount()
    {
        var count = await _fixture.Reader!.CountEventsAsync(EventFilter.All, CancellationToken.None);
        count.Should().Be(_fixture.EventsWrittenCount,
            "CountEventsAsync must equal the number of events written during the scenario");
    }

    [Fact]
    public async Task CombatEngagement_QueryByTraceId_ReturnsValidCausalTree()
    {
        await using var combatFixture = await TracerStackFixture.CreateAsync(
            "CombatEngagement",
            seed: 42,
            duration: TimeSpan.FromSeconds(30));
        await combatFixture.RunScenarioAsync();

        // Find any trace_id that has more than one event.
        var allEvents = await combatFixture.Reader!.QueryEventsAsync(
            new EventQuery { Filter = EventFilter.All, Limit = 10_000 },
            CancellationToken.None);

        var traceId = allEvents
            .GroupBy(e => e.TraceId)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .FirstOrDefault();

        traceId.Should().NotBe(default, "CombatEngagement must produce multi-event traces");

        var trace = await combatFixture.Reader!.QueryEventsAsync(
            new EventQuery { Filter = EventFilter.ForTrace(traceId), Limit = 100 },
            CancellationToken.None);

        trace.ShouldFormValidTrace();
    }

    [Fact]
    public async Task QueryByEntity_ReturnsOnlyMatchingEntity()
    {
        var allEvents = await _fixture.Reader!.QueryEventsAsync(
            new EventQuery { Filter = EventFilter.All, Limit = 10_000 },
            CancellationToken.None);

        var targetEntityId = allEvents
            .Where(e => e.EntityId.HasValue)
            .Select(e => e.EntityId!.Value)
            .FirstOrDefault();

        targetEntityId.Value.Should().NotBeNullOrEmpty("there must be at least one event with an EntityId");

        var filtered = await _fixture.Reader!.QueryEventsAsync(
            new EventQuery { Filter = EventFilter.ForEntity(targetEntityId), Limit = 10_000 },
            CancellationToken.None);

        filtered.Should().NotBeEmpty("filter by EntityId must return matching events");
        filtered.Should().OnlyContain(e => e.EntityId == targetEntityId,
            "all returned events must match the queried entity");
    }

    [Fact]
    public async Task QueryWithTimeRange_ReturnsOnlyEventsInRange()
    {
        var allEvents = await _fixture.Reader!.QueryEventsAsync(
            new EventQuery { Filter = EventFilter.All, Limit = 10_000 },
            CancellationToken.None);

        allEvents.Should().HaveCountGreaterThan(0);

        var rangeFrom = allEvents[allEvents.Count / 4].PublishWallclock;
        var rangeTo = allEvents[allEvents.Count * 3 / 4].PublishWallclock;

        var filtered = await _fixture.Reader!.QueryEventsAsync(new EventQuery
        {
            Filter = new EventFilter { From = rangeFrom, To = rangeTo },
            Limit = 10_000,
        }, CancellationToken.None);

        filtered.Should().NotBeEmpty("the time range must contain events");
        filtered.Should().OnlyContain(e =>
            e.PublishWallclock.NanosecondsSinceEpoch >= rangeFrom.NanosecondsSinceEpoch &&
            e.PublishWallclock.NanosecondsSinceEpoch < rangeTo.NanosecondsSinceEpoch,
            "all returned events must fall within [From, To)");
    }

    [Fact]
    public async Task QueryWithLimit_RespectsLimit()
    {
        const int RequestedLimit = 10;

        var results = await _fixture.Reader!.QueryEventsAsync(
            new EventQuery { Filter = EventFilter.All, Limit = RequestedLimit },
            CancellationToken.None);

        results.Should().HaveCount(RequestedLimit,
            "the Limit parameter must be honoured exactly when there are sufficient events");
    }

    [Fact]
    public async Task GetEventAsync_KnownEventId_ReturnsMatchingEvent()
    {
        var first = (await _fixture.Reader!.QueryEventsAsync(
            new EventQuery { Filter = EventFilter.All, Limit = 1 },
            CancellationToken.None))[0];

        var retrieved = await _fixture.Reader!.GetEventAsync(first.EventId, CancellationToken.None);

        retrieved.Should().NotBeNull("a stored event must be retrievable by its EventId");
        retrieved!.EventId.Should().Be(first.EventId);
        retrieved!.TraceId.Should().Be(first.TraceId);
    }

    [Fact]
    public async Task CountEventsAsync_MatchesFullQueryCount()
    {
        var count = await _fixture.Reader!.CountEventsAsync(EventFilter.All, CancellationToken.None);

        var queried = await _fixture.Reader!.QueryEventsAsync(
            new EventQuery { Filter = EventFilter.All, Limit = (int)count + 1 },
            CancellationToken.None);

        count.Should().Be(queried.Count,
            "CountEventsAsync must equal the number of rows returned by an unbounded query");
    }
}
