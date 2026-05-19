namespace Tracer.Core.Time;

/// <summary>
/// A point in time on the cluster's synchronized wall-clock.
/// Stored as nanoseconds since Unix epoch UTC.
/// </summary>
public readonly record struct WallclockTime(long NanosecondsSinceEpoch)
    : IComparable<WallclockTime>
{
    private static readonly long UnixEpochTicks = DateTime.UnixEpoch.Ticks;
    private const long TicksToNanoseconds = 100L;

    /// <summary>The zero point (Unix epoch).</summary>
    public static WallclockTime Zero => new(0);

    /// <summary>The maximum representable wallclock time.</summary>
    public static WallclockTime MaxValue => new(long.MaxValue);

    /// <summary>Creates a <see cref="WallclockTime"/> from a nanosecond count.</summary>
    public static WallclockTime FromUnixNanoseconds(long ns) => new(ns);

    /// <summary>
    /// Converts a <see cref="DateTimeOffset"/> to a <see cref="WallclockTime"/>.
    /// Precision is limited to 100ns (DateTimeOffset tick resolution).
    /// </summary>
    public static WallclockTime FromDateTimeOffset(DateTimeOffset dto)
    {
        long ticks = dto.UtcTicks - UnixEpochTicks;
        return new WallclockTime(ticks * TicksToNanoseconds);
    }

    /// <summary>
    /// Converts this <see cref="WallclockTime"/> to a <see cref="DateTimeOffset"/>.
    /// Sub-100ns precision is truncated.
    /// </summary>
    public DateTimeOffset ToDateTimeOffset()
    {
        long ticks = NanosecondsSinceEpoch / TicksToNanoseconds;
        return new DateTimeOffset(DateTime.UnixEpoch.AddTicks(ticks), TimeSpan.Zero);
    }

    /// <summary>Subtracts two wallclock times, yielding a <see cref="TimeSpan"/>.</summary>
    public static TimeSpan operator -(WallclockTime a, WallclockTime b)
        => TimeSpan.FromTicks((a.NanosecondsSinceEpoch - b.NanosecondsSinceEpoch) / TicksToNanoseconds);

    /// <summary>Adds a <see cref="TimeSpan"/> to a wallclock time.</summary>
    public static WallclockTime operator +(WallclockTime t, TimeSpan d)
        => new(t.NanosecondsSinceEpoch + d.Ticks * TicksToNanoseconds);

    /// <inheritdoc />
    public int CompareTo(WallclockTime other)
        => NanosecondsSinceEpoch.CompareTo(other.NanosecondsSinceEpoch);

    /// <inheritdoc />
    public override string ToString() => ToDateTimeOffset().ToString("O");
}
