using System.Text.Json.Serialization;

namespace Tracer.WebApi.Contracts.Dto;

public sealed record EventListDto
{
    public required IReadOnlyList<EventDto> Events { get; init; }
    public required long TotalMatching { get; init; }
    public required int Returned { get; init; }
    public required bool Truncated { get; init; }
}

public sealed record EventAggregateBucketGroupDto
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? GroupKey { get; init; }
    public required long Count { get; init; }
}

public sealed record EventAggregateBucketDto
{
    public required DateTimeOffset BucketStartUtc { get; init; }
    public required IReadOnlyList<EventAggregateBucketGroupDto> Groups { get; init; }
    public required long Total { get; init; }
}

public sealed record EventAggregateDto
{
    public required string BucketDuration { get; init; }
    public required IReadOnlyList<EventAggregateBucketDto> Buckets { get; init; }
}
