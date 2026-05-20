using Tracer.Core.Time;

namespace Tracer.Aggregator.Configuration;

/// <summary>
/// Parameters for a single aggregation run.
/// Specify exactly one of <see cref="TimeRange"/> or <see cref="SessionId"/>.
/// </summary>
public sealed record AggregationRequest
{
    /// <summary>
    /// The exact time range to aggregate.
    /// Mutually exclusive with <see cref="SessionId"/>.
    /// </summary>
    public TimeRange? TimeRange { get; init; }

    /// <summary>
    /// Session ID to aggregate; the aggregator resolves it to a time range automatically.
    /// Mutually exclusive with <see cref="TimeRange"/>.
    /// </summary>
    public string? SessionId { get; init; }

    /// <summary>
    /// Nodes to include. <c>null</c> means all nodes that have data in the time range.
    /// Case-insensitive comparison.
    /// </summary>
    public IReadOnlyList<string>? NodeFilter { get; init; }

    /// <summary>
    /// Controls which fast-state (Parquet) data is included. Defaults to <see cref="FastStateScope.None"/>.
    /// </summary>
    public FastStateScope FastStateScope { get; init; } = FastStateScope.None;

    /// <summary>
    /// Entity IDs to include when <see cref="FastStateScope"/> is <see cref="FastStateScope.SelectedEntities"/>.
    /// </summary>
    public IReadOnlyList<string>? FastStateEntities { get; init; }

    /// <summary>
    /// Absolute output path. A <c>.zip</c> suffix produces a zipped bundle; otherwise a directory.
    /// </summary>
    public required string OutputPath { get; init; }

    /// <summary>
    /// Optional human-readable label for the bundle; overrides the label derived from session-start event.
    /// </summary>
    public string? LabelOverride { get; init; }

    /// <summary>
    /// Optional name of the tool producing this bundle (defaults to "tracer-aggregate").
    /// </summary>
    public string? WriterTool { get; init; }
}
