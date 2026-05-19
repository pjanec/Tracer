namespace Tracer.Core.Queries;

/// <summary>
/// Defines the width of a time bucket for aggregation queries.
/// </summary>
public readonly record struct QueryBucket(TimeSpan Width)
{
    /// <summary>A five-minute time bucket.</summary>
    public static QueryBucket FiveMinutes => new(TimeSpan.FromMinutes(5));

    /// <summary>A thirty-second time bucket.</summary>
    public static QueryBucket ThirtySeconds => new(TimeSpan.FromSeconds(30));

    /// <summary>A five-second time bucket.</summary>
    public static QueryBucket FiveSeconds => new(TimeSpan.FromSeconds(5));
}
