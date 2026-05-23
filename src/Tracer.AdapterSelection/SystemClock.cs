using Tracer.Core.Time;

namespace Tracer.AdapterSelection;

/// <summary>
/// System clock implementation backed by a <see cref="System.TimeProvider"/> injected via DI.
/// Defined here to avoid a circular dependency with Tracer.Agent.
/// </summary>
internal sealed class SystemClock : IClock
{
    private readonly TimeProvider _timeProvider;

    public SystemClock(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        _timeProvider = timeProvider;
    }

    public WallclockTime Now => WallclockTime.FromDateTimeOffset(_timeProvider.GetUtcNow());
}
