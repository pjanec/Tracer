using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Tracer.AdapterSelection;
using Tracer.Agent.Time;
using Xunit;

namespace Tracer.Tests.Unit.AdapterSelection;

/// <summary>Unit tests for <see cref="SystemClock"/> TimeProvider injection (FIX-A1).</summary>
public sealed class SystemClockTests
{
    // ── AdapterSelection.SystemClock ─────────────────────────────────────────

    [Fact]
    public void AdapterSelectionSystemClock_Now_ReflectsFakeTimeProvider()
    {
        var fake = new FakeTimeProvider();
        var expected = DateTimeOffset.UtcNow;
        fake.SetUtcNow(expected);

        var clock = new Tracer.AdapterSelection.SystemClock(fake);
        clock.Now.ToDateTimeOffset().Should().Be(expected);
    }

    [Fact]
    public void AdapterSelectionSystemClock_Now_AdvancesWhenFakeTimeAdvances()
    {
        var fake = new FakeTimeProvider();
        var start = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        fake.SetUtcNow(start);
        var clock = new Tracer.AdapterSelection.SystemClock(fake);

        var before = clock.Now;
        fake.Advance(TimeSpan.FromSeconds(30));
        var after = clock.Now;

        after.ToDateTimeOffset().Should().Be(start.AddSeconds(30));
        after.ToDateTimeOffset().Should().BeAfter(before.ToDateTimeOffset());
    }

    [Fact]
    public void AdapterSelectionSystemClock_Constructor_ThrowsForNullTimeProvider()
    {
        var act = () => new Tracer.AdapterSelection.SystemClock(null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("timeProvider");
    }

    [Fact]
    public void AdapterSelectionSystemClock_WithSystemTimeProvider_ReturnsCurrentTime()
    {
        var before = DateTimeOffset.UtcNow;
        var clock = new Tracer.AdapterSelection.SystemClock(TimeProvider.System);
        var now = clock.Now.ToDateTimeOffset();
        var after = DateTimeOffset.UtcNow;

        now.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
    }

    // ── Tracer.Agent.Time.SystemClock ────────────────────────────────────────

    [Fact]
    public void AgentSystemClock_Now_ReflectsFakeTimeProvider()
    {
        var fake = new FakeTimeProvider();
        var expected = new DateTimeOffset(2025, 6, 15, 12, 0, 0, TimeSpan.Zero);
        fake.SetUtcNow(expected);

        var clock = new Tracer.Agent.Time.SystemClock(fake);
        clock.Now.ToDateTimeOffset().Should().Be(expected);
    }

    [Fact]
    public void AgentSystemClock_Now_AdvancesWhenFakeTimeAdvances()
    {
        var fake = new FakeTimeProvider();
        var start = new DateTimeOffset(2024, 3, 10, 8, 0, 0, TimeSpan.Zero);
        fake.SetUtcNow(start);
        var clock = new Tracer.Agent.Time.SystemClock(fake);

        fake.Advance(TimeSpan.FromMinutes(5));
        clock.Now.ToDateTimeOffset().Should().Be(start.AddMinutes(5));
    }

    [Fact]
    public void AgentSystemClock_Constructor_ThrowsForNullTimeProvider()
    {
        var act = () => new Tracer.Agent.Time.SystemClock(null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("timeProvider");
    }

    [Fact]
    public void AgentSystemClock_WithSystemTimeProvider_ReturnsCurrentTime()
    {
        var before = DateTimeOffset.UtcNow;
        var clock = new Tracer.Agent.Time.SystemClock(TimeProvider.System);
        var now = clock.Now.ToDateTimeOffset();
        var after = DateTimeOffset.UtcNow;

        now.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
    }
}
