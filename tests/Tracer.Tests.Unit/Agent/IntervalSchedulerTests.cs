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
}
