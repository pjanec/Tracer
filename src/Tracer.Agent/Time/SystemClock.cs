using Tracer.Core.Time;

namespace Tracer.Agent.Time;

public sealed class SystemClock : IClock
{
    public WallclockTime Now
        => WallclockTime.FromDateTimeOffset(DateTimeOffset.UtcNow);
}
