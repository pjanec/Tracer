using Tracer.Adapters.Mock.Scenarios;
using Tracer.Agent.Configuration;

namespace Tracer.FakeNode.Configuration;

/// <summary>
/// Top-level configuration for a FakeNode run.
/// </summary>
public sealed record FakeNodeConfig
{
    public required string ScenarioName { get; init; }
    public required ScenarioConfig ScenarioConfig { get; init; }
    public required AgentConfig AgentConfig { get; init; }
}
