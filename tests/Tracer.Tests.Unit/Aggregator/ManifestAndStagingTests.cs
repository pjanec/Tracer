using System.Security.Cryptography;
using FluentAssertions;
using Tracer.Aggregator.Configuration;
using Tracer.Aggregator.Consolidation;
using Tracer.Aggregator.Staging;
using Tracer.Bundle.Format;
using Tracer.Core.Time;
using Xunit;

namespace Tracer.Tests.Unit.Aggregator;

public sealed class ManifestAndStagingTests : IDisposable
{
    private readonly List<string> _dirs = new();

    private string TempDir()
    {
        var d = Path.Combine(Path.GetTempPath(), $"mst-{Guid.NewGuid():N}");
        Directory.CreateDirectory(d);
        _dirs.Add(d);
        return d;
    }

    public void Dispose()
    {
        foreach (var d in _dirs)
        {
            try { if (Directory.Exists(d)) Directory.Delete(d, recursive: true); } catch { }
        }
    }

    private static readonly DateTimeOffset _base = new(2026, 5, 19, 14, 0, 0, TimeSpan.Zero);

    private static Tracer.Core.Time.TimeRange MakeRange(DateTimeOffset start, DateTimeOffset end)
        => new(WallclockTime.FromDateTimeOffset(start), WallclockTime.FromDateTimeOffset(end));

    // ── ManifestBuilder tests ─────────────────────────────────────────────────

    [Fact]
    public async Task ManifestBuilder_FilesSha256_MatchActualFileHash()
    {
        var stagingDir = TempDir();

        // Write a couple of known-content files into staging
        var scenarioContent = """{"scenarioId":"test"}"""u8.ToArray();
        var topologyContent = """{"nodes":[]}"""u8.ToArray();
        await File.WriteAllBytesAsync(Path.Combine(stagingDir, "scenario.json"), scenarioContent);
        await File.WriteAllBytesAsync(Path.Combine(stagingDir, "topology.json"), topologyContent);

        var request = new AggregationRequest { OutputPath = Path.Combine(stagingDir, "output") };
        var timeRange = MakeRange(_base, _base.AddHours(1));
        var scenario = new ScenarioMetadata(
            ScenarioId: "test",
            SessionId: "sess",
            Label: null,
            StartUtc: _base,
            EndUtc: _base.AddHours(1));
        var statistics = new BundleStatistics
        {
            TotalEvents = 0,
            TotalSlowStateSamples = 0,
            TotalFastStateRows = 0,
            UncompressedBytes = 0,
        };

        var manifest = await ManifestBuilder.BuildAsync(
            stagingDir, request, timeRange, scenario, statistics);

        manifest.Files.Should().NotBeEmpty();

        // Verify each listed file's SHA-256 matches what we compute independently
        foreach (var entry in manifest.Files)
        {
            var absPath = Path.Combine(stagingDir, entry.Path.Replace('/', Path.DirectorySeparatorChar));
            File.Exists(absPath).Should().BeTrue($"file {entry.Path} should exist");

            var expectedHash = Convert.ToHexString(
                SHA256.HashData(await File.ReadAllBytesAsync(absPath))).ToLowerInvariant();
            entry.Sha256.Should().Be(expectedHash,
                $"SHA-256 for {entry.Path} should match the actual file contents");
        }
    }

    // ── StagingDirectory tests ────────────────────────────────────────────────

    [Fact]
    public async Task StagingDirectory_DisposeAsync_DeletesStagingDirectory()
    {
        var outputPath = Path.Combine(TempDir(), "output");
        var staging = await StagingDirectory.CreateAsync(outputPath);

        // Staging root must exist after creation
        Directory.Exists(staging.BundleStagingPath).Should().BeTrue();
        Directory.Exists(staging.SourcesPath).Should().BeTrue();

        // Capture the root path via reflection isn't needed — both sub-dirs share the same root
        // parent. We can derive the root from BundleStagingPath.
        var rootDir = Directory.GetParent(staging.BundleStagingPath)!.FullName;
        Directory.Exists(rootDir).Should().BeTrue("staging root should exist before disposal");

        await staging.DisposeAsync();

        Directory.Exists(rootDir).Should().BeFalse(
            "DisposeAsync should delete the entire staging temp directory");
    }
}
