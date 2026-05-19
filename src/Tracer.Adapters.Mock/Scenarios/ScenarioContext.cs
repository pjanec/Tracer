using Tracer.Adapters.Mock.Generation;

namespace Tracer.Adapters.Mock.Scenarios;

/// <summary>
/// Runtime state threaded through every scenario invocation.
/// The <see cref="Random"/> instance is shared with <see cref="TraceIdGenerator"/>
/// so that all random draws advance a single deterministic sequence.
/// </summary>
public sealed class ScenarioContext
{
    public required SimulatedClock Clock { get; init; }

    /// <summary>
    /// Shared random — also owned by <see cref="TraceIdGen"/>.
    /// Callers must use this for all random draws to maintain a single sequence.
    /// </summary>
    public required Random Random { get; init; }

    public required ScenarioConfig Config { get; init; }

    public required TraceIdGenerator TraceIdGen { get; init; }
}
