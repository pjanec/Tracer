using System.Text.Json;
using System.Text.Json.Serialization;
using Tracer.Core.Domain;
using Tracer.Core.Identity;
using Tracer.Core.Records;
using Tracer.Core.Time;

namespace Tracer.Adapters.SharedMemory;

/// <summary>
/// Wrapper carrying the kind discriminator for cross-process record transport.
/// </summary>
[JsonSerializable(typeof(SerializedRecord))]
[JsonSerializable(typeof(EventRecordDto))]
[JsonSerializable(typeof(StateSampleRecordDto))]
internal partial class DiagnosticRecordSerializerContext : JsonSerializerContext { }

/// <summary>Top-level wrapper for polymorphic IPC serialization.</summary>
internal sealed record SerializedRecord
{
    public required string Kind { get; init; }   // "Event" | "StateSlow" | "StateFast"
    public required string Json { get; init; }   // inner DTO JSON
}

/// <summary>Primitive-typed DTO for <see cref="EventRecord"/> IPC transport.</summary>
internal sealed record EventRecordDto
{
    public ulong SequenceNumber { get; init; }
    public long PublishWallclockNs { get; init; }
    public long ReceiveWallclockNs { get; init; }
    public string PublisherNode { get; init; } = "";
    public string SubscriberNode { get; init; } = "";
    public string Topic { get; init; } = "";
    public ulong EventId { get; init; }
    public ulong TraceId { get; init; }
    public ulong? ParentEventId { get; init; }
    public string? EntityId { get; init; }
    public string? OwningPlayerId { get; init; }
    public string? ScenarioPhase { get; init; }
    public int? Severity { get; init; }
    public string? NotableLabel { get; init; }
    public string PayloadJson { get; init; } = "";
}

/// <summary>Primitive-typed DTO for <see cref="StateSampleRecord"/> IPC transport.</summary>
internal sealed record StateSampleRecordDto
{
    public ulong SequenceNumber { get; init; }
    public long PublishWallclockNs { get; init; }
    public long ReceiveWallclockNs { get; init; }
    public string PublisherNode { get; init; } = "";
    public string SubscriberNode { get; init; } = "";
    public string Topic { get; init; } = "";
    public string InstanceKey { get; init; } = "";
    public ulong? TraceId { get; init; }
    public string PayloadJson { get; init; } = "";
    public int Rate { get; init; }   // 0 = Slow, 1 = Fast
}

/// <summary>
/// Encodes/decodes <see cref="DiagnosticRecord"/> instances to/from byte arrays
/// using source-generated JSON serialization via <see cref="DiagnosticRecordSerializerContext.Default"/>.
/// </summary>
public sealed class SharedMemoryDiagnosticRecordCodec
{
    /// <summary>Encodes a record to UTF-8 JSON bytes.</summary>
    public byte[] Encode(DiagnosticRecord record)
    {
        var (kind, innerJson) = record switch
        {
            EventRecord e => ("Event", JsonSerializer.Serialize(ToDto(e),
                DiagnosticRecordSerializerContext.Default.EventRecordDto)),
            StateSampleRecord { Rate: StateSampleRate.Slow } s => ("StateSlow",
                JsonSerializer.Serialize(ToDto(s), DiagnosticRecordSerializerContext.Default.StateSampleRecordDto)),
            StateSampleRecord s => ("StateFast",
                JsonSerializer.Serialize(ToDto(s), DiagnosticRecordSerializerContext.Default.StateSampleRecordDto)),
            _ => throw new NotSupportedException($"Unsupported record type: {record.GetType()}")
        };

        var wrapper = new SerializedRecord { Kind = kind, Json = innerJson };
        return JsonSerializer.SerializeToUtf8Bytes(wrapper,
            DiagnosticRecordSerializerContext.Default.SerializedRecord);
    }

    /// <summary>Decodes a record from UTF-8 JSON bytes. Returns <c>null</c> on failure.</summary>
    public DiagnosticRecord? Decode(byte[] bytes)
    {
        SerializedRecord? wrapper;
        try
        {
            wrapper = JsonSerializer.Deserialize(bytes,
                DiagnosticRecordSerializerContext.Default.SerializedRecord);
        }
        catch (JsonException) { return null; }

        if (wrapper is null) return null;

        return wrapper.Kind switch
        {
            "Event" => DeserializeEvent(wrapper.Json),
            "StateSlow" => DeserializeState(wrapper.Json, StateSampleRate.Slow),
            "StateFast" => DeserializeState(wrapper.Json, StateSampleRate.Fast),
            _ => null,
        };
    }

