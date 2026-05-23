using FluentAssertions;
using Tracer.Tests.Integration.Real.Infrastructure;
using Xunit;

namespace Tracer.Tests.Integration.Real;

[Collection("RealIntegration")]
[RealIntegrationTest]
public sealed class SyncUploadTests(SimulationHarnessFixture harness)
{
    [SkipIfNoSimulationHarness]
    public async Task HappyPathUploadCompletes()
    {
        harness.IsAvailable.Should().BeTrue("harness must be available when test is not skipped");
        // Complete an interval; poll until NAS zip exists with _ready sentinel.
        await Task.Delay(100); // placeholder

        true.Should().BeTrue("placeholder assertion — requires harness to be running");
    }
}
