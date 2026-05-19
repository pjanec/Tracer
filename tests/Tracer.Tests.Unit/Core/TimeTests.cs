using FluentAssertions;
using Tracer.Adapters.Mock;
using Tracer.Core.Time;
using Xunit;

namespace Tracer.Tests.Unit.Core;

public sealed class TimeTests
{
    // ── SimulatedClock tests ─────────────────────────────────────────────

    [Fact]
    public void SimulatedClock_AdvancesExactly_WhenTold()
    {
        var start = WallclockTime.FromDateTimeOffset(DateTimeOffset.UtcNow);
        var clock = new SimulatedClock(start);

        clock.Advance(TimeSpan.FromSeconds(1));

        var expectedNs = start.NanosecondsSinceEpoch + 1_000_000_000L;
        clock.Now.NanosecondsSinceEpoch.Should().Be(expectedNs);
    }

    [Fact]
    public void SimulatedClock_DoesNotAdvanceSpontaneously()
    {
        var clock = new SimulatedClock();
        var first = clock.Now;
        var second = clock.Now;

        second.NanosecondsSinceEpoch.Should().Be(first.NanosecondsSinceEpoch);
    }

    [Fact]
    public void SimulatedClock_TwoInstancesAtSameInitial_ReturnSameNow()
    {
        var initial = WallclockTime.FromDateTimeOffset(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var clockA = new SimulatedClock(initial);
        var clockB = new SimulatedClock(initial);

        clockA.Now.NanosecondsSinceEpoch.Should().Be(clockB.Now.NanosecondsSinceEpoch);
    }

    [Fact]
    public void SimulatedClock_Set_ReplacesCurrentTime()
    {
        var clock = new SimulatedClock();
        var target = WallclockTime.FromDateTimeOffset(new DateTimeOffset(2025, 6, 15, 12, 0, 0, TimeSpan.Zero));

        clock.Set(target);

        clock.Now.NanosecondsSinceEpoch.Should().Be(target.NanosecondsSinceEpoch);
    }

    // ── WallclockTime tests ──────────────────────────────────────────────

    [Fact]
    public void WallclockTime_CompareTo_ConsistentWithLongCompare()
    {
        var earlier = new WallclockTime(100L);
        var later = new WallclockTime(200L);
        var same = new WallclockTime(100L);

        earlier.CompareTo(later).Should().BeNegative(
            "earlier.CompareTo(later) should be negative like long 100.CompareTo(200)");
        later.CompareTo(earlier).Should().BePositive(
            "later.CompareTo(earlier) should be positive like long 200.CompareTo(100)");
        earlier.CompareTo(same).Should().Be(0,
            "equal times should compare to zero");
    }
}
