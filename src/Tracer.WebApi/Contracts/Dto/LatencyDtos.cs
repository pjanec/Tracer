namespace Tracer.WebApi.Contracts.Dto;

public sealed record LatencyDistributionDto
{
    public required long SampleCount { get; init; }
    public required double P50Ms { get; init; }
    public required double P90Ms { get; init; }
    public required double P99Ms { get; init; }
    public required double P999Ms { get; init; }
    public required double MaxMs { get; init; }
    public required double MinMs { get; init; }
    public required double MeanMs { get; init; }
    public required double StddevMs { get; init; }
    public required IReadOnlyList<HistogramBucketDto> Buckets { get; init; }
}

public sealed record HistogramBucketDto
{
    public required long Index { get; init; }
    public required double LowMs { get; init; }
    public required double HighMs { get; init; }
    public required long Count { get; init; }
}

public sealed record LatencyPairSummaryDto
{
    public required string Topic { get; init; }
    public required string PublisherNode { get; init; }
    public required string SubscriberNode { get; init; }
    public required long SampleCount { get; init; }
    public required double P50Ms { get; init; }
    public required double P99Ms { get; init; }
    public required double MaxMs { get; init; }
}

public sealed record LatencyTimePointDto
{
    public required DateTimeOffset BucketStartUtc { get; init; }
    public required double P50Ms { get; init; }
    public required double P99Ms { get; init; }
    public required long SampleCount { get; init; }
}

public sealed record LatencyTimeSeriesDto
{
    public required string BucketSize { get; init; }
    public required IReadOnlyList<LatencyTimePointDto> Points { get; init; }
}

public sealed record LatencyOutlierDto
{
    public required string EventId { get; init; }
    public required string Topic { get; init; }
    public required string PublisherNode { get; init; }
    public required string SubscriberNode { get; init; }
    public required DateTimeOffset PublishWallclockUtc { get; init; }
    public required DateTimeOffset ReceiveWallclockUtc { get; init; }
    public required double LatencyMs { get; init; }
    public required double ThresholdMs { get; init; }
    public required string BudgetSource { get; init; }
}

public sealed record LatencyOutlierListDto
{
    public required IReadOnlyList<LatencyOutlierDto> Outliers { get; init; }
    public required IReadOnlyList<BudgetDto> BudgetsUsed { get; init; }
}
