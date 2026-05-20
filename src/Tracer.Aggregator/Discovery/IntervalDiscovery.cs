using Tracer.Core.Abstractions;
using Tracer.Core.Time;

namespace Tracer.Aggregator.Discovery;

/// <summary>
/// Finds uploaded intervals whose time range overlaps a requested <see cref="TimeRange"/>.
/// </summary>
public static class IntervalDiscovery
{
    /// <summary>
    /// Returns all intervals from <paramref name="reader"/> whose <c>[StartUtc, EndUtc)</c>
    /// overlaps <paramref name="timeRange"/>.
    /// </summary>
    /// <param name="reader">The storage reader to enumerate nodes and intervals from.</param>
    /// <param name="timeRange">The half-open time range to match against.</param>
    /// <param name="nodeFilter">
    /// When non-<c>null</c>, only intervals for nodes whose IDs appear in this list are returned
    /// (case-insensitive comparison). Pass <c>null</c> to include all nodes.
    /// </param>
    public static async Task<DiscoveredIntervals> FindOverlappingAsync(
        ITelemetryStorageReader reader,
        TimeRange timeRange,
        IReadOnlyList<string>? nodeFilter,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(timeRange);

        var allNodes = await reader.ListNodesAsync(ct);
        var nodes = nodeFilter is null
            ? allNodes
            : allNodes.Where(n => nodeFilter.Contains(n, StringComparer.OrdinalIgnoreCase)).ToList();

        var intervals = new List<DiscoveredInterval>();
        foreach (var nodeId in nodes)
        {
            var nodeIntervals = await reader.ListIntervalsAsync(nodeId, ct);
            foreach (var iv in nodeIntervals)
            {
                if (Overlaps(iv.StartUtc, iv.EndUtc, timeRange))
                    intervals.Add(new DiscoveredInterval(nodeId, iv));
            }
        }

        return new DiscoveredIntervals(intervals);
    }

    /// <summary>
    /// Returns <c>true</c> when the interval <c>[ivStart, ivEnd)</c> overlaps the range
    /// <c>[rangeStart, rangeEnd)</c>.
    /// </summary>
    private static bool Overlaps(DateTimeOffset ivStart, DateTimeOffset ivEnd, TimeRange range)
    {
        // [a, b) overlaps [c, d) iff a < d AND b > c
        return ivStart < range.EndUtc.ToDateTimeOffset()
            && ivEnd   > range.StartUtc.ToDateTimeOffset();
    }
}
