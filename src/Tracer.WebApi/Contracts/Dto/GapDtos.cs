namespace Tracer.WebApi.Contracts.Dto;

public sealed record GapDto
{
    public required string Topic { get; init; }
    public required string PublisherNode { get; init; }
    public required string SubscriberNode { get; init; }
    public required ulong ResumedAtSequence { get; init; }
    public required ulong PreviousSequence { get; init; }
    public required ulong MissingCount { get; init; }
    public required DateTimeOffset ResumedAtWallclockUtc { get; init; }
}

public sealed record GapResultDto
{
    public required IReadOnlyList<GapDto> Gaps { get; init; }
    public required long TotalGaps { get; init; }
}
