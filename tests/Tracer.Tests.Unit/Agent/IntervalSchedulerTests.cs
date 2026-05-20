using FluentAssertions;
using Tracer.Agent.Configuration;
using Tracer.Agent.Lifecycle;
using Tracer.Core.Time;
using Xunit;

namespace Tracer.Tests.Unit.Agent;

public sealed class IntervalSchedulerTests
{
    private sealed class FakeClock(WallclockTime now) : IClock
    {
        public WallclockTime Now => now;
    }

    private static AgentConfig HourlyConfig() => new()
    {
        NodeId = "n",
        DataRoot = @"C:\d",
        LogsRoot = @"C:\l",
        IntervalDuration = TimeSpan.FromHours(1),
    };

    private static AgentConfig HalfHourConfig() => new()
    {
        NodeId = "n",
        DataRoot = @"C:\d",
        LogsRoot = @"C:\l",
        IntervalDuration = TimeSpan.FromMinutes(30),
    };

    [Fact]
    public void IntervalScheduler_AtHourBoundary_CurrentStartEqualsNow()
    {
        var boundary = new DateTimeOffset(2026, 5, 19, 14, 0, 0, TimeSpan.Zero);
        var clock = new FakeClock(WallclockTime.FromDateTimeOffset(boundary));
        var scheduler = new IntervalScheduler(clock, HourlyConfig());

        scheduler.CurrentIntervalStart().Should().Be(boundary);
    }

    [Fact]
    public void IntervalScheduler_BetweenBoundaries_CurrentStartIsPriorHour()
    {
        var halfPast = new DateTimeOffset(2026, 5, 19, 14, 30, 0, TimeSpan.Zero);
        var expected = new DateTimeOffset(2026, 5, 19, 14, 0, 0, TimeSpan.Zero);
        var clock = new FakeClock(WallclockTime.FromDateTimeOffset(halfPast));
        var scheduler = new IntervalScheduler(clock, HourlyConfig());

        scheduler.CurrentIntervalStart().Should().Be(expected);
    }

    [Fact]
    public void IntervalScheduler_NextBoundary_IsOneHourAfterCurrentStart()
    {
        var halfPast = new DateTimeOffset(2026, 5, 19, 14, 30, 0, TimeSpan.Zero);
        var expectedNext = new DateTimeOffset(2026, 5, 19, 15, 0, 0, TimeSpan.Zero);
        var clock = new FakeClock(WallclockTime.FromDateTimeOffset(halfPast));
        var scheduler = new IntervalScheduler(clock, HourlyConfig());

        scheduler.NextIntervalBoundary().Should().Be(expectedNext);
    }

    [Fact]
    public void IntervalScheduler_30MinDuration_ConstructsWithoutError()
    {
        var clock = new FakeClock(WallclockTime.Zero);
        var act = () => new IntervalScheduler(clock, HalfHourConfig());
        act.Should().NotThrow();
    }

    [Fact]
    public void IntervalScheduler_NonDivisibleDuration_Throws()
    {
        var config = new AgentConfig
        {
            NodeId = "n",
            DataRoot = @"C:\d",
            LogsRoot = @"C:\l",
            IntervalDuration = TimeSpan.FromMinutes(11),
        };
        var clock = new FakeClock(WallclockTime.Zero);
        var act = () => new IntervalScheduler(clock, config);
        act.Should().Throw<ArgumentException>();
    }

    // DT-006 fixes ────────────────────────────────────────────────────────────

    [Fact]
    public void IntervalScheduler_LessThanOneMinute_Throws()
    {
        var config = new AgentConfig
        {
            NodeId = "n",
            DataRoot = @"C:\d",
            LogsRoot = @"C:\l",
            IntervalDuration = TimeSpan.FromSeconds(30),
        };
        var clock = new FakeClock(WallclockTime.Zero);
        var act = () => new IntervalScheduler(clock, config);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void IntervalScheduler_TimeUntilNextBoundary_DecreasesAsClockAdvances()
    {
        var t1 = new DateTimeOffset(2026, 5, 19, 14, 30, 0, TimeSpan.Zero);
        var clock1 = new FakeClock(WallclockTime.FromDateTimeOffset(t1));
        var scheduler1 = new IntervalScheduler(clock1, HourlyConfig());
        var remaining1 = scheduler1.TimeUntilNextBoundary();

        var t2 = new DateTimeOffset(2026, 5, 19, 14, 45, 0, TimeSpan.Zero);
        var clock2 = new FakeClock(WallclockTime.FromDateTimeOffset(t2));
        var scheduler2 = new IntervalScheduler(clock2, HourlyConfig());
        var remaining2 = scheduler2.TimeUntilNextBoundary();

        remaining2.Should().BeLessThan(remaining1);
    }

    [Fact]
    public void IntervalScheduler_24HourDuration_DoesNotThrow()
    {
        var config = new AgentConfig
        {
            NodeId = "n",
            DataRoot = @"C:\d",
            LogsRoot = @"C:\l",
            IntervalDuration = TimeSpan.FromHours(24),
        };
        var clock = new FakeClock(WallclockTime.Zero);
        var act = () => new IntervalScheduler(clock, config);
        act.Should().NotThrow();
    }
}

