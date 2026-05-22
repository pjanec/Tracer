using DuckDB.NET.Data;
using Tracer.Core.Time;
using Tracer.Storage.DuckDB.MultiInterval;

namespace Tracer.WebApi.Queries;

public sealed record NetworkTopology
{
    public required IReadOnlyList<string> Nodes { get; init; }
    public required IReadOnlyList<TopologyEdge> Edges { get; init; }
}

public sealed record TopologyEdge
{
    public required string Topic { get; init; }
    public required string PublisherNode { get; init; }
    public required string SubscriberNode { get; init; }
    public required long MessageCount { get; init; }
    public required DateTimeOffset FirstSeenUtc { get; init; }
    public required DateTimeOffset LastSeenUtc { get; init; }
}

public sealed class NetworkTopologyService(LiveMultiIntervalReader reader)
{
    private readonly LiveMultiIntervalReader _reader = reader;

    public async Task<NetworkTopology> GetAsync(WallclockTime from, WallclockTime to, CancellationToken ct)
    {
        await using var pooled = await _reader.AcquireAsync(ct);

        var sql = pooled.WithEventsCte("""
            SELECT
                topic, publisher_node, subscriber_node,
                COUNT(*) AS message_count,
                MIN(publish_wallclock) AS first_seen,
                MAX(publish_wallclock) AS last_seen
            FROM events
            WHERE publish_wallclock >= $from
              AND publish_wallclock < $to
              AND publisher_node != subscriber_node
            GROUP BY topic, publisher_node, subscriber_node
            ORDER BY message_count DESC
            """);

        using var cmd = pooled.Connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.Add(new DuckDBParameter("from", from.ToDateTimeOffset().UtcDateTime));
        cmd.Parameters.Add(new DuckDBParameter("to", to.ToDateTimeOffset().UtcDateTime));

        var edges = new List<TopologyEdge>();
        var nodeSet = new HashSet<string>();

        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var topic = r.GetString(0);
            var pub = r.GetString(1);
            var sub = r.GetString(2);
            var count = Convert.ToInt64(r.GetValue(3));
            var firstDt = (DateTime)r.GetValue(4);
            var lastDt = (DateTime)r.GetValue(5);

            nodeSet.Add(pub);
            nodeSet.Add(sub);

            edges.Add(new TopologyEdge
            {
                Topic = topic,
                PublisherNode = pub,
                SubscriberNode = sub,
                MessageCount = count,
                FirstSeenUtc = new DateTimeOffset(firstDt, TimeSpan.Zero),
                LastSeenUtc = new DateTimeOffset(lastDt, TimeSpan.Zero),
            });
        }

        var nodes = nodeSet.OrderBy(n => n).ToList();
        return new NetworkTopology { Nodes = nodes, Edges = edges };
    }
}
