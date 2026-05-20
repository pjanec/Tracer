using Tracer.Core.Identity;
using Tracer.Core.Records;

namespace Tracer.WebApi.Queries;

/// <summary>
/// A tree (or DAG) of events sharing a trace_id, with edges derived from parent_event_id.
/// </summary>
public sealed record TraceTree
{
    public required ulong TraceId { get; init; }
    public required IReadOnlyList<TraceNode> Nodes { get; init; }
    public required IReadOnlyList<TraceEdge> Edges { get; init; }
    public required IReadOnlyList<TraceNode> Roots { get; init; }
    public required IReadOnlyList<TraceNode> Leaves { get; init; }
    public required TraceSummary Summary { get; init; }
    /// <summary>Session ID of the session whose time range contains the trace's first event. Empty when not resolvable.</summary>
    public string SessionId { get; init; } = string.Empty;
}

/// <summary>A node in the trace tree, wrapping the underlying event record.</summary>
public sealed record TraceNode(EventRecord Event);

/// <summary>A directed edge from parent to child, annotated with latency.</summary>
public sealed record TraceEdge(EventId ParentEventId, EventId ChildEventId, double LatencyMs);

/// <summary>Metadata about the trace as a whole.</summary>
public sealed record TraceSummary
{
    public required ulong TraceId { get; init; }
    public required int TotalEvents { get; init; }
    public required bool Truncated { get; init; }
    public required double TotalSpanMs { get; init; }
    public required IReadOnlyList<string> ParticipatingNodes { get; init; }
    public required int RootCount { get; init; }
    public required int LeafCount { get; init; }
    public DateTimeOffset? FirstEventUtc { get; init; }
    public DateTimeOffset? LastEventUtc { get; init; }
    public int? TotalEventsAvailable { get; init; }  // populated when Truncated = true
}
