using Tracer.WebApi.Contracts.Dto;
using Tracer.WebApi.Queries;

namespace Tracer.WebApi.Contracts.Mapping;

public static class TraceDtoMapper
{
    public static TraceTreeDto Map(TraceTree tree)
    {
        ArgumentNullException.ThrowIfNull(tree);
        var nodes   = tree.Nodes.Select(MapNode).ToList();
        var edges   = tree.Edges.Select(MapEdge).ToList();
        var rootIds = tree.Roots.Select(n => DtoMappers.ToHex(n.Event.EventId)).ToList();
        var leafIds = tree.Leaves.Select(n => DtoMappers.ToHex(n.Event.EventId)).ToList();

        return new TraceTreeDto
        {
            TraceId      = tree.TraceId.ToString("X16"),
            Nodes        = nodes,
            Edges        = edges,
            RootEventIds = rootIds,
            LeafEventIds = leafIds,
            Summary      = Map(tree.Summary),
        };
    }

    public static TraceNodeDto MapNode(TraceNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        var ev = node.Event;
        return new TraceNodeDto
        {
            EventId          = DtoMappers.ToHex(ev.EventId),
            TraceId          = DtoMappers.ToHex(ev.TraceId),
            ParentEventId    = ev.ParentEventId.HasValue
                                   ? DtoMappers.ToHex(ev.ParentEventId.Value)
                                   : null,
            PublishWallclock = ev.PublishWallclock.ToDateTimeOffset(),
            PublisherNode    = ev.PublisherNode.Value,
            Topic            = ev.Topic.Value,
            EntityId         = ev.EntityId?.Value,
            Severity         = ev.Severity?.ToString(),
            NotableLabel     = ev.NotableLabel,
            PayloadJson      = ev.PayloadJson,
        };
    }

    public static TraceEdgeDto MapEdge(TraceEdge edge)
    {
        ArgumentNullException.ThrowIfNull(edge);
        return new()
        {
            ParentEventId = DtoMappers.ToHex(edge.ParentEventId),
            ChildEventId  = DtoMappers.ToHex(edge.ChildEventId),
            LatencyMs     = edge.LatencyMs,
        };
    }

    public static TraceSummaryDto Map(TraceSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        return new()
        {
            TraceId              = summary.TraceId.ToString("X16"),
            TotalEvents          = summary.TotalEvents,
            TotalEventsAvailable = summary.Truncated ? summary.TotalEventsAvailable : null,
            Truncated            = summary.Truncated,
            TotalSpanMs          = summary.TotalSpanMs,
            ParticipatingNodes   = summary.ParticipatingNodes,
            RootCount            = summary.RootCount,
            LeafCount            = summary.LeafCount,
            FirstEventUtc        = summary.FirstEventUtc,
            LastEventUtc         = summary.LastEventUtc,
        };
    }
}
