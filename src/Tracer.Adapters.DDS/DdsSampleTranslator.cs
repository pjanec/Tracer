using System.Text.Json;
using Microsoft.Extensions.Logging;
using Tracer.Adapters.DDS.Configuration;
using Tracer.Core.Domain;
using Tracer.Core.Identity;
using Tracer.Core.Records;
using Tracer.Core.Time;

namespace Tracer.Adapters.DDS;

/// <summary>
/// Translates a raw <see cref="IDdsSample"/> into a <see cref="DiagnosticRecord"/>
/// based on topic kind metadata.
/// </summary>
public sealed class DdsSampleTranslator
{
    private readonly DdsTraceContextExtractor _traceExtractor;
    private readonly DdsTopicRegistry _topicRegistry;
    private readonly DdsAdapterConfig _config;
    private readonly IClock _clock;
    private readonly ILogger<DdsSampleTranslator> _logger;

    public DdsSampleTranslator(
        DdsTraceContextExtractor traceExtractor,
        DdsTopicRegistry topicRegistry,
        DdsAdapterConfig config,
        IClock clock,
        ILogger<DdsSampleTranslator> logger)
    {
        _traceExtractor = traceExtractor;
        _topicRegistry = topicRegistry;
        _config = config;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>
    /// Translates the sample. Returns <c>null</c> if the topic is unknown (and logs a warning).
    /// </summary>
    public DiagnosticRecord? Translate(IDdsSample sample, DdsTopicSubscription topicSub)
    {
        ArgumentNullException.ThrowIfNull(sample);
        ArgumentNullException.ThrowIfNull(topicSub);

        var meta = _topicRegistry.Lookup(topicSub.TopicName);
        if (meta is null)
        {
            _logger.LogWarning("Topic {Topic} not in DdsTopicRegistry; skipping sample", topicSub.TopicName);
            return null;
        }

        var publishWallclock = WallclockTime.FromDateTimeOffset(sample.SourceTimestamp);
        var receiveWallclock = _clock.Now;
        var traceContext = _traceExtractor.Extract(sample, meta);
        var payload = sample.GetPayload();
        var payloadJson = JsonSerializer.Serialize(payload);
        var publisherNode = new AgentId(_config.PublisherNodeId);

        return meta.Kind switch
        {
            DdsTopicKind.Event => new EventRecord
            {
                SequenceNumber = sample.SequenceNumber,
                PublishWallclock = publishWallclock,
                ReceiveWallclock = receiveWallclock,
                PublisherNode = publisherNode,
                SubscriberNode = publisherNode,
                Topic = new TopicName(topicSub.TopicName),
                EventId = traceContext.EventId,
                TraceId = new TraceId(traceContext.TraceId),
                ParentEventId = traceContext.ParentEventId.Value == 0 ? null : traceContext.ParentEventId,
                EntityId = ExtractStringField(payload, meta.EntityIdField) is { } eid
                    ? new EntityId(eid)
                    : null,
                OwningPlayerId = meta.OwningPlayerIdField is not null
                    ? ExtractStringField(payload, meta.OwningPlayerIdField)
                    : null,
                ScenarioPhase = null,
                Severity = null,
                NotableLabel = meta.NotableLabelField is not null
                    ? ExtractStringField(payload, meta.NotableLabelField)
                    : null,
                PayloadJson = payloadJson,
            },
            DdsTopicKind.SlowState => new StateSampleRecord
            {
                SequenceNumber = sample.SequenceNumber,
                PublishWallclock = publishWallclock,
                ReceiveWallclock = receiveWallclock,
                PublisherNode = publisherNode,
                SubscriberNode = publisherNode,
                Topic = new TopicName(topicSub.TopicName),
                InstanceKey = ExtractStringField(payload, meta.InstanceKeyField ?? meta.EntityIdField) ?? "",
                TraceId = null,
                PayloadJson = payloadJson,
                Rate = StateSampleRate.Slow,
            },
            DdsTopicKind.FastState => new StateSampleRecord
            {
                SequenceNumber = sample.SequenceNumber,
                PublishWallclock = publishWallclock,
                ReceiveWallclock = receiveWallclock,
                PublisherNode = publisherNode,
                SubscriberNode = publisherNode,
                Topic = new TopicName(topicSub.TopicName),
                InstanceKey = ExtractStringField(payload, meta.InstanceKeyField ?? meta.EntityIdField) ?? "",
                TraceId = null,
                PayloadJson = payloadJson,
                Rate = StateSampleRate.Fast,
            },
            _ => null,
        };
    }

    private static string? ExtractStringField(object payload, string? fieldName)
    {
        if (fieldName is null) return null;
        return payload.GetType()
            .GetProperty(fieldName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            ?.GetValue(payload)
            ?.ToString();
    }
}
