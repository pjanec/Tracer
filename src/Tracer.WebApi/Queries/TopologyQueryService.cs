using Tracer.WebApi.Contracts.Dto;
using Tracer.WebApi.Lifecycle;

namespace Tracer.WebApi.Queries;

public sealed class TopologyQueryService(ReadOnlyConnectionPool pool)
{
    private readonly ReadOnlyConnectionPool _pool = pool;

    public async Task<TopologyDto> GetAsync(CancellationToken ct)
    {
        await using var pooled = await _pool.AcquireAsync(ct);
        var conn = pooled.Connection;

        var nodes = new List<NodeInfoDto>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT publisher_node,
                   MIN(publish_wallclock) AS first_seen,
                   MAX(publish_wallclock) AS last_seen,
                   COUNT(*) AS event_count
            FROM events
            GROUP BY publisher_node
            ORDER BY publisher_node
            """;
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var nodeId = reader.GetString(0);
            var firstDt = (DateTime)reader.GetValue(1);
            var lastDt = (DateTime)reader.GetValue(2);
            var count = Convert.ToInt64(reader.GetValue(3));
            nodes.Add(new NodeInfoDto
            {
                NodeId = nodeId,
                FirstSeenUtc = new DateTimeOffset(firstDt, TimeSpan.Zero),
                LastSeenUtc = new DateTimeOffset(lastDt, TimeSpan.Zero),
                EventsPublished = count,
            });
        }

        return new TopologyDto
        {
            Nodes = nodes,
            AsOfUtc = DateTimeOffset.UtcNow,
        };
    }
}

