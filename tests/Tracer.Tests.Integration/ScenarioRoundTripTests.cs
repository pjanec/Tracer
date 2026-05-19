using FluentAssertions;
using Tracer.Core.Queries;
using Tracer.TestHarness;
using Tracer.TestHarness.Assertions;
using Xunit;

namespace Tracer.Tests.Integration;

/// <summary>
/// Tests that verify full write → flush → reopen → read round-trips
/// and that two runs with the same seed produce identical stored data.
/// </summary>
public sealed class ScenarioRoundTripTests
{
    [Fact]
    public async Task CalmScenario_WriteClosedReopened_QueryResultsIdentical()
    {
        await using var fixture = await TracerStackFixture.CreateAsync(
            "Calm", seed: 42, duration: TimeSpan.FromSeconds(30));
        await fixture.RunScenarioAsync();

        var queryFirst = await fixture.Reader!.QueryEventsAsync(
            new EventQuery { Filter = EventFilter.All, Limit = 10_000 },
            CancellationToken.None);

        await fixture.ReopenReaderAsync();

        var querySecond = await fixture.Reader!.QueryEventsAsync(
            new EventQuery { Filter = EventFilter.All, Limit = 10_000 },
            CancellationToken.None);

        querySecond.Should().HaveCount(queryFirst.Count,
            "reopened reader must return the same number of events");

        for (int i = 0; i < queryFirst.Count; i++)
        {
            querySecond[i].EventId.Should().Be(queryFirst[i].EventId,
                $"EventId must match at index {i} after reopen");
            querySecond[i].TraceId.Should().Be(queryFirst[i].TraceId,
                $"TraceId must match at index {i} after reopen");
            querySecond[i].PublishWallclock.Should().Be(queryFirst[i].PublishWallclock,
                $"PublishWallclock must match at index {i} after reopen");
        }
    }

    [Fact]
    public async Task CalmScenario_TwoRunsSameSeed_ProduceBytewiseSameEventData()
    {
        await using var fixtureA = await TracerStackFixture.CreateAsync(
            "Calm", seed: 42, duration: TimeSpan.FromSeconds(30));
        await fixtureA.RunScenarioAsync();

        await using var fixtureB = await TracerStackFixture.CreateAsync(
            "Calm", seed: 42, duration: TimeSpan.FromSeconds(30));
        await fixtureB.RunScenarioAsync();

        var eventsA = await fixtureA.Reader!.QueryEventsAsync(
            new EventQuery { Filter = EventFilter.All, Limit = 10_000 },
            CancellationToken.None);
        var eventsB = await fixtureB.Reader!.QueryEventsAsync(
            new EventQuery { Filter = EventFilter.All, Limit = 10_000 },
            CancellationToken.None);

        eventsA.Should().HaveCount(eventsB.Count,
            "two runs with the same seed must produce the same event count");

        for (int i = 0; i < eventsA.Count; i++)
        {
            var a = eventsA[i];
            var b = eventsB[i];
            a.EventId.Should().Be(b.EventId, $"EventId at index {i}");
            a.TraceId.Should().Be(b.TraceId, $"TraceId at index {i}");
            a.PublishWallclock.Should().Be(b.PublishWallclock, $"PublishWallclock at index {i}");
            a.Topic.Should().Be(b.Topic, $"Topic at index {i}");
            a.PayloadJson.Should().Be(b.PayloadJson, $"PayloadJson at index {i}");
        }
    }

    [Fact]
    public async Task CombatEngagement_AllParentEventIds_ReferenceExistingEvents()
    {
        await using var fixture = await TracerStackFixture.CreateAsync(
            "CombatEngagement", seed: 42, duration: TimeSpan.FromSeconds(30));
        await fixture.RunScenarioAsync();

        var allEvents = await fixture.Reader!.QueryEventsAsync(
            new EventQuery { Filter = EventFilter.All, Limit = 100_000 },
            CancellationToken.None);

        var traces = allEvents
            .GroupBy(e => e.TraceId)
            .Where(g => g.Count() > 1);

        foreach (var trace in traces)
            trace.ShouldFormValidTrace();
    }
}
