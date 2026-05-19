using FluentAssertions;
using Tracer.Adapters.Mock;
using Tracer.Adapters.Mock.Scenarios;
using Tracer.Core.Records;
using Xunit;

namespace Tracer.Tests.Unit.Mock;

public sealed class ScenarioTests
{
    private const int DefaultSeed = 42;
    private const double SessionDurationSeconds = 60;
    private const double EventsPerSecond = 100;
    private const int CountLowerBound = 5950;
    private const int CountUpperBound = 6050;

    private static MockDataSource MakeSource(
        string scenario,
        int seed = DefaultSeed,
        double durationSeconds = SessionDurationSeconds,
        double eventsPerSecond = EventsPerSecond) =>
        new(scenario, new ScenarioConfig
        {
            Seed = seed,
            Duration = TimeSpan.FromSeconds(durationSeconds),
            EventsPerSecond = eventsPerSecond,
        });

    // ── CalmScenario ─────────────────────────────────────────────────────

    [Fact]
    public async Task CalmScenario_FirstRecord_IsSessionStart()
    {
        var source = MakeSource("Calm");

        EventRecord? first = null;
        await foreach (var record in source.ReadAsync())
        {
            first = record as EventRecord;
            break;
        }

        first.Should().NotBeNull("Calm scenario must emit at least one record");
        first!.Topic.Value.Should().Be("system.session_start",
            "the very first record must be the session-start marker");
    }

    [Fact]
    public async Task CalmScenario_Duration_TerminatesWithinConfiguredTime()
    {
        var source = MakeSource("Calm");
        var endDeadline = source.Clock.Now + TimeSpan.FromSeconds(SessionDurationSeconds + 1);

        EventRecord? last = null;
        await foreach (var record in source.ReadAsync())
            if (record is EventRecord ev)
                last = ev;

        last.Should().NotBeNull("Calm scenario must emit at least one record");
        last!.PublishWallclock.NanosecondsSinceEpoch
            .Should().BeLessThan(endDeadline.NanosecondsSinceEpoch,
                "the last event's publish time must be before StartTime + duration + 1 second");
    }

    [Fact]
    public async Task CalmScenario_EventCount_WithinTolerance()
    {
        var source = MakeSource("Calm", seed: DefaultSeed,
            durationSeconds: SessionDurationSeconds, eventsPerSecond: EventsPerSecond);

        int count = 0;
        await foreach (var record in source.ReadAsync())
            if (record is EventRecord)
                count++;

        count.Should().BeInRange(CountLowerBound, CountUpperBound,
            $"100 eps × 60 s ≈ 6000 events (±50), got {count}");
    }

    // ── CombatEngagementScenario ──────────────────────────────────────────

    [Fact]
    public async Task CombatEngagement_CausalTrees_AreValid()
    {
        var source = MakeSource("CombatEngagement", durationSeconds: 30);

        var seenEventIds = new HashSet<Tracer.Core.Identity.EventId>();
        await foreach (var record in source.ReadAsync())
        {
            if (record is EventRecord ev)
            {
                if (ev.ParentEventId is { } parentId)
                {
                    seenEventIds.Should().Contain(parentId,
                        $"event {ev.EventId} references parent {parentId} which has not been yielded yet");
                }
                seenEventIds.Add(ev.EventId);
            }
        }
    }

    [Fact]
    public async Task CombatEngagement_AllEvents_HaveNonNullScenarioPhase()
    {
        var source = MakeSource("CombatEngagement", durationSeconds: 30);

        await foreach (var record in source.ReadAsync())
        {
            if (record is EventRecord ev)
            {
                ev.ScenarioPhase.Should().NotBeNull(
                    $"event {ev.EventId} on topic '{ev.Topic.Value}' must have a non-null ScenarioPhase");
            }
        }
    }

    // ── ScenarioRegistry ─────────────────────────────────────────────────

    [Fact]
    public void ScenarioRegistry_Get_UnknownName_ThrowsArgumentException()
    {
        var act = () => Tracer.Adapters.Mock.Scenarios.ScenarioRegistry.Get("NonExistent");
        act.Should().Throw<ArgumentException>()
            .WithMessage("*NonExistent*");
    }
}
