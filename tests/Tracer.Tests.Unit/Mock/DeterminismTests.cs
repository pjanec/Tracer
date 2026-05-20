using FluentAssertions;
using Tracer.Adapters.Mock;
using Tracer.Adapters.Mock.Generation;
using Tracer.Adapters.Mock.Scenarios;
using Tracer.Core.Records;
using Tracer.Core.Time;
using Xunit;

namespace Tracer.Tests.Unit.Mock;

public sealed class DeterminismTests
{
    private static ScenarioConfig MakeConfig(int seed = 42, double durationSeconds = 30) =>
        new()
        {
            Seed = seed,
            Duration = TimeSpan.FromSeconds(durationSeconds),
        };

    // ── MockDataSource determinism ───────────────────────────────────────

    [Fact]
    public async Task MockDataSource_SameSeedSameScenario_ProducesIdenticalSequence()
    {
        var sourceA = new MockDataSource("Calm", MakeConfig(seed: 42));
        var sourceB = new MockDataSource("Calm", MakeConfig(seed: 42));

        var listA = await Collect<EventRecord>(sourceA);
        var listB = await Collect<EventRecord>(sourceB);

        listA.Should().HaveCount(listB.Count, "both sources use the same seed");

        for (int i = 0; i < listA.Count; i++)
        {
            var a = listA[i];
            var b = listB[i];
            a.EventId.Should().Be(b.EventId, $"EventId differs at index {i}");
            a.TraceId.Should().Be(b.TraceId, $"TraceId differs at index {i}");
            a.PublishWallclock.Should().Be(b.PublishWallclock, $"PublishWallclock differs at index {i}");
            a.Topic.Should().Be(b.Topic, $"Topic differs at index {i}");
            a.ScenarioPhase.Should().Be(b.ScenarioPhase, $"ScenarioPhase differs at index {i}");
            a.SequenceNumber.Should().Be(b.SequenceNumber, $"SequenceNumber differs at index {i}");
            a.PayloadJson.Should().Be(b.PayloadJson, $"PayloadJson differs at index {i}");
        }
    }

    [Fact]
    public async Task MockDataSource_DifferentSeeds_ProduceDifferentSequences()
    {
        var sourceA = new MockDataSource("Calm", MakeConfig(seed: 1));
        var sourceB = new MockDataSource("Calm", MakeConfig(seed: 2));

        var listA = await Collect<EventRecord>(sourceA);
        var listB = await Collect<EventRecord>(sourceB);

        // At minimum the first record's TraceId should differ.
        listA.Should().NotBeEmpty();
        listB.Should().NotBeEmpty();

        // Different seeds must diverge from the very first record.
        listA[0].TraceId.Should().NotBe(listB[0].TraceId,
            "different seeds should produce a different TraceId for the first record");
    }

    // ── TraceIdGenerator determinism ─────────────────────────────────────

    [Fact]
    public void TraceIdGenerator_SameSeed_ProducesSameTraceIds()
    {
        const int TraceCount = 5;
        var genA = new TraceIdGenerator(new Random(42));
        var genB = new TraceIdGenerator(new Random(42));

        var idsA = Enumerable.Range(0, TraceCount).Select(_ => genA.NewTrace()).ToList();
        var idsB = Enumerable.Range(0, TraceCount).Select(_ => genB.NewTrace()).ToList();

        idsA.Should().Equal(idsB, "generators seeded identically must produce identical trace IDs");
    }

    // ── SimulatedClock consistency ────────────────────────────────────────

    [Fact]
    public void SimulatedClock_AdvancesMatchAcrossRuns()
    {
        var initial = WallclockTime.FromDateTimeOffset(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var clockA = new SimulatedClock(initial);
        var clockB = new SimulatedClock(initial);

        var advances = new[] { 100, 200, 50, 300, 150 };
        foreach (var ms in advances)
        {
            clockA.Advance(TimeSpan.FromMilliseconds(ms));
            clockB.Advance(TimeSpan.FromMilliseconds(ms));

            clockA.Now.NanosecondsSinceEpoch.Should().Be(
                clockB.Now.NanosecondsSinceEpoch,
                $"clocks should be equal after advance of {ms}ms");
        }
    }

    // ── helpers ─────────────────────────────────────────────────────────

    private static async Task<List<T>> Collect<T>(MockDataSource source) where T : class
    {
        var results = new List<T>();
        await foreach (var record in source.ReadAsync())
            if (record is T typed)
                results.Add(typed);
        return results;
    }
}
