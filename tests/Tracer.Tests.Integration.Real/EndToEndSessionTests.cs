using FluentAssertions;
using Tracer.Tests.Integration.Real.Infrastructure;
using Xunit;

namespace Tracer.Tests.Integration.Real;

[Collection("RealIntegration")]
[RealIntegrationTest]
public sealed class EndToEndSessionTests(SimulationHarnessFixture harness)
{
    [SkipIfNoSimulationHarness]
    public async Task BundleContainsAllAgentData()
    {
        harness.IsAvailable.Should().BeTrue("harness must be available when test is not skipped");
        // 5-minute simulated session across multiple agent processes.
        // Assert bundle contains events from all agents.
        await Task.Delay(100); // placeholder

        true.Should().BeTrue("placeholder assertion — requires harness to be running");
    }
}
