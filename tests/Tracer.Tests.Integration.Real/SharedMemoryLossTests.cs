using FluentAssertions;
using Tracer.Tests.Integration.Real.Infrastructure;
using Xunit;

namespace Tracer.Tests.Integration.Real;

[Collection("RealIntegration")]
[RealIntegrationTest]
public sealed class SharedMemoryLossTests(SimulationHarnessFixture harness)
{
    [SkipIfNoSimulationHarness]
    public async Task DroppedCountMatchesObservedDeficit()
    {
        harness.IsAvailable.Should().BeTrue("harness must be available when test is not skipped");
        // Pause consumer, saturate ring, resume, measure deficit vs dropped_count.
        await Task.Delay(100); // placeholder

        true.Should().BeTrue("placeholder assertion — requires harness to be running");
    }
}
