using System.Data;
using Tracer.Core.Domain;
using Tracer.Core.Identity;
using Tracer.Core.Records;
using Tracer.Core.Time;

namespace Tracer.WebApi.Queries;

/// <summary>
/// Maps a DuckDB DataReader row (SELECT * from events, schema order) to an <see cref="EventRecord"/>.
/// </summary>
internal static class EventRecordMapper
{
    public static EventRecord FromReader(IDataReader reader)
    {
        var eventId = new EventId(GetULong(reader, 0));
        var traceId = new TraceId(GetULong(reader, 1));
        var parentRaw = reader.IsDBNull(2) ? (ulong?)null : GetULong(reader, 2);
        var parentEventId = parentRaw.HasValue ? new EventId(parentRaw.Value) : (EventId?)null;
        var sequenceNumber = GetULong(reader, 3);
        var publishWallclock = GetWallclock(reader, 4);
        var receiveWallclock = GetWallclock(reader, 5);
        var publisherNode = new AgentId(reader.GetString(6));
        var subscriberNode = new AgentId(reader.GetString(7));
        var topic = new TopicName(reader.GetString(8));
        var entityIdStr = reader.IsDBNull(9) ? null : reader.GetString(9);
        var entityId = entityIdStr is not null ? new EntityId(entityIdStr) : (EntityId?)null;
        var owningPlayerId = reader.IsDBNull(10) ? null : reader.GetString(10);
        var scenarioPhase = reader.IsDBNull(11) ? null : reader.GetString(11);
        var severityStr = reader.IsDBNull(12) ? null : reader.GetString(12);
        var severity = severityStr is not null ? Enum.Parse<Severity>(severityStr) : (Severity?)null;
        var notableLabel = reader.IsDBNull(13) ? null : reader.GetString(13);
        var payload = reader.IsDBNull(14) ? "{}" : reader.GetString(14);

        return new EventRecord
        {
            EventId = eventId,
            TraceId = traceId,
            ParentEventId = parentEventId,
            SequenceNumber = sequenceNumber,
            PublishWallclock = publishWallclock,
            ReceiveWallclock = receiveWallclock,
            PublisherNode = publisherNode,
            SubscriberNode = subscriberNode,
            Topic = topic,
            EntityId = entityId,
            OwningPlayerId = owningPlayerId,
            ScenarioPhase = scenarioPhase,
            Severity = severity,
            NotableLabel = notableLabel,
            PayloadJson = payload,
        };
    }

    private static ulong GetULong(IDataReader reader, int ordinal)
        => Convert.ToUInt64(reader.GetValue(ordinal));

    private static WallclockTime GetWallclock(IDataReader reader, int ordinal)
    {
        var dt = (DateTime)reader.GetValue(ordinal);
        return new WallclockTime((dt.Ticks - DateTime.UnixEpoch.Ticks) * 100L);
    }
}
