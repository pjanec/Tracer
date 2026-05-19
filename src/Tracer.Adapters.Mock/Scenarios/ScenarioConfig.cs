using Tracer.Core.Time;

namespace Tracer.Adapters.Mock.Scenarios;

/// <summary>
/// Immutable configuration for a single scenario run.
/// All properties that drive record generation have deterministic defaults.
/// </summary>
public sealed record ScenarioConfig
{
    public TimeSpan Duration { get; init; } = TimeSpan.FromMinutes(5);
    public int NodeCount { get; init; } = 3;
    public int EntityCount { get; init; } = 10;
    public double EventsPerSecond { get; init; } = 100;
    public int Seed { get; init; } = 42;

    public WallclockTime StartTime { get; init; } =
        WallclockTime.FromDateTimeOffset(new DateTimeOffset(2026, 5, 19, 14, 0, 0, TimeSpan.Zero));
}
