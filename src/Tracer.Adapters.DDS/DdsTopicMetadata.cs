namespace Tracer.Adapters.DDS;

/// <summary>
/// Describes a DDS topic's semantic metadata, guiding sample translation.
/// </summary>
public sealed record DdsTopicMetadata
{
    public required string TopicName { get; init; }
    public required Type SampleType { get; init; }
    public required DdsTopicKind Kind { get; init; }
    public required string? EntityIdField { get; init; }
    public string? OwningPlayerIdField { get; init; }
    public string? SeverityField { get; init; }
    public string? NotableLabelField { get; init; }
    public string? InstanceKeyField { get; init; }
}
