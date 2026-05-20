using Tracer.Core.Abstractions;
using Tracer.Core.Domain;
using Tracer.Core.Time;

namespace Tracer.Aggregator.Discovery;

/// <summary>
/// Resolves a session ID to a <see cref="TimeRange"/> by scanning interval manifests
/// for session-start and session-end markers.
/// </summary>
public static class SessionResolver
{
    /// <summary>
    /// Scans all interval manifests accessible via <paramref name="reader"/> for markers
    /// belonging to <paramref name="sessionId"/> and derives the session's time range.
    /// </summary>
    /// <returns>
    /// A <see cref="TimeRange"/> from the earliest session-start to the latest session-end marker,
    /// or from the earliest start to <see cref="DateTimeOffset.UtcNow"/> if the session has not
    /// ended. Returns <c>null</c> if no markers for <paramref name="sessionId"/> are found.
    /// </returns>
    public static async Task<TimeRange?> ResolveAsync(
        ITelemetryStorageReader reader,
        string sessionId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(sessionId);

        DateTimeOffset? startedAt = null;
        DateTimeOffset? endedAt = null;

        var allNodes = await reader.ListNodesAsync(ct);
        foreach (var nodeId in allNodes)
        {
            var intervals = await reader.ListIntervalsAsync(nodeId, ct);
            foreach (var iv in intervals)
            {
                var manifest = await reader.ReadIntervalManifestAsync(nodeId, iv, ct);
                if (manifest is null) continue;

                foreach (var marker in manifest.SessionMarkers)
                {
                    if (!string.Equals(marker.SessionId, sessionId, StringComparison.Ordinal))
                        continue;

                    var when = marker.Wallclock.ToDateTimeOffset();
                    if (marker.Type == SessionMarkerType.Start)
                    {
                        if (startedAt is null || when < startedAt)
                            startedAt = when;
                    }
                    else if (marker.Type == SessionMarkerType.End)
                    {
                        if (endedAt is null || when > endedAt)
                            endedAt = when;
                    }
                }
            }
        }

        if (startedAt is null) return null;

        var end = endedAt ?? DateTimeOffset.UtcNow;
        return new TimeRange(
            WallclockTime.FromDateTimeOffset(startedAt.Value),
            WallclockTime.FromDateTimeOffset(end));
    }
}
