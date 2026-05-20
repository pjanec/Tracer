using Tracer.Aggregator.Configuration;
using Tracer.Aggregator.Discovery;
using Tracer.Bundle.Format;
using Tracer.Bundle.Packaging;
using Tracer.Core.Time;

namespace Tracer.Aggregator.Consolidation;

/// <summary>
/// Builds the <see cref="BundleManifest"/> by scanning all files in the bundle staging directory
/// and computing per-file sizes and SHA-256 hashes.
/// </summary>
internal static class ManifestBuilder
{
    public static async Task<BundleManifest> BuildAsync(
        string bundleStagingPath,
        AggregationRequest request,
        TimeRange timeRange,
        ScenarioMetadata scenario,
        BundleStatistics statistics,
        CancellationToken ct = default)
    {
        // Enumerate files to include (skip checksums.txt and annotations/.keep — those are written later)
        var filesToList = new List<string>
        {
            "events.duckdb",
            "slow_state.duckdb",
            "scenario.json",
            "topology.json",
            "source_intervals.json",
        };

        // Add any fast_state Parquet files
        var fastStateDir = Path.Combine(bundleStagingPath, BundleLayout.FastStateDirectory);
        if (Directory.Exists(fastStateDir))
        {
            foreach (var parquet in Directory.GetFiles(fastStateDir, "*.parquet", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(bundleStagingPath, parquet).Replace('\\', '/');
                filesToList.Add(relative);
            }
        }

        var fileEntries = new List<BundleFileEntry>();
        foreach (var relativePath in filesToList)
        {
            var absPath = Path.Combine(bundleStagingPath, relativePath);
            if (!File.Exists(absPath)) continue;

            var size = new FileInfo(absPath).Length;
            var hash = await BundleDirectoryWriter.ComputeSha256Async(absPath, ct);
            fileEntries.Add(new BundleFileEntry { Path = relativePath, SizeBytes = size, Sha256 = hash });
        }

        return new BundleManifest
        {
            BundleId = Ulid.NewUlid().ToString(),
            SchemaVersion = BundleSchemaV1.CurrentVersion,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            TracerVersion = "1.0.0",
            Writer = new BundleWriterInfo
            {
                Tool = request.WriterTool ?? "tracer-aggregate",
                Version = "1.0.0",
                Host = Environment.MachineName,
            },
            TimeRange = new BundleTimeRange
            {
                StartUtc = timeRange.StartUtc.ToDateTimeOffset(),
                EndUtc = timeRange.EndUtc.ToDateTimeOffset(),
            },
            SessionContext = new BundleSessionContext
            {
                SessionId = scenario.SessionId,
                ScenarioId = scenario.ScenarioId,
                Label = scenario.Label,
            },
            ParticipatingNodes = Array.Empty<string>(),
            FastStateScope = request.FastStateScope.ToString().ToLowerInvariant(),
            FastStateEntities = request.FastStateEntities?.ToArray() ?? Array.Empty<string>(),
            Statistics = statistics,
            Files = fileEntries,
        };
    }
}
