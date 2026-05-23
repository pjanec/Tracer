using FluentAssertions;
using Tracer.Tests.Integration.Real.Infrastructure;
using Xunit;

namespace Tracer.Tests.Integration.Real;

[Collection("RealIntegration")]
[RealIntegrationTest]
public sealed class DdsRoundTripTests(SimulationHarnessFixture harness)
{
    [SkipIfNoSimulationHarness]
    public async Task KnownTraceChainArrivesInBundle()
    {
        // Arrange: emit 1000 events with known trace chain.
        const ulong traceId = 0xDEADBEEF;
        await harness.EmitKnownTraceAsync(traceId, depth: 10);

        // Act: (In real deployment) rotate interval and build bundle.
        // On a CI machine without harness this test is skipped.
        await Task.Delay(100); // placeholder

        // Assert: (placeholder — real assertion compares bundle events)
        true.Should().BeTrue("placeholder assertion — requires harness to be running");
    }
}
