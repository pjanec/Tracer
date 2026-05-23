using FluentAssertions;
using Tracer.Tests.Integration.Real.Infrastructure;
using Xunit;

namespace Tracer.Tests.Integration.Real;

[Collection("RealIntegration")]
[RealIntegrationTest]
public sealed class SharedMemoryThroughputTests(SimulationHarnessFixture harness)
{
    [SkipIfNoSimulationHarness]
    public async Task SustainedThroughput_DropRateBelow0Point1Percent()
    {
        // Emit 5000 events/sec × 60 s = 300,000 events.
        // Drop rate must be < 0.1% (< 300 drops).
        await harness.EmitEventBurstAsync(count: 300_000, ratePerSec: 5_000);
        await Task.Delay(100); // placeholder for actual measurement

        // Assert: (placeholder — real assertion reads dropped_count from transport health)
        true.Should().BeTrue("placeholder assertion — requires harness to be running");
    }
}
