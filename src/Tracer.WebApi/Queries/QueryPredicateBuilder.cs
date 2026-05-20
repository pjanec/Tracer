using System.Globalization;
using DuckDB.NET.Data;

namespace Tracer.WebApi.Queries;

public interface IEventFilter
{
    IReadOnlyList<string>? Topics { get; }
    IReadOnlyList<string>? Nodes { get; }
    string? TraceId { get; }
    IReadOnlyList<string>? EntityIds { get; }
    IReadOnlyList<string>? PlayerIds { get; }
    IReadOnlyList<string>? Severities { get; }
    bool NotablesOnly { get; }
}

public static class QueryPredicateBuilder
{
    /// <summary>
    /// Builds a WHERE clause and list of parameter names from the given filter.
    /// Includes time-range conditions when <paramref name="includeTimeRange"/> is true.
    /// Returns an empty string when no conditions exist.
    /// </summary>
    public static (string WhereSql, IReadOnlyList<string> ParamNames) Build(
        IEventFilter filter, bool includeTimeRange = false)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var clauses = new List<string>();
        var paramNames = new List<string>();

        if (includeTimeRange)
        {
            clauses.Add("publish_wallclock >= $from");
            clauses.Add("publish_wallclock < $to");
        }

        if (filter.NotablesOnly)
            clauses.Add("notable_label IS NOT NULL");

        if (filter.Topics is { Count: > 0 })
        {
            var placeholders = string.Join(", ", Enumerable.Range(0, filter.Topics.Count).Select(i => $"$topics_{i}"));
            clauses.Add($"topic IN ({placeholders})");
            for (int i = 0; i < filter.Topics.Count; i++) paramNames.Add($"topics_{i}");
        }

        if (filter.Nodes is { Count: > 0 })
        {
            var placeholders = string.Join(", ", Enumerable.Range(0, filter.Nodes.Count).Select(i => $"$nodes_{i}"));
            clauses.Add($"publisher_node IN ({placeholders})");
            for (int i = 0; i < filter.Nodes.Count; i++) paramNames.Add($"nodes_{i}");
        }

        if (filter.TraceId is not null)
            clauses.Add("trace_id = $traceId");

        if (filter.EntityIds is { Count: > 0 })
        {
            var placeholders = string.Join(", ", Enumerable.Range(0, filter.EntityIds.Count).Select(i => $"$entityIds_{i}"));
            clauses.Add($"entity_id IN ({placeholders})");
            for (int i = 0; i < filter.EntityIds.Count; i++) paramNames.Add($"entityIds_{i}");
        }

        if (filter.PlayerIds is { Count: > 0 })
        {
            var placeholders = string.Join(", ", Enumerable.Range(0, filter.PlayerIds.Count).Select(i => $"$playerIds_{i}"));
            clauses.Add($"owning_player_id IN ({placeholders})");
            for (int i = 0; i < filter.PlayerIds.Count; i++) paramNames.Add($"playerIds_{i}");
        }

        if (filter.Severities is { Count: > 0 })
        {
            var placeholders = string.Join(", ", Enumerable.Range(0, filter.Severities.Count).Select(i => $"$severities_{i}"));
            clauses.Add($"severity IN ({placeholders})");
            for (int i = 0; i < filter.Severities.Count; i++) paramNames.Add($"severities_{i}");
        }

        if (clauses.Count == 0)
            return (string.Empty, paramNames);

        return ("WHERE " + string.Join(" AND ", clauses), paramNames);
    }

    /// <summary>Binds all filter parameters (excluding time-range) to the command.</summary>
    public static void BindParameters(DuckDBCommand cmd, IEventFilter filter)
    {
        ArgumentNullException.ThrowIfNull(cmd);
        ArgumentNullException.ThrowIfNull(filter);

        if (filter.Topics is { Count: > 0 })
            for (int i = 0; i < filter.Topics.Count; i++)
                cmd.Parameters.Add(new DuckDBParameter($"topics_{i}", filter.Topics[i]));

        if (filter.Nodes is { Count: > 0 })
            for (int i = 0; i < filter.Nodes.Count; i++)
                cmd.Parameters.Add(new DuckDBParameter($"nodes_{i}", filter.Nodes[i]));

        if (filter.TraceId is not null)
        {
            if (ulong.TryParse(filter.TraceId, NumberStyles.HexNumber, null, out var traceIdVal))
                cmd.Parameters.Add(new DuckDBParameter("traceId", traceIdVal));
        }

        if (filter.EntityIds is { Count: > 0 })
            for (int i = 0; i < filter.EntityIds.Count; i++)
                cmd.Parameters.Add(new DuckDBParameter($"entityIds_{i}", filter.EntityIds[i]));

        if (filter.PlayerIds is { Count: > 0 })
            for (int i = 0; i < filter.PlayerIds.Count; i++)
                cmd.Parameters.Add(new DuckDBParameter($"playerIds_{i}", filter.PlayerIds[i]));

        if (filter.Severities is { Count: > 0 })
            for (int i = 0; i < filter.Severities.Count; i++)
                cmd.Parameters.Add(new DuckDBParameter($"severities_{i}", filter.Severities[i]));
    }
}
