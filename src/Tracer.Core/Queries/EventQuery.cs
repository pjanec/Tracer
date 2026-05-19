namespace Tracer.Core.Queries;

/// <summary>
/// Specifies an event query: what to retrieve, how many, and in what order.
/// </summary>
public sealed record EventQuery
{
    /// <summary>The filter criteria for this query.</summary>
    public required EventFilter Filter { get; init; }

    /// <summary>Maximum number of results to return. Defaults to 1000.</summary>
    public int Limit { get; init; } = 1000;

    /// <summary>Number of results to skip (for pagination). Defaults to 0.</summary>
    public int Offset { get; init; } = 0;

    /// <summary>The ordering of results. Defaults to publish time ascending.</summary>
    public QueryOrder Order { get; init; } = QueryOrder.PublishTimeAscending;
}
