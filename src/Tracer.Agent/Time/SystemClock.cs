using Tracer.Core.Time;

namespace Tracer.Agent.Time;

/// <summary>
/// System clock implementation backed by a <see cref="System.TimeProvider"/> injected via DI.
/// </summary>
public sealed class SystemClock : IClock
{
    private readonly TimeProvider _timeProvider;

    public SystemClock(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        _timeProvider = timeProvider;
    }

    public WallclockTime Now => WallclockTime.FromDateTimeOffset(_timeProvider.GetUtcNow());
}
