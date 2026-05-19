using DuckDB.NET.Data;
using Tracer.Core.Domain;
using Tracer.Core.Queries;
using Tracer.Storage.DuckDB.Internal;

namespace Tracer.Storage.DuckDB.Queries;

/// <summary>
/// Builds parameterized SQL queries for the events table.
/// </summary>
internal static class EventQueryBuilder
{
    internal static (string Sql, List<DuckDBParameter> Parameters) Build(EventQuery query)
    {
        var (whereSql, parameters) = BuildWhere(query.Filter);
        var order = query.Order switch
        {
            QueryOrder.PublishTimeAscending => "ORDER BY publish_wallclock ASC",
            QueryOrder.PublishTimeDescending => "ORDER BY publish_wallclock DESC",
            QueryOrder.SequenceNumberAscending => "ORDER BY sequence_number ASC",
            _ => "ORDER BY publish_wallclock ASC",
        };

        var sql = $"SELECT * FROM events WHERE 1=1{whereSql} {order} LIMIT {query.Limit} OFFSET {query.Offset}";
        return (sql, parameters);
    }

    internal static (string Sql, List<DuckDBParameter> Parameters) BuildCount(EventFilter filter)
    {
        var (whereSql, parameters) = BuildWhere(filter);
        var sql = $"SELECT COUNT(*) FROM events WHERE 1=1{whereSql}";
        return (sql, parameters);
    }

    private static (string WhereSql, List<DuckDBParameter> Parameters) BuildWhere(EventFilter filter)
    {
        var sb = new System.Text.StringBuilder();
        var parameters = new List<DuckDBParameter>();

        if (filter.From.HasValue)
        {
            sb.Append(" AND publish_wallclock >= $from");
            parameters.Add(new DuckDBParameter("from", Mapping.WallclockToDateTime(filter.From.Value)));
        }

        if (filter.To.HasValue)
        {
            sb.Append(" AND publish_wallclock < $to");
            parameters.Add(new DuckDBParameter("to", Mapping.WallclockToDateTime(filter.To.Value)));
        }

        if (filter.Topic.HasValue)
        {
            sb.Append(" AND topic = $topic");
            parameters.Add(new DuckDBParameter("topic", filter.Topic.Value.Value));
        }

        if (filter.PublisherNode.HasValue)
        {
            sb.Append(" AND publisher_node = $publisher_node");
            parameters.Add(new DuckDBParameter("publisher_node", filter.PublisherNode.Value.Value));
        }

        if (filter.SubscriberNode.HasValue)
        {
            sb.Append(" AND subscriber_node = $subscriber_node");
            parameters.Add(new DuckDBParameter("subscriber_node", filter.SubscriberNode.Value.Value));
        }

        if (filter.TraceId.HasValue)
        {
            sb.Append(" AND trace_id = $trace_id");
            parameters.Add(new DuckDBParameter("trace_id", filter.TraceId.Value.Value));
        }

        if (filter.EntityId.HasValue)
        {
            sb.Append(" AND entity_id = $entity_id");
            parameters.Add(new DuckDBParameter("entity_id", filter.EntityId.Value.Value));
        }

        if (filter.OwningPlayerId is not null)
        {
            sb.Append(" AND owning_player_id = $owning_player_id");
            parameters.Add(new DuckDBParameter("owning_player_id", filter.OwningPlayerId));
        }

        if (filter.MinSeverity.HasValue)
        {
            var severities = GetSeveritiesAtOrAbove(filter.MinSeverity.Value);
            var placeholders = new List<string>();
            for (int i = 0; i < severities.Count; i++)
            {
                var paramName = $"sev{i}";
                placeholders.Add($"${paramName}");
                parameters.Add(new DuckDBParameter(paramName, severities[i].ToString()));
            }
            sb.Append($" AND severity IN ({string.Join(", ", placeholders)})");
        }

        if (filter.PayloadSearch is not null)
        {
            var escaped = EscapeLike(filter.PayloadSearch);
            sb.Append(" AND payload LIKE $search");
            parameters.Add(new DuckDBParameter("search", $"%{escaped}%"));
        }

        return (sb.ToString(), parameters);
    }

    private static List<Severity> GetSeveritiesAtOrAbove(Severity min)
    {
        var result = new List<Severity>();
        foreach (Severity s in Enum.GetValues<Severity>())
        {
            if (s >= min) result.Add(s);
        }
        return result;
    }

    private static string EscapeLike(string input) =>
        input.Replace("%", "\\%").Replace("_", "\\_");
}
