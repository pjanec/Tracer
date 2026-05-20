using System.Text.Json;
using Tracer.Aggregator.Discovery;

namespace Tracer.Aggregator.Consolidation;

/// <summary>
/// Writes scenario.json, topology.json, and source_intervals.json to the bundle staging directory.
/// </summary>
internal static class BundleMetadataWriter
{
    private static readonly JsonSerializerOptions _opts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static async Task WriteAsync(
        string bundleStagingPath,
        ScenarioMetadata scenario,
        BundleTopology topology,
        IReadOnlyList<SourceIntervalEntry> sourceIntervals,
        CancellationToken ct = default)
    {
        await WriteJsonAsync(
            Path.Combine(bundleStagingPath, "scenario.json"),
            BuildScenarioDoc(scenario), ct);

        await WriteJsonAsync(
            Path.Combine(bundleStagingPath, "topology.json"),
            BuildTopologyDoc(topology), ct);

        await WriteJsonAsync(
            Path.Combine(bundleStagingPath, "source_intervals.json"),
            BuildSourceIntervalsDoc(sourceIntervals), ct);
    }

    private static object BuildScenarioDoc(ScenarioMetadata s) => new
    {
        s.ScenarioId,
        s.SessionId,
        s.Label,
        s.StartUtc,
        s.EndUtc,
    };

    private static object BuildTopologyDoc(BundleTopology t) => new
    {
        nodes = t.Nodes.Select(n => new
        {
            n.NodeId,
            n.FirstSeenUtc,
            n.LastSeenUtc,
            n.EventsPublished,
        }),
    };

    private static object BuildSourceIntervalsDoc(IReadOnlyList<SourceIntervalEntry> sources) => new
    {
        sources = sources.Select(s => new
        {
            s.NodeId,
            s.IntervalTimestamp,
            s.IntervalSourcePath,
            s.ContributedEventCount,
        }),
    };

    private static async Task WriteJsonAsync(string path, object obj, CancellationToken ct)
    {
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, obj, _opts, ct);
    }
}
