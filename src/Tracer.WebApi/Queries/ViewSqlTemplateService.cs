using System.Text;

namespace Tracer.WebApi.Queries;

/// <summary>
/// Generates SQL templates corresponding to the built-in analytical views.
/// Used by the "Show SQL for this view" affordance and the /api/sql/view-template endpoint.
/// </summary>
public sealed class ViewSqlTemplateService
{
    private static readonly HashSet<string> KnownViews = new(StringComparer.OrdinalIgnoreCase)
    {
        "timeline", "entity-history", "causal", "latency", "gaps", "topology",
    };

    public bool IsKnownView(string view) => KnownViews.Contains(view);

    public ViewSqlTemplate Generate(string view, ViewTemplateParams p)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(p);
        return view.ToLowerInvariant() switch
        {
            "timeline"       => GenerateTimeline(p),
            "entity-history" => GenerateEntityHistory(p),
            "causal"         => GenerateCausal(p),
            "latency"        => GenerateLatency(p),
            "gaps"           => GenerateGaps(p),
            "topology"       => GenerateTopology(p),
            _ => throw new ArgumentException($"Unknown view type: {view}", nameof(view)),
        };
    }

    private static ViewSqlTemplate GenerateTimeline(ViewTemplateParams p)
    {
        var sb = new StringBuilder();
        sb.Append("SELECT publish_wallclock, publisher_node, topic, event_id");
        sb.AppendLine();
        sb.Append("FROM events");
        sb.AppendLine();
        var clauses = new List<string>();
        if (p.From.HasValue) clauses.Add($"publish_wallclock >= '{p.From.Value:O}'");
        if (p.To.HasValue) clauses.Add($"publish_wallclock < '{p.To.Value:O}'");
        if (!string.IsNullOrEmpty(p.Topic)) clauses.Add($"topic = '{SqlEscape(p.Topic)}'");
        if (!string.IsNullOrEmpty(p.PublisherNode)) clauses.Add($"publisher_node = '{SqlEscape(p.PublisherNode)}'");
        if (clauses.Count > 0) { sb.Append("WHERE "); sb.AppendLine(string.Join("\n  AND ", clauses)); }
        sb.Append("ORDER BY publish_wallclock");
        sb.AppendLine();
        sb.Append("LIMIT 1000");
        return new ViewSqlTemplate(sb.ToString(), "Timeline view: recent events ordered by publication time");
    }

    private static ViewSqlTemplate GenerateEntityHistory(ViewTemplateParams p)
    {
        var sb = new StringBuilder();
        sb.Append("SELECT event_id, topic, publish_wallclock");
        sb.AppendLine();
        sb.Append("FROM events");
        sb.AppendLine();
        var clauses = new List<string>();
        if (!string.IsNullOrEmpty(p.EntityId)) clauses.Add($"entity_id = '{SqlEscape(p.EntityId)}'");
        if (p.From.HasValue) clauses.Add($"publish_wallclock >= '{p.From.Value:O}'");
        if (p.To.HasValue) clauses.Add($"publish_wallclock < '{p.To.Value:O}'");
        if (clauses.Count > 0) { sb.Append("WHERE "); sb.AppendLine(string.Join("\n  AND ", clauses)); }
        sb.Append("ORDER BY publish_wallclock");
        return new ViewSqlTemplate(sb.ToString(), "Entity history: events referencing a specific entity");
    }

    private static ViewSqlTemplate GenerateCausal(ViewTemplateParams p)
    {
        var sb = new StringBuilder();
        sb.Append("SELECT event_id, publisher_node, topic, publish_wallclock");
        sb.AppendLine();
        sb.Append("FROM events");
        sb.AppendLine();
        var clauses = new List<string>();
        if (!string.IsNullOrEmpty(p.TraceId)) clauses.Add($"trace_id = '{SqlEscape(p.TraceId)}'");
        if (clauses.Count > 0) { sb.Append("WHERE "); sb.AppendLine(string.Join("\n  AND ", clauses)); }
        sb.Append("ORDER BY publish_wallclock");
        return new ViewSqlTemplate(sb.ToString(), "Causal view: all events sharing a trace lineage");
    }

    private static ViewSqlTemplate GenerateLatency(ViewTemplateParams p)
    {
        var sb = new StringBuilder();
        sb.AppendLine("SELECT topic,");
        sb.AppendLine("  COUNT(*) AS sample_count,");
        sb.AppendLine("  APPROX_QUANTILE((EXTRACT(EPOCH FROM (receive_wallclock - publish_wallclock)) * 1000.0), 0.50) AS p50_ms,");
        sb.AppendLine("  APPROX_QUANTILE((EXTRACT(EPOCH FROM (receive_wallclock - publish_wallclock)) * 1000.0), 0.99) AS p99_ms");
        sb.Append("FROM events");
        sb.AppendLine();
        var clauses = new List<string> { "publisher_node != subscriber_node" };
        if (p.From.HasValue) clauses.Add($"publish_wallclock >= '{p.From.Value:O}'");
        if (p.To.HasValue) clauses.Add($"publish_wallclock < '{p.To.Value:O}'");
        if (!string.IsNullOrEmpty(p.Topic)) clauses.Add($"topic = '{SqlEscape(p.Topic)}'");
        sb.Append("WHERE "); sb.AppendLine(string.Join("\n  AND ", clauses));
        sb.AppendLine("GROUP BY topic");
        sb.Append("ORDER BY p99_ms DESC");
        return new ViewSqlTemplate(sb.ToString(), "Latency distribution: per-topic p50/p99 latency percentiles");
    }

    private static ViewSqlTemplate GenerateGaps(ViewTemplateParams p)
    {
        var sb = new StringBuilder();
        sb.AppendLine("WITH topic_times AS (");
        sb.AppendLine("  SELECT topic, publish_wallclock,");
        sb.AppendLine("    LAG(publish_wallclock) OVER (PARTITION BY topic ORDER BY publish_wallclock) AS prev_time");
        sb.AppendLine("  FROM events");
        var clauses = new List<string>();
        if (p.From.HasValue) clauses.Add($"  publish_wallclock >= '{p.From.Value:O}'");
        if (p.To.HasValue) clauses.Add($"  publish_wallclock < '{p.To.Value:O}'");
        if (!string.IsNullOrEmpty(p.Topic)) clauses.Add($"  topic = '{SqlEscape(p.Topic)}'");
        if (clauses.Count > 0) { sb.Append("  WHERE "); sb.AppendLine(string.Join("\n    AND ", clauses)); }
        sb.AppendLine(")");
        sb.AppendLine("SELECT topic, prev_time, publish_wallclock,");
        sb.AppendLine("  EXTRACT(EPOCH FROM (publish_wallclock - prev_time)) AS gap_seconds");
        sb.AppendLine("FROM topic_times");
        sb.AppendLine("WHERE prev_time IS NOT NULL");
        sb.AppendLine("  AND EXTRACT(EPOCH FROM (publish_wallclock - prev_time)) > 1.0");
        sb.Append("ORDER BY gap_seconds DESC");
        return new ViewSqlTemplate(sb.ToString(), "Gap detection: intervals where a topic was silent");
    }

    private static ViewSqlTemplate GenerateTopology(ViewTemplateParams p)
    {
        var sb = new StringBuilder();
        sb.AppendLine("SELECT publisher_node, subscriber_node, topic, COUNT(*) AS event_count");
        sb.Append("FROM events");
        sb.AppendLine();
        var clauses = new List<string>();
        if (p.From.HasValue) clauses.Add($"publish_wallclock >= '{p.From.Value:O}'");
        if (p.To.HasValue) clauses.Add($"publish_wallclock < '{p.To.Value:O}'");
        if (clauses.Count > 0) { sb.Append("WHERE "); sb.AppendLine(string.Join("\n  AND ", clauses)); }
        sb.AppendLine("GROUP BY publisher_node, subscriber_node, topic");
        sb.Append("ORDER BY event_count DESC");
        return new ViewSqlTemplate(sb.ToString(), "Network topology: publisher-subscriber event flow by topic");
    }

    /// <summary>Escapes single-quotes in SQL string literals (replaces ' with '').</summary>
    internal static string SqlEscape(string value) => value.Replace("'", "''");
}

public sealed record ViewTemplateParams
{
    public DateTimeOffset? From { get; init; }
    public DateTimeOffset? To { get; init; }
    public string? Topic { get; init; }
    public string? PublisherNode { get; init; }
    public string? EntityId { get; init; }
    public string? TraceId { get; init; }
}

public sealed record ViewSqlTemplate(string Sql, string Description);
