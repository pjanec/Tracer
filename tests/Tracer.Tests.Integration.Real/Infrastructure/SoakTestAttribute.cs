using Xunit;

namespace Tracer.Tests.Integration.Real.Infrastructure;

/// <summary>
/// Custom skip attribute for 48-hour soak tests. Skipped when TRACER_HARNESS_PATH is absent.
/// Pair with [Trait("Category", "SoakTest")] on the test method for CI filter support.
/// </summary>
public sealed class SoakTestAttribute : FactAttribute
{
    private const string EnvVar = "TRACER_HARNESS_PATH";

    public SoakTestAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(EnvVar)))
            Skip = $"Soak test skipped ({EnvVar} not set). Requires harness and 48 h of runtime.";
    }
}
