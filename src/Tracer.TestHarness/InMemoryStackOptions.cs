namespace Tracer.TestHarness;

/// <summary>
/// Tuning parameters for the <see cref="TracerStackFixture"/> scaffolding
/// that are independent of the scenario name / seed.
/// </summary>
public sealed record InMemoryStackOptions
{
    /// <summary>Number of logical nodes in the simulated cluster.</summary>
    public int NodeCount { get; init; } = 3;

    /// <summary>Number of entity identifiers to distribute across records.</summary>
    public int EntityCount { get; init; } = 10;

    /// <summary>Simulated event throughput in events per second.</summary>
    public double EventsPerSecond { get; init; } = 100;
}