    private static EventRecord? DeserializeEvent(string json)
    {
        var dto = JsonSerializer.Deserialize(json,
            DiagnosticRecordSerializerContext.Default.EventRecordDto);
        if (dto is null) return null;
        return new EventRecord
        {
            SequenceNumber = dto.SequenceNumber,
            PublishWallclock = new WallclockTime(dto.PublishWallclockNs),
            ReceiveWallclock = new WallclockTime(dto.ReceiveWallclockNs),
            PublisherNode = new AgentId(dto.PublisherNode),
            SubscriberNode = new AgentId(dto.SubscriberNode),
            Topic = new TopicName(dto.Topic),
            EventId = new Core.Identity.EventId(dto.EventId),
            TraceId = new Core.Identity.TraceId(dto.TraceId),
            ParentEventId = dto.ParentEventId.HasValue
                ? new Core.Identity.EventId(dto.ParentEventId.Value)
                : null,
            EntityId = dto.EntityId is not null ? new EntityId(dto.EntityId) : null,
            OwningPlayerId = dto.OwningPlayerId,
            ScenarioPhase = dto.ScenarioPhase,
            Severity = dto.Severity.HasValue ? (Core.Domain.Severity)dto.Severity.Value : null,
            NotableLabel = dto.NotableLabel,
            PayloadJson = dto.PayloadJson,
        };
    }

    private static StateSampleRecord? DeserializeState(string json, StateSampleRate rate)
    {
        var dto = JsonSerializer.Deserialize(json,
            DiagnosticRecordSerializerContext.Default.StateSampleRecordDto);
        if (dto is null) return null;
        return new StateSampleRecord
        {
            SequenceNumber = dto.SequenceNumber,
            PublishWallclock = new WallclockTime(dto.PublishWallclockNs),
            ReceiveWallclock = new WallclockTime(dto.ReceiveWallclockNs),
            PublisherNode = new AgentId(dto.PublisherNode),
            SubscriberNode = new AgentId(dto.SubscriberNode),
            Topic = new TopicName(dto.Topic),
            InstanceKey = dto.InstanceKey,
            TraceId = dto.TraceId.HasValue ? new Core.Identity.TraceId(dto.TraceId.Value) : null,
            PayloadJson = dto.PayloadJson,
            Rate = rate,
        };
    }

    private static EventRecordDto ToDto(EventRecord e) => new()
    {
        SequenceNumber = e.SequenceNumber,
        PublishWallclockNs = e.PublishWallclock.NanosecondsSinceEpoch,
        ReceiveWallclockNs = e.ReceiveWallclock.NanosecondsSinceEpoch,
        PublisherNode = e.PublisherNode.Value,
        SubscriberNode = e.SubscriberNode.Value,
        Topic = e.Topic.Value,
        EventId = e.EventId.Value,
        TraceId = e.TraceId.Value,
        ParentEventId = e.ParentEventId?.Value,
        EntityId = e.EntityId?.Value,
        OwningPlayerId = e.OwningPlayerId,
        ScenarioPhase = e.ScenarioPhase,
        Severity = e.Severity.HasValue ? (int)e.Severity.Value : null,
        NotableLabel = e.NotableLabel,
        PayloadJson = e.PayloadJson,
    };

    private static StateSampleRecordDto ToDto(StateSampleRecord s) => new()
    {
        SequenceNumber = s.SequenceNumber,
        PublishWallclockNs = s.PublishWallclock.NanosecondsSinceEpoch,
        ReceiveWallclockNs = s.ReceiveWallclock.NanosecondsSinceEpoch,
        PublisherNode = s.PublisherNode.Value,
        SubscriberNode = s.SubscriberNode.Value,
        Topic = s.Topic.Value,
        InstanceKey = s.InstanceKey,
        TraceId = s.TraceId?.Value,
        PayloadJson = s.PayloadJson,
        Rate = (int)s.Rate,
    };
}
