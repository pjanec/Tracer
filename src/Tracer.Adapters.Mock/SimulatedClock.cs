using Tracer.Core.Time;

namespace Tracer.Adapters.Mock;

/// <summary>
/// A deterministic clock whose time can be advanced programmatically.
/// </summary>
public sealed class SimulatedClock : IClock
{
    private long _nanosecondsSinceEpoch;

    /// <summary>Initialises the clock at the given start time.</summary>
    public SimulatedClock(WallclockTime startTime)
    {
        _nanosecondsSinceEpoch = startTime.NanosecondsSinceEpoch;
    }

    /// <summary>Initialises the clock at the Unix epoch.</summary>
    public SimulatedClock() : this(new WallclockTime(0)) { }

    /// <inheritdoc/>
    public WallclockTime Now => new WallclockTime(Volatile.Read(ref _nanosecondsSinceEpoch));

    /// <summary>Advances the clock by <paramref name="delta"/>.</summary>
    public void Advance(TimeSpan delta)
    {
        Interlocked.Add(ref _nanosecondsSinceEpoch, delta.Ticks * 100L);
    }

    /// <summary>Sets the clock to an absolute time.</summary>
    public void Set(WallclockTime time)
    {
        Volatile.Write(ref _nanosecondsSinceEpoch, time.NanosecondsSinceEpoch);
    }
}
