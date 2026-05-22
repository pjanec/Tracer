using System.Data;
using DuckDB.NET.Data;
using Microsoft.Extensions.Logging;
using Tracer.Core.Time;
using Tracer.Storage.DuckDB.MultiInterval;

namespace Tracer.WebApi.Queries;

public sealed record EntitySummary
{
    public required string EntityId { get; init; }
    public required DateTimeOffset FirstSeenUtc { get; init; }
    public required DateTimeOffset LastSeenUtc { get; init; }
    public required long EventCount { get; init; }
    public string? SamplePlayerId { get; init; }
    public required IReadOnlyList<string> Topics { get; init; }
}

public sealed class EntityDiscoveryService(LiveMultiIntervalReader reader, ILogger<EntityDiscoveryService> logger)
{
    public async Task<IReadOnlyList<EntitySummary>> DiscoverAsync(
        string sessionId,
        WallclockTime sessionStart,
        WallclockTime sessionEnd,
        string? topicFilter,
        string? playerFilter,
        int limit,
        CancellationToken ct)
    {
        await using var pooled = await reader.AcquireAsync(ct);

        var whereExtra = "";
        if (topicFilter != null) whereExtra += " AND topic = $topicFilter";
        if (playerFilter != null) whereExtra += " AND owning_player_id = $playerFilter";

        var sql = pooled.WithEventsCte($"""
            SELECT entity_id,
                   MIN(publish_wallclock)                        AS first_seen,
                   MAX(publish_wallclock)                        AS last_seen,
                   COUNT(*)                                      AS event_count,
                   ANY_VALUE(owning_player_id)                   AS sample_player_id,
                   ARRAY_AGG(DISTINCT topic ORDER BY topic)      AS topics
            FROM events
            WHERE entity_id IS NOT NULL
              AND publish_wallclock >= $from
              AND publish_wallclock < $to
              {whereExtra}
            GROUP BY entity_id
            ORDER BY event_count DESC
            LIMIT $limit
            """);

        using var cmd = pooled.Connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.Add(new DuckDBParameter("from", sessionStart.ToDateTimeOffset().UtcDateTime));
        cmd.Parameters.Add(new DuckDBParameter("to", sessionEnd.ToDateTimeOffset().UtcDateTime));
        cmd.Parameters.Add(new DuckDBParameter("limit", limit));
        if (topicFilter != null) cmd.Parameters.Add(new DuckDBParameter("topicFilter", topicFilter));
        if (playerFilter != null) cmd.Parameters.Add(new DuckDBParameter("playerFilter", playerFilter));

        var results = new List<EntitySummary>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var entityId = r.GetString(0);
            var firstSeen = GetUtcDateTimeOffset(r, 1);
            var lastSeen = GetUtcDateTimeOffset(r, 2);
            var eventCount = Convert.ToInt64(r.GetValue(3));
            var samplePlayerId = r.IsDBNull(4) ? null : r.GetString(4);
            var topics = ReadStringList(r, 5);

            results.Add(new EntitySummary
            {
                EntityId = entityId,
                FirstSeenUtc = firstSeen,
                LastSeenUtc = lastSeen,
                EventCount = eventCount,
                SamplePlayerId = samplePlayerId,
                Topics = topics,
            });
        }

        logger.LogDebug("DiscoverAsync returned {Count} entities for session {SessionId}", results.Count, sessionId);
        return results;
    }

    private static DateTimeOffset GetUtcDateTimeOffset(IDataReader reader, int ordinal)
    {
        var dt = (DateTime)reader.GetValue(ordinal);
        return new DateTimeOffset(dt, TimeSpan.Zero);
    }

    private static IReadOnlyList<string> ReadStringList(IDataReader r, int col)
    {
        if (r.IsDBNull(col)) return Array.Empty<string>();
        var raw = r.GetValue(col);
        if (raw is List<string> list) return list;
        if (raw is IEnumerable<object> enumerable)
            return enumerable.Select(x => x?.ToString() ?? "").ToList();
        return Array.Empty<string>();
    }
}
