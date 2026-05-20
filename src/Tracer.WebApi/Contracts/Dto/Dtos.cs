using System.Text.Json.Serialization;

namespace Tracer.WebApi.Contracts.Dto;

public sealed record SessionDto
{
    public required string SessionId { get; init; }
    public required DateTimeOffset StartUtc { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? EndUtc { get; init; }
    public required string Status { get; init; }
    public required int EventCount { get; init; }
    public required IReadOnlyList<string> ParticipatingNodes { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ScenarioId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Label { get; init; }
}

public sealed record NodeInfoDto
{
    public required string NodeId { get; init; }
    public required DateTimeOffset FirstSeenUtc { get; init; }
    public required DateTimeOffset LastSeenUtc { get; init; }
    public required long EventsPublished { get; init; }
}

public sealed record TopologyDto
{
    public required IReadOnlyList<NodeInfoDto> Nodes { get; init; }
    public required DateTimeOffset AsOfUtc { get; init; }
}

public sealed record NotableEventDto
{
    public required string EventId { get; init; }
    public required string TraceId { get; init; }
    public required DateTimeOffset OccurredAtUtc { get; init; }
    public required string Topic { get; init; }
    public required string NotableLabel { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Severity { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? EntityId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ScenarioPhase { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PayloadJson { get; init; }
}

public sealed record ScenarioPhaseDto
{
    public required string PhaseName { get; init; }
    public required DateTimeOffset StartedAtUtc { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? EndedAtUtc { get; init; }
    public required string Status { get; init; }
}

public sealed record ScenarioStateDto
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CurrentPhase { get; init; }
    public required long TotalEvents { get; init; }
    public required long TotalNotables { get; init; }
    public required IReadOnlyList<string> ParticipatingNodes { get; init; }
}

public sealed record EventDto
{
    public required string EventId { get; init; }
    public required string TraceId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ParentEventId { get; init; }
    public required DateTimeOffset OccurredAtUtc { get; init; }
    public required string PublisherNode { get; init; }
    public required string SubscriberNode { get; init; }
    public required string Topic { get; init; }
    public required long SequenceNumber { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? EntityId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? OwningPlayerId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ScenarioPhase { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Severity { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? NotableLabel { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PayloadJson { get; init; }
}

public sealed record LiveStatusDto
{
    public required bool IngestionHealthy { get; init; }
    public required long IngestedTotal { get; init; }
    public required long DroppedTotal { get; init; }
    public required int ActiveSseClients { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? LastEventUtc { get; init; }
}

public sealed record TimeRangeDto
{
    public required DateTimeOffset StartUtc { get; init; }
    public required DateTimeOffset EndUtc { get; init; }
}

public sealed record BundleBuildRequestDto
{
    public string? SessionId { get; init; }
    public TimeRangeDto? TimeRange { get; init; }
    public IReadOnlyList<string>? NodeFilter { get; init; }
    public string FastStateScope { get; init; } = "None";
    public IReadOnlyList<string>? FastStateEntities { get; init; }
    public string? LabelOverride { get; init; }
}

public sealed record BundleBuildAcceptedDto
{
    public required string BundleId { get; init; }
}

public sealed record BundleBuildStatusDto
{
    public required string BundleId { get; init; }
    public required string State { get; init; }
    public required DateTimeOffset QueuedAtUtc { get; init; }
    public DateTimeOffset? StartedAtUtc { get; init; }
    public DateTimeOffset? CompletedAtUtc { get; init; }
    public string? Error { get; init; }
    public string? CurrentStage { get; init; }
    public string? OutputPath { get; init; }
}

public sealed record BundleListDto
{
    public required IReadOnlyList<BundleListEntryDto> Bundles { get; init; }
}

public sealed record BundleListEntryDto
{
    public required string BundleId { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public required TimeRangeDto TimeRange { get; init; }
    public required long SizeBytes { get; init; }
    public string? Label { get; init; }
    public string? SessionId { get; init; }
}

public sealed record BundleWriterInfoDto
{
    public required string Tool { get; init; }
    public required string Version { get; init; }
    public required string Host { get; init; }
}

public sealed record BundleSessionContextDto
{
    public string? SessionId { get; init; }
    public string? ScenarioId { get; init; }
    public string? Label { get; init; }
}

public sealed record BundleStatisticsDto
{
    public required long TotalEvents { get; init; }
    public required long TotalSlowStateSamples { get; init; }
    public required long TotalFastStateRows { get; init; }
    public required long UncompressedBytes { get; init; }
}

public sealed record BundleManifestDto
{
    public required string BundleId { get; init; }
    public required int SchemaVersion { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public required string TracerVersion { get; init; }
    public required BundleWriterInfoDto Writer { get; init; }
    public required TimeRangeDto TimeRange { get; init; }
    public required BundleSessionContextDto SessionContext { get; init; }
    public required IReadOnlyList<string> ParticipatingNodes { get; init; }
    public required string FastStateScope { get; init; }
    public required BundleStatisticsDto Statistics { get; init; }
}
