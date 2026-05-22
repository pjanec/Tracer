using System.Text.Json.Serialization;

namespace Tracer.WebApi.Contracts.Dto;

public sealed record EntityListDto
{
    public required IReadOnlyList<EntitySummaryDto> Entities { get; init; }
    public required int Count { get; init; }
}

public sealed record EntitySummaryDto
{
    public required string EntityId { get; init; }
    public required DateTimeOffset FirstSeenUtc { get; init; }
    public required DateTimeOffset LastSeenUtc { get; init; }
    public required long EventCount { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SamplePlayerId { get; init; }
    public required IReadOnlyList<string> Topics { get; init; }
}

public sealed record EntityEventsDto
{
    public required string EntityId { get; init; }
    public required IReadOnlyList<EventDto> Events { get; init; }
    public required bool Truncated { get; init; }
}

public sealed record EntitySlowStateDto
{
    public required string EntityId { get; init; }
    public required IReadOnlyDictionary<string, IReadOnlyList<SlowStateSampleDto>> ByTopic { get; init; }
}

public sealed record SlowStateSampleDto
{
    public required string Topic { get; init; }
    public required DateTimeOffset PublishWallclock { get; init; }
    public required string PayloadJson { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TraceId { get; init; }
}

public sealed record FastStateTopicSchemaDto
{
    public required string EntityId { get; init; }
    public required string Topic { get; init; }
    public required IReadOnlyList<FastStateColumnDto> Columns { get; init; }
}

public sealed record FastStateColumnDto
{
    public required string Name { get; init; }
    public required string DuckType { get; init; }
    public required bool IsNumeric { get; init; }
}

public sealed record EntityFastStateDto
{
    public required string EntityId { get; init; }
    public required string Topic { get; init; }
    public required IReadOnlyList<string> Columns { get; init; }
    public required IReadOnlyList<FastStateSampleDto> Samples { get; init; }
    public required long TotalSamples { get; init; }
    public required bool Downsampled { get; init; }
}

public sealed record FastStateSampleDto
{
    public required DateTimeOffset PublishWallclock { get; init; }
    public required IReadOnlyDictionary<string, double?> Values { get; init; }
}
