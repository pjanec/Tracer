using Tracer.WebApi.Queries;
using Tracer.WebApi.Util;

namespace Tracer.WebApi.Contracts.Dto;

public static class LatencyDtoMapper
{
    public static LatencyDistributionDto Map(LatencyDistribution src)
    {
        ArgumentNullException.ThrowIfNull(src);
        return new LatencyDistributionDto
        {
            SampleCount = src.SampleCount,
            P50Ms = src.P50Ms,
            P90Ms = src.P90Ms,
            P99Ms = src.P99Ms,
            P999Ms = src.P999Ms,
            MaxMs = src.MaxMs,
            MinMs = src.MinMs,
            MeanMs = src.MeanMs,
            StddevMs = src.StddevMs,
            Buckets = src.Buckets.Select(b => new HistogramBucketDto
            {
                Index = b.Index,
                LowMs = b.LowMs,
                HighMs = b.HighMs,
                Count = b.Count,
            }).ToList(),
        };
    }

    public static IReadOnlyList<LatencyPairSummaryDto> MapPairs(IReadOnlyList<LatencyPairSummary> src)
    {
        ArgumentNullException.ThrowIfNull(src);
        return src.Select(p => new LatencyPairSummaryDto
        {
            Topic = p.Topic,
            PublisherNode = p.PublisherNode,
            SubscriberNode = p.SubscriberNode,
            SampleCount = p.SampleCount,
            P50Ms = p.P50Ms,
            P99Ms = p.P99Ms,
            MaxMs = p.MaxMs,
        }).ToList();
    }

    public static LatencyTimeSeriesDto MapTimeSeries(LatencyTimeSeries src)
    {
        ArgumentNullException.ThrowIfNull(src);
        return new LatencyTimeSeriesDto
        {
            BucketSize = src.BucketSize,
            Points = src.Points.Select(p => new LatencyTimePointDto
            {
                BucketStartUtc = p.BucketStartUtc,
                P50Ms = p.P50Ms,
                P99Ms = p.P99Ms,
                SampleCount = p.SampleCount,
            }).ToList(),
        };
    }

    public static LatencyOutlierListDto MapOutliers(LatencyOutlierResult src)
    {
        ArgumentNullException.ThrowIfNull(src);
        return new LatencyOutlierListDto
        {
            Outliers = src.Outliers.Select(o => new LatencyOutlierDto
            {
                EventId = o.EventId,
                Topic = o.Topic,
                PublisherNode = o.PublisherNode,
                SubscriberNode = o.SubscriberNode,
                PublishWallclockUtc = o.PublishWallclockUtc,
                ReceiveWallclockUtc = o.ReceiveWallclockUtc,
                LatencyMs = o.LatencyMs,
                ThresholdMs = o.ThresholdMs,
                BudgetSource = o.BudgetSource,
            }).ToList(),
            BudgetsUsed = src.BudgetsUsed.Select(BudgetDtoMapper.Map).ToList(),
        };
    }
}

public static class GapDtoMapper
{
    public static GapResultDto Map(GapDetectionResult src)
    {
        ArgumentNullException.ThrowIfNull(src);
        return new GapResultDto
        {
            TotalGaps = src.TotalGaps,
            Gaps = src.Gaps.Select(g => new GapDto
            {
                Topic = g.Topic,
                PublisherNode = g.PublisherNode,
                SubscriberNode = g.SubscriberNode,
                ResumedAtSequence = g.ResumedAtSequence,
                PreviousSequence = g.PreviousSequence,
                MissingCount = g.MissingCount,
                ResumedAtWallclockUtc = g.ResumedAtWallclockUtc,
            }).ToList(),
        };
    }
}

public static class NetworkTopologyDtoMapper
{
    public static NetworkTopologyDto Map(NetworkTopology src)
    {
        ArgumentNullException.ThrowIfNull(src);
        return new NetworkTopologyDto
        {
            Nodes = src.Nodes,
            Edges = src.Edges.Select(e => new NetworkTopologyEdgeDto
            {
                Topic = e.Topic,
                PublisherNode = e.PublisherNode,
                SubscriberNode = e.SubscriberNode,
                MessageCount = e.MessageCount,
                FirstSeenUtc = e.FirstSeenUtc,
                LastSeenUtc = e.LastSeenUtc,
            }).ToList(),
        };
    }
}

public static class BudgetDtoMapper
{
    public static BudgetDto Map(Tracer.Core.Domain.LatencyBudget src)
    {
        ArgumentNullException.ThrowIfNull(src);
        return new BudgetDto
        {
            Topic = src.Topic,
            P99BudgetMs = src.P99BudgetMs,
            AbsoluteMaxMs = src.AbsoluteMaxMs,
        };
    }
}
