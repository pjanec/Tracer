using Tracer.Core.Time;

namespace Tracer.AdapterSelection;

/// <summary>
/// System clock implementation that reads from <see cref="DateTimeOffset.UtcNow"/>.
/// Defined here to avoid a circular dependency with Tracer.Agent.
/// </summary>
internal sealed class SystemClock : IClock
{
    public WallclockTime Now => WallclockTime.FromDateTimeOffset(DateTimeOffset.UtcNow);
}
