using Tracer.Core.Identity;
using Tracer.Core.Records;
using Tracer.WebApi.Contracts.Dto;

namespace Tracer.WebApi.Contracts.Mapping;

public static class DtoMappers
{
    public static string ToHex(EventId id) => id.Value.ToString("X16");
    public static string ToHex(TraceId id) => id.Value.ToString("X16");

    public static EventDto ToDto(EventRecord ev)
    {
        ArgumentNullException.ThrowIfNull(ev);
        return new EventDto
        {
            EventId = ToHex(ev.EventId),
            TraceId = ToHex(ev.TraceId),
            ParentEventId = ev.ParentEventId.HasValue && !ev.ParentEventId.Value.IsNone
                ? ToHex(ev.ParentEventId.Value)
                : null,
            OccurredAtUtc = ev.PublishWallclock.ToDateTimeOffset(),
            PublisherNode = ev.PublisherNode.Value,
            SubscriberNode = ev.SubscriberNode.Value,
            Topic = ev.Topic.Value,
            SequenceNumber = (long)ev.SequenceNumber,
            EntityId = ev.EntityId?.Value,
            OwningPlayerId = ev.OwningPlayerId,
            ScenarioPhase = ev.ScenarioPhase,
            Severity = ev.Severity?.ToString(),
            NotableLabel = ev.NotableLabel,
            PayloadJson = ev.PayloadJson,
        };
    }

    public static NotableEventDto ToNotableDto(EventRecord ev)
    {
        ArgumentNullException.ThrowIfNull(ev);
        return new NotableEventDto
        {
            EventId = ToHex(ev.EventId),
            TraceId = ToHex(ev.TraceId),
            OccurredAtUtc = ev.PublishWallclock.ToDateTimeOffset(),
            Topic = ev.Topic.Value,
            NotableLabel = ev.NotableLabel ?? string.Empty,
            Severity = ev.Severity?.ToString(),
            EntityId = ev.EntityId?.Value,
            ScenarioPhase = ev.ScenarioPhase,
            PayloadJson = ev.PayloadJson,
        };
    }
}

