using DuckDB.NET.Data;
using Microsoft.Extensions.Logging;
using Tracer.Core.Time;
using Tracer.Storage.DuckDB.MultiInterval;

namespace Tracer.WebApi.Queries;

public sealed record SlowStateSample
{
    public required string Topic { get; init; }
    public required WallclockTime PublishWallclock { get; init; }
    public required string PayloadJson { get; init; }
    public required ulong TraceId { get; init; }
}

public sealed record EntitySlowStateResult
{
    public required string EntityId { get; init; }
    public required IReadOnlyDictionary<string, IReadOnlyList<SlowStateSample>> ByTopic { get; init; }
}

public sealed class EntitySlowStateService(LiveMultiIntervalReader reader, ILogger<EntitySlowStateService> logger)
{
    public async Task<EntitySlowStateResult> GetAsync(
        string entityId,
        WallclockTime from,
        WallclockTime to,
        IReadOnlyList<string>? topicFilter,
        CancellationToken ct)
    {
        await using var pooled = await reader.AcquireAsync(ct);

        var whereClause = "WHERE instance_key = $entityId" +
                          " AND publish_wallclock >= $from" +
                          " AND publish_wallclock < $to";

        if (topicFilter?.Count > 0)
        {
            var inList = string.Join(",", Enumerable.Range(0, topicFilter.Count).Select(i => $"$topic{i}"));
            whereClause += $" AND topic IN ({inList})";
        }

        var sql = pooled.BuildSlowStateUnionSql(
            whereClause: whereClause,
            orderByClause: "ORDER BY publish_wallclock");

        using var cmd = pooled.Connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.Add(new DuckDBParameter("entityId", entityId));
        cmd.Parameters.Add(new DuckDBParameter("from", from.ToDateTimeOffset().UtcDateTime));
        cmd.Parameters.Add(new DuckDBParameter("to", to.ToDateTimeOffset().UtcDateTime));
        for (int i = 0; topicFilter != null && i < topicFilter.Count; i++)
            cmd.Parameters.Add(new DuckDBParameter($"topic{i}", topicFilter[i]));

        var byTopic = new SortedDictionary<string, List<SlowStateSample>>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            // slow_state columns (schema order, no __source_alias in PooledMultiIntervalConnection):
            // 0: sequence_number, 1: publish_wallclock, 2: receive_wallclock,
            // 3: publisher_node, 4: subscriber_node, 5: topic, 6: instance_key,
            // 7: entity_id (nullable), 8: trace_id (nullable), 9: payload
            var topic = r.GetString(5);
            var publishWallclock = GetWallclock(r, 1);
            var payload = r.GetString(9);
            var traceId = r.IsDBNull(8) ? 0UL : Convert.ToUInt64(r.GetValue(8));

            if (!byTopic.TryGetValue(topic, out var list))
                byTopic[topic] = list = new List<SlowStateSample>();

            list.Add(new SlowStateSample
            {
                Topic = topic,
                PublishWallclock = publishWallclock,
                PayloadJson = payload,
                TraceId = traceId,
            });
        }

        var result = byTopic.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyList<SlowStateSample>)kv.Value);

        logger.LogDebug("GetAsync returned {TopicCount} topics for entity {EntityId}", result.Count, entityId);
        return new EntitySlowStateResult { EntityId = entityId, ByTopic = result };
    }

    private static WallclockTime GetWallclock(System.Data.IDataReader reader, int ordinal)
    {
        var dt = (DateTime)reader.GetValue(ordinal);
        return new WallclockTime((dt.Ticks - DateTime.UnixEpoch.Ticks) * 100L);
    }
}
