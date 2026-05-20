using System.Net.Http.Json;
using System.Text.Json;
using Tracer.WebApi.Contracts.Dto;

namespace Tracer.TestHarness.Assertions;

/// <summary>
/// Provides helpers that compare query results from a live Observer and an OfflineViewer
/// (both accessed via <see cref="HttpClient"/>), asserting that results are identical.
/// </summary>
public static class RoundTripAssertions
{
    private static readonly JsonSerializerOptions _opts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// Fetches <c>GET /api/sessions</c> from both clients and asserts that
    /// session IDs and event counts are equal. Throws <see cref="InvalidOperationException"/>
    /// with a descriptive message if they differ.
    /// </summary>
    public static async Task AssertSessionListsMatchAsync(
        HttpClient liveClient,
        HttpClient bundleClient,
        CancellationToken ct = default)
    {
        var liveSessions   = await GetSessionsAsync(liveClient, ct);
        var bundleSessions = await GetSessionsAsync(bundleClient, ct);

        var liveIds   = liveSessions.Select(s => s.SessionId).OrderBy(x => x).ToList();
        var bundleIds = bundleSessions.Select(s => s.SessionId).OrderBy(x => x).ToList();

        if (!liveIds.SequenceEqual(bundleIds))
        {
            throw new InvalidOperationException(
                $"Session ID lists differ.\n" +
                $"  Live:   [{string.Join(", ", liveIds)}]\n" +
                $"  Bundle: [{string.Join(", ", bundleIds)}]");
        }

        foreach (var liveSession in liveSessions)
        {
            var bundleSession = bundleSessions.FirstOrDefault(s => s.SessionId == liveSession.SessionId);
            if (bundleSession is null)
                throw new InvalidOperationException(
                    $"Session '{liveSession.SessionId}' present in live but not in bundle.");

            if (liveSession.EventCount != bundleSession.EventCount)
                throw new InvalidOperationException(
                    $"Session '{liveSession.SessionId}' event count mismatch: " +
                    $"live={liveSession.EventCount}, bundle={bundleSession.EventCount}.");
        }
    }

    /// <summary>
    /// Fetches <c>GET /api/scenario/notables?sessionId={id}</c> from both clients
    /// and asserts that notable count, IDs, severities, and publish timestamps are equal.
    /// Throws <see cref="InvalidOperationException"/> with a descriptive message if they differ.
    /// </summary>
    public static async Task AssertNotablesMatchAsync(
        HttpClient liveClient,
        HttpClient bundleClient,
        string sessionId,
        CancellationToken ct = default)
    {
        var liveNotables   = await GetNotablesAsync(liveClient,   sessionId, ct);
        var bundleNotables = await GetNotablesAsync(bundleClient, sessionId, ct);

        if (liveNotables.Count != bundleNotables.Count)
        {
            throw new InvalidOperationException(
                $"Notable count mismatch for session '{sessionId}': " +
                $"live={liveNotables.Count}, bundle={bundleNotables.Count}.");
        }

        var liveOrdered   = liveNotables.OrderBy(n => n.EventId).ToList();
        var bundleOrdered = bundleNotables.OrderBy(n => n.EventId).ToList();

        for (var i = 0; i < liveOrdered.Count; i++)
        {
            var l = liveOrdered[i];
            var b = bundleOrdered[i];

            if (l.EventId != b.EventId)
                throw new InvalidOperationException(
                    $"Notable [{i}] EventId mismatch: live='{l.EventId}', bundle='{b.EventId}'.");

            if (l.OccurredAtUtc != b.OccurredAtUtc)
                throw new InvalidOperationException(
                    $"Notable [{i}] timestamp mismatch: live='{l.OccurredAtUtc:O}', bundle='{b.OccurredAtUtc:O}'.");

            if (l.Severity != b.Severity)
                throw new InvalidOperationException(
                    $"Notable [{i}] severity mismatch: live='{l.Severity}', bundle='{b.Severity}'.");
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static async Task<IReadOnlyList<SessionDto>> GetSessionsAsync(
        HttpClient client, CancellationToken ct)
    {
        var result = await client.GetFromJsonAsync<IReadOnlyList<SessionDto>>("/api/sessions", _opts, ct);
        return result ?? Array.Empty<SessionDto>();
    }

    private static async Task<IReadOnlyList<NotableEventDto>> GetNotablesAsync(
        HttpClient client, string sessionId, CancellationToken ct)
    {
        var url = $"/api/scenario/notables?sessionId={Uri.EscapeDataString(sessionId)}";
        var result = await client.GetFromJsonAsync<IReadOnlyList<NotableEventDto>>(url, _opts, ct);
        return result ?? Array.Empty<NotableEventDto>();
    }
}
