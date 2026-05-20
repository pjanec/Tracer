using Tracer.Core.Identity;
using Tracer.Core.Time;

namespace Tracer.Core.Domain;

/// <summary>
/// Per-interval metadata written at interval close time.
/// </summary>
public sealed record IntervalManifest
{
    public required IntervalTimestamp IntervalStart { get; init; }
    public required IntervalTimestamp IntervalEnd { get; init; }
    public required AgentId NodeId { get; init; }
    public required string TracerVersion { get; init; }
    public required int SchemaVersion { get; init; }
    public required long EventCount { get; init; }
    public required long SlowStateCount { get; init; }
    public required IReadOnlyList<string> FastStateTopics { get; init; }
    public required IReadOnlyList<CaptureGap> CaptureGaps { get; init; }
    public required IReadOnlyList<SessionMarker> SessionMarkers { get; init; }
    public required WallclockTime FinalizedAt { get; init; }
    public required ManifestFinalizationReason FinalizationReason { get; init; }
}

/// <summary>
/// Reason an interval was finalized.
/// </summary>
public enum ManifestFinalizationReason
{
    ScheduledRotation,
    GracefulShutdown,
    RecoveryAfterCrash
}
