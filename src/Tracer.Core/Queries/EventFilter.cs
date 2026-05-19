using Tracer.Core.Domain;
using Tracer.Core.Identity;
using Tracer.Core.Time;

namespace Tracer.Core.Queries;

/// <summary>
/// Specifies filter criteria for event queries.
/// All properties are optional; null means "no constraint on this field."
/// </summary>
public sealed record EventFilter
{
    /// <summary>Include events published at or after this time.</summary>
    public WallclockTime? From { get; init; }

    /// <summary>Include events published strictly before this time.</summary>
    public WallclockTime? To { get; init; }

    /// <summary>Include only events on this topic.</summary>
    public TopicName? Topic { get; init; }

    /// <summary>Include only events from this publisher node.</summary>
    public AgentId? PublisherNode { get; init; }

    /// <summary>Include only events received by this subscriber node.</summary>
    public AgentId? SubscriberNode { get; init; }

    /// <summary>Include only events belonging to this trace.</summary>
    public Identity.TraceId? TraceId { get; init; }

    /// <summary>Include only events associated with this entity.</summary>
    public Domain.EntityId? EntityId { get; init; }

    /// <summary>Include only events owned by this player.</summary>
    public string? OwningPlayerId { get; init; }

    /// <summary>Include only events at or above this severity.</summary>
    public Severity? MinSeverity { get; init; }

    /// <summary>Include only events whose payload JSON contains this string.</summary>
    public string? PayloadSearch { get; init; }

    /// <summary>Returns a filter with no constraints — matches all events.</summary>
    public static EventFilter All => new();

    /// <summary>Returns a filter matching all events in the given trace.</summary>
    public static EventFilter ForTrace(Identity.TraceId traceId) => new() { TraceId = traceId };

    /// <summary>Returns a filter matching all events for the given entity.</summary>
    public static EventFilter ForEntity(Domain.EntityId entityId) => new() { EntityId = entityId };
}
