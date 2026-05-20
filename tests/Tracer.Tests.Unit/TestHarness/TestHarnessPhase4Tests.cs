using FluentAssertions;
using Tracer.Bundle.Validation;
using Tracer.TestHarness;
using Xunit;

namespace Tracer.Tests.Unit.TestHarness;

/// <summary>
/// Tests for the Phase 4 TestHarness additions: <see cref="AggregationFixture"/>
/// and <see cref="BundleFixture"/>.
/// </summary>
public sealed class TestHarnessPhase4Tests
{
    [Fact]
    public async Task AggregationFixture_RunsAndProducesBundle()
    {
        await using var fixture = await AggregationFixture.InitializeAsync();

        fixture.NasTimeRange.Should().NotBeNull("NasTimeRange should be populated after initialization");

        var outputPath = Path.Combine(Path.GetTempPath(), $"agg-fix-test-{Guid.NewGuid():N}");
        try
        {
            var result = await fixture.RunDefaultBuildAsync(outputPath);

            result.Statistics.TotalEvents.Should().BeGreaterThan(0,
                "the Calm scenario should produce at least one event");
            Directory.Exists(outputPath).Should().BeTrue("bundle directory should exist after build");
        }
        finally
        {
            if (Directory.Exists(outputPath))
                Directory.Delete(outputPath, recursive: true);
        }
    }

    [Fact]
    public async Task BundleFixture_ProducesValidBundle()
    {
        await using var fixture = await BundleFixture.InitializeAsync();

        fixture.BundlePath.Should().NotBeNullOrEmpty();
        fixture.Manifest.Should().NotBeNull();
        fixture.Manifest.BundleId.Should().NotBeNullOrEmpty("manifest must have a bundle ID");

        Directory.Exists(fixture.BundlePath).Should().BeTrue("bundle directory should exist");

        var validation = await BundleValidator.ValidateAsync(fixture.BundlePath, fixture.Manifest, strict: false);
        validation.IsValid.Should().BeTrue(
            $"bundle should be valid; errors: {string.Join(", ", validation.Errors.Select(e => e.Message))}");
    }

    [Fact]
    public async Task BundleFixture_CleansUpOnDispose()
    {
        var fixture = await BundleFixture.InitializeAsync();
        var bundlePath = fixture.BundlePath;

        Directory.Exists(bundlePath).Should().BeTrue("bundle must exist before disposal");

        await fixture.DisposeAsync();

        Directory.Exists(bundlePath).Should().BeFalse(
            "bundle directory should be deleted after disposal");
    }
}
