using FluentAssertions;
using Tracer.Adapters.Mock;
using Tracer.Core.Time;
using Tracer.Observer.Lifecycle;
using Xunit;

namespace Tracer.Tests.Unit.Observer;

public sealed class ObserverStateReporterTests
{
    [Fact]
    public void IncrementIngested_UpdatesAllCounters()
    {
        var reporter = new ObserverStateReporter();
        reporter.IncrementIngested();
        reporter.IncrementIngested();

        var snap = reporter.Snapshot();
        snap.IngestedTotal.Should().Be(2);
        snap.IngestedLastMinute.Should().Be(2);
        snap.LastEventUtc.Should().NotBeNull();
    }

    [Fact]
    public void IncrementDropped_UpdatesDroppedOnly()
    {
        var reporter = new ObserverStateReporter();
        reporter.IncrementDropped();
        reporter.IncrementDropped();
        reporter.IncrementDropped();

        var snap = reporter.Snapshot();
        snap.DroppedTotal.Should().Be(3);
        snap.IngestedTotal.Should().Be(0);
        snap.IngestedLastMinute.Should().Be(0);
    }

    [Fact]
    public void Snapshot_ReflectsCurrentState()
    {
        var reporter = new ObserverStateReporter();

        var initial = reporter.Snapshot();
        initial.IngestedTotal.Should().Be(0);
        initial.DroppedTotal.Should().Be(0);
        initial.LastEventUtc.Should().BeNull();

        reporter.IncrementIngested();
        reporter.IncrementDropped();

        var after = reporter.Snapshot();
        after.IngestedTotal.Should().Be(1);
        after.DroppedTotal.Should().Be(1);
        after.LastEventUtc.Should().NotBeNull();
    }

    [Fact]
    public void RollingCounter_ReturnsZeroAfterWindowElapsed()
    {
        var clock = new SimulatedClock(WallclockTime.FromDateTimeOffset(DateTimeOffset.UtcNow));
        var reporter = new ObserverStateReporter(clock);

        reporter.IncrementIngested(); // increments _ingestedLastMinute

        // Advance clock by 2 minutes — beyond the 1-minute window
        clock.Advance(TimeSpan.FromMinutes(2));

        // The rolling counter should now report 0 (old bucket cleared)
        reporter.Snapshot().IngestedLastMinute.Should().Be(0);
    }

    [Fact]
    public void RollingCounter_SumsMultipleBucketsWithinWindow()
    {
        var clock = new SimulatedClock(WallclockTime.FromDateTimeOffset(DateTimeOffset.UtcNow));
        var reporter = new ObserverStateReporter(clock);

        reporter.IncrementIngested();
        clock.Advance(TimeSpan.FromSeconds(20));
        reporter.IncrementIngested();
        clock.Advance(TimeSpan.FromSeconds(20));
        reporter.IncrementIngested();

        // All 3 increments are within 1 minute window
        reporter.Snapshot().IngestedLastMinute.Should().Be(3);
    }
}
