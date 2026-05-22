namespace Tracer.Adapters.DDS.Configuration;

/// <summary>Configuration for the DDS diagnostic data source adapter.</summary>
public sealed class DdsAdapterConfig
{
    public required string PublisherNodeId { get; init; }
    public required IReadOnlyList<DdsTopicSubscription> Topics { get; init; }
    public int IngestBufferSize { get; init; } = 50_000;
    public required CycloneDdsParticipantConfig Participant { get; init; }
}

/// <summary>Identifies a single DDS topic to subscribe to.</summary>
public sealed class DdsTopicSubscription
{
    public required string TopicName { get; init; }
    public required string SampleTypeName { get; init; }
}

/// <summary>CycloneDDS participant configuration.</summary>
public sealed class CycloneDdsParticipantConfig
{
    public required int DomainId { get; init; }
    public string? QosProfile { get; init; }
}
