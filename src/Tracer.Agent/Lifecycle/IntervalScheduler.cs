using Tracer.Agent.Configuration;
using Tracer.Core.Time;

namespace Tracer.Agent.Lifecycle;

public sealed class IntervalScheduler
{
    private readonly IClock _clock;
    private readonly TimeSpan _duration;

    public IntervalScheduler(IClock clock, AgentConfig config)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(config);
        if (config.IntervalDuration < TimeSpan.FromMinutes(1))
            throw new ArgumentOutOfRangeException(nameof(config),
                "Interval duration must be at least 1 minute.");
        if (config.IntervalDuration > TimeSpan.FromHours(24))
            throw new ArgumentOutOfRangeException(nameof(config),
                "Interval duration must not exceed 24 hours.");
        if (TimeSpan.FromDays(1).Ticks % config.IntervalDuration.Ticks != 0)
            throw new ArgumentException(
                "Interval duration must evenly divide 24 hours.", nameof(config));

        _clock = clock;
        _duration = config.IntervalDuration;
    }

    /// <summary>
    /// Returns the UTC start of the interval that contains the current clock time.
    /// </summary>
    public DateTimeOffset CurrentIntervalStart()
    {
        var now = _clock.Now.ToDateTimeOffset();
        var midnightUtc = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);
        var secondsSinceMidnight = (long)now.UtcDateTime.TimeOfDay.TotalSeconds;
        var durationSeconds = (long)_duration.TotalSeconds;
        var index = secondsSinceMidnight / durationSeconds;
        return midnightUtc.AddSeconds(index * durationSeconds);
    }

    /// <summary>Returns the UTC start of the next interval.</summary>
    public DateTimeOffset NextIntervalBoundary()
        => CurrentIntervalStart() + _duration;

    /// <summary>Returns how long until the next interval boundary (never negative).</summary>
    public TimeSpan TimeUntilNextBoundary()
    {
        var remaining = NextIntervalBoundary() - _clock.Now.ToDateTimeOffset();
        return remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining;
    }
}
