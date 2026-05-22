using Tracer.Core.Identity;

namespace Tracer.Adapters.DDS;

/// <summary>
/// Extracted trace context from a DDS sample.
/// </summary>
public sealed record TraceContext
{
    public required ulong TraceId { get; init; }
    public required EventId EventId { get; init; }
    public required EventId ParentEventId { get; init; }

    public static TraceContext Empty => new()
    {
        TraceId = 0,
        EventId = new EventId(0),
        ParentEventId = new EventId(0),
    };
}
