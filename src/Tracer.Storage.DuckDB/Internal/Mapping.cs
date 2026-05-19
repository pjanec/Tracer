using System.Data;
using Tracer.Core.Domain;
using Tracer.Core.Identity;
using Tracer.Core.Records;
using Tracer.Core.Time;

namespace Tracer.Storage.DuckDB.Internal;

/// <summary>
/// Maps <see cref="IDataReader"/> rows to Tracer domain record types.
/// </summary>
internal static class Mapping
{
    // events table column ordinals (matches CREATE TABLE order in SchemaV1)
    private const int ColEventId = 0;
    private const int ColTraceId = 1;
    private const int ColParentEventId = 2;
    private const int ColSequenceNumber = 3;
    private const int ColPublishWallclock = 4;
    private const int ColReceiveWallclock = 5;
    private const int ColPublisherNode = 6;
    private const int ColSubscriberNode = 7;
    private const int ColTopic = 8;
    private const int ColEntityId = 9;
    private const int ColOwningPlayerId = 10;
    private const int ColScenarioPhase = 11;
    private const int ColSeverity = 12;
    private const int ColNotableLabel = 13;
    private const int ColPayload = 14;

    internal static EventRecord MapEventRecord(IDataReader reader)
    {
        var eventId = new EventId(GetULong(reader, ColEventId));
        var traceId = new TraceId(GetULong(reader, ColTraceId));
        var parentRaw = GetNullableULong(reader, ColParentEventId);
        var parentEventId = parentRaw.HasValue ? new EventId(parentRaw.Value) : (EventId?)null;
        var sequenceNumber = GetULong(reader, ColSequenceNumber);
        var publishWallclock = GetWallclock(reader, ColPublishWallclock);
        var receiveWallclock = GetWallclock(reader, ColReceiveWallclock);
        var publisherNode = new AgentId(reader.GetString(ColPublisherNode));
        var subscriberNode = new AgentId(reader.GetString(ColSubscriberNode));
        var topic = new TopicName(reader.GetString(ColTopic));
        var entityIdStr = GetNullableString(reader, ColEntityId);
        var entityId = entityIdStr is not null ? new EntityId(entityIdStr) : (EntityId?)null;
        var owningPlayerId = GetNullableString(reader, ColOwningPlayerId);
        var scenarioPhase = GetNullableString(reader, ColScenarioPhase);
        var severityStr = GetNullableString(reader, ColSeverity);
        var severity = severityStr is not null ? Enum.Parse<Severity>(severityStr) : (Severity?)null;
        var notableLabel = GetNullableString(reader, ColNotableLabel);
        var payload = reader.GetString(ColPayload);

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

    internal static DateTime WallclockToDateTime(WallclockTime t) =>
        new DateTime(DateTime.UnixEpoch.Ticks + t.NanosecondsSinceEpoch / 100L, DateTimeKind.Utc);

    private static WallclockTime GetWallclock(IDataReader reader, int ordinal)
    {
        var dt = (DateTime)reader.GetValue(ordinal);
        return new WallclockTime((dt.Ticks - DateTime.UnixEpoch.Ticks) * 100L);
    }

    private static ulong GetULong(IDataReader reader, int ordinal) =>
        Convert.ToUInt64(reader.GetValue(ordinal));

    private static ulong? GetNullableULong(IDataReader reader, int ordinal)
    {
        var v = reader.GetValue(ordinal);
        return v is DBNull ? null : Convert.ToUInt64(v);
    }

    private static string? GetNullableString(IDataReader reader, int ordinal)
    {
        var v = reader.GetValue(ordinal);
        return v is DBNull ? null : (string)v;
    }
}
