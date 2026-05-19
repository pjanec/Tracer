namespace Tracer.Core.Time;

/// <summary>
/// Abstraction for reading the current wall-clock time.
/// Allows substitution of simulated time in tests.
/// </summary>
public interface IClock
{
    /// <summary>Returns the current wall-clock time.</summary>
    WallclockTime Now { get; }
}
