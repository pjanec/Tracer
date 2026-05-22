using System.Globalization;
using System.Text.Json;
using DuckDB.NET.Data;
using Microsoft.Extensions.Logging;
using Tracer.Core.Identity;
using Tracer.Core.Time;
using Tracer.Storage.DuckDB.MultiInterval;
using EventId = Tracer.Core.Identity.EventId;

namespace Tracer.WebApi.Queries;

public enum TriggerResult { Fired, NotFired }

public sealed record TriggerEvaluation
{
    public required EventId EventId { get; init; }
    public required DateTimeOffset EvaluatedAtUtc { get; init; }
    public required string PublisherNode { get; init; }
    public required TraceId TraceId { get; init; }
    public required string TriggerId { get; init; }
    public string? TriggerLabel { get; init; }
    public required string Inputs { get; init; }
    public required TriggerResult Result { get; init; }
    public EventId? NextEventId { get; init; }
    public string? Reason { get; init; }
}

public sealed record TriggerEvalResult
{
    public required IReadOnlyList<TriggerEvaluation> Evaluations { get; init; }
}

public sealed class TriggerEvalService(LiveMultiIntervalReader reader, ILogger<TriggerEvalService> logger)
{
    private readonly LiveMultiIntervalReader _reader = reader;
    private readonly ILogger<TriggerEvalService> _logger = logger;

    public async Task<TriggerEvalResult> ListAsync(
        string sessionId,
        WallclockTime from,
        WallclockTime to,
        string? triggerIdFilter,
        TriggerResult? resultFilter,
        int limit,
        CancellationToken ct)
    {
        await using var pooled = await _reader.AcquireAsync(ct);

        var whereExtra = "";
        if (triggerIdFilter != null)
            whereExtra += " AND JSON_EXTRACT_STRING(payload, '$.triggerId') = $triggerId";
        if (resultFilter.HasValue)
            whereExtra += " AND JSON_EXTRACT_STRING(payload, '$.result') = $result";

        var innerSql = $"""
            SELECT event_id, trace_id, parent_event_id, sequence_number,
                   publish_wallclock, receive_wallclock, publisher_node, subscriber_node,
                   topic, entity_id, owning_player_id, scenario_phase, severity, notable_label, payload
            FROM events
            WHERE topic = 'scenario.trigger_evaluated'
              AND publish_wallclock >= $from
              AND publish_wallclock < $to
              {whereExtra}
            ORDER BY publish_wallclock DESC
            LIMIT $limit
            """;

        var sql = pooled.WithEventsCte(innerSql);

        using var cmd = pooled.Connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.Add(new DuckDBParameter("from", from.ToDateTimeOffset().UtcDateTime));
        cmd.Parameters.Add(new DuckDBParameter("to", to.ToDateTimeOffset().UtcDateTime));
        cmd.Parameters.Add(new DuckDBParameter("limit", limit));
        if (triggerIdFilter != null)
            cmd.Parameters.Add(new DuckDBParameter("triggerId", triggerIdFilter));
        if (resultFilter.HasValue)
        {
            var resultStr = resultFilter.Value == TriggerResult.Fired ? "fired" : "not-fired";
            cmd.Parameters.Add(new DuckDBParameter("result", resultStr));
        }

        var evaluations = new List<TriggerEvaluation>();
        using var dbReader = cmd.ExecuteReader();
        while (dbReader.Read())
        {
            var ev = EventRecordMapper.FromReader(dbReader);
            evaluations.Add(ParseEvaluation(ev));
        }

        _logger.LogDebug(
            "ListAsync returned {Count} trigger evaluations for session {SessionId}",
            evaluations.Count, sessionId);

        return new TriggerEvalResult { Evaluations = evaluations };
    }

    private static TriggerEvaluation ParseEvaluation(Core.Records.EventRecord ev)
    {
        try
        {
            using var doc = JsonDocument.Parse(ev.PayloadJson);
            var root = doc.RootElement;

            var triggerId = root.TryGetProperty("triggerId", out var tidProp)
                ? tidProp.GetString() ?? ""
                : "";

            var triggerLabel = root.TryGetProperty("triggerLabel", out var tlProp)
                ? tlProp.GetString()
                : null;

            var inputs = root.TryGetProperty("inputs", out var inputsProp)
                ? inputsProp.ToString()
                : "{}";

            var resultStr = root.TryGetProperty("result", out var resProp)
                ? resProp.GetString()
                : null;
            var result = resultStr == "fired" ? TriggerResult.Fired : TriggerResult.NotFired;

            EventId? nextEventId = null;
            if (root.TryGetProperty("nextEventId", out var neidProp))
            {
                var hexStr = neidProp.GetString();
                if (!string.IsNullOrEmpty(hexStr))
                    nextEventId = new EventId(ulong.Parse(hexStr, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
            }

            var reason = root.TryGetProperty("reason", out var reasonProp)
                ? reasonProp.GetString()
                : null;

            return new TriggerEvaluation
            {
                EventId = ev.EventId,
                EvaluatedAtUtc = ev.PublishWallclock.ToDateTimeOffset(),
                PublisherNode = ev.PublisherNode.Value,
                TraceId = ev.TraceId,
                TriggerId = triggerId,
                TriggerLabel = triggerLabel,
                Inputs = inputs,
                Result = result,
                NextEventId = nextEventId,
                Reason = reason,
            };
        }
        catch
        {
            return new TriggerEvaluation
            {
                EventId = ev.EventId,
                EvaluatedAtUtc = ev.PublishWallclock.ToDateTimeOffset(),
                PublisherNode = ev.PublisherNode.Value,
                TraceId = ev.TraceId,
                TriggerId = "(malformed payload)",
                Inputs = ev.PayloadJson,
                Result = TriggerResult.NotFired,
            };
        }
    }
}
