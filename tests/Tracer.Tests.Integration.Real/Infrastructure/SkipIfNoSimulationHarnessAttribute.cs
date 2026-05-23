using Xunit;

namespace Tracer.Tests.Integration.Real.Infrastructure;

/// <summary>
/// Use instead of [Fact] on tests that require the simulation harness process.
/// The test is automatically skipped (not failed) when TRACER_HARNESS_PATH is not set.
/// </summary>
public sealed class SkipIfNoSimulationHarnessAttribute : FactAttribute
{
    private const string EnvVar = "TRACER_HARNESS_PATH";

    public SkipIfNoSimulationHarnessAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(EnvVar)))
            Skip = $"Simulation harness unavailable ({EnvVar} not set). " +
                   "See README-integration-real.md for setup instructions.";
    }
}
