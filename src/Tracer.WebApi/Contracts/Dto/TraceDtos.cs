using System.Text.Json.Serialization;

namespace Tracer.WebApi.Contracts.Dto;

public sealed record TraceTreeDto
{
    public required string TraceId { get; init; }
    public required string SessionId { get; init; }
    public required IReadOnlyList<TraceNodeDto> Nodes { get; init; }
    public required IReadOnlyList<TraceEdgeDto> Edges { get; init; }
    public required IReadOnlyList<string> RootEventIds { get; init; }
    public required IReadOnlyList<string> LeafEventIds { get; init; }
    public required TraceSummaryDto Summary { get; init; }
}

public sealed record TraceNodeDto
{
    public required string EventId { get; init; }
    public required string TraceId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ParentEventId { get; init; }
    public required DateTimeOffset PublishWallclock { get; init; }
    public required string PublisherNode { get; init; }
    public required string Topic { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? EntityId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Severity { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? NotableLabel { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PayloadJson { get; init; }
}

public sealed record TraceEdgeDto
{
    public required string ParentEventId { get; init; }
    public required string ChildEventId { get; init; }
    public required double LatencyMs { get; init; }
}

public sealed record TraceSummaryDto
{
    public required string TraceId { get; init; }
    public required int TotalEvents { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? TotalEventsAvailable { get; init; }
    public required bool Truncated { get; init; }
    public required double TotalSpanMs { get; init; }
    public required IReadOnlyList<string> ParticipatingNodes { get; init; }
    public required int RootCount { get; init; }
    public required int LeafCount { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? FirstEventUtc { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? LastEventUtc { get; init; }
}
