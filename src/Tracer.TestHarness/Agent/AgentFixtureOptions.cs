namespace Tracer.TestHarness;

/// <summary>Options controlling how <see cref="TracerAgentFixture"/> is built.</summary>
public sealed record AgentFixtureOptions
{
    /// <summary>
    /// When <c>true</c> the fixture wires a <see cref="Tracer.Adapters.Mock.SimulatedClock"/>
    /// as <c>IClock</c> and exposes it via <see cref="TracerAgentFixture.SimulatedClock"/>.
    /// </summary>
    public bool UseSimulatedClock { get; init; } = false;

    /// <summary>Bounded channel capacity for the in-process transport.</summary>
    public int TransportCapacity { get; init; } = 10_000;

    /// <summary>Number of completed intervals to keep on disk.</summary>
    public int KeepLastNIntervals { get; init; } = 24;
}
