using Tracer.Core.Time;

namespace Tracer.Core.Domain;

/// <summary>
/// Records a session start or end marker observed within an interval.
/// </summary>
public sealed record SessionMarker
{
    public required string SessionId { get; init; }
    public required SessionMarkerType Type { get; init; }
    public required WallclockTime Wallclock { get; init; }
    public string? Label { get; init; }
}

/// <summary>
/// Whether a session marker records the start or end of a session.
/// </summary>
public enum SessionMarkerType
{
    Start,
    End
}
