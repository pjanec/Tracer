using Tracer.Core.Records;

namespace Tracer.WebApi.Streaming;

public sealed record SseFilter
{
    public string? SessionId { get; init; }
    public IReadOnlySet<string>? Topics { get; init; }
    public IReadOnlySet<string>? Nodes { get; init; }
    public string? TraceId { get; init; }
    public IReadOnlySet<string>? EntityIds { get; init; }
    public IReadOnlySet<string>? PlayerIds { get; init; }
    public IReadOnlySet<string>? Severities { get; init; }
    public bool NotablesOnly { get; init; }

    public bool Matches(EventRecord ev)
    {
        ArgumentNullException.ThrowIfNull(ev);
        if (SessionId is not null &&
            !ev.PayloadJson.Contains($"\"sessionId\":\"{SessionId}\"", StringComparison.Ordinal))
            return false;
        if (NotablesOnly && ev.NotableLabel is null) return false;
        if (Topics is not null && !Topics.Contains(ev.Topic.Value)) return false;
        if (Nodes is not null && !Nodes.Contains(ev.PublisherNode.Value)) return false;
        if (TraceId is not null && ev.TraceId.Value.ToString("X16") != TraceId) return false;
        if (EntityIds is not null && (ev.EntityId is null || !EntityIds.Contains(ev.EntityId.Value.Value))) return false;
        if (PlayerIds is not null && (ev.OwningPlayerId is null || !PlayerIds.Contains(ev.OwningPlayerId))) return false;
        if (Severities is not null && (ev.Severity is null || !Severities.Contains(ev.Severity.Value.ToString()))) return false;
        return true;
    }
}

