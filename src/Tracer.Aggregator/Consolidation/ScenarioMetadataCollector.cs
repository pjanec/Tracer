using System.Text.Json;
using Tracer.Core.Time;

namespace Tracer.Aggregator.Consolidation;

/// <summary>Stub scenario metadata returned by the collector.</summary>
internal sealed record ScenarioMetadata(
    string ScenarioId,
    string SessionId,
    string? Label,
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc);

/// <summary>
/// Stub implementation: returns minimal scenario metadata derived from the time range.
/// Will be replaced with a real implementation in TRC-P4-006.
/// </summary>
internal static class ScenarioMetadataCollector
{
    public static Task<ScenarioMetadata> CollectAsync(
        string eventsDbPath,
        TimeRange timeRange,
        CancellationToken ct = default)
    {
        return Task.FromResult(new ScenarioMetadata(
            ScenarioId: "unknown",
            SessionId: "unknown",
            Label: null,
            StartUtc: timeRange.StartUtc.ToDateTimeOffset(),
            EndUtc: timeRange.EndUtc.ToDateTimeOffset()));
    }
}
