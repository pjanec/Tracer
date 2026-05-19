using DuckDB.NET.Data;
using Microsoft.Extensions.Logging;
using Tracer.Core.Abstractions;
using Tracer.Core.Records;
using Tracer.Storage.DuckDB.Internal;
using Tracer.Storage.DuckDB.Schema;

namespace Tracer.Storage.DuckDB;

/// <summary>
/// DuckDB-backed implementation of <see cref="IDiagnosticStorageWriter"/>.
/// Thread-safe: all append operations are serialised via an internal lock.
/// </summary>
public sealed class DuckDbStorageWriter : IDiagnosticStorageWriter
{
    private readonly DuckDBConnection _connection;
    private readonly ILogger<DuckDbStorageWriter> _logger;
    private readonly object _lock = new();
    private DuckDBAppender _eventsAppender;
    private DuckDBAppender _stateAppender;
    private bool _disposed;

    private DuckDbStorageWriter(
        DuckDBConnection connection,
        DuckDBAppender eventsAppender,
        DuckDBAppender stateAppender,
        ILogger<DuckDbStorageWriter> logger)
    {
        _connection = connection;
        _eventsAppender = eventsAppender;
        _stateAppender = stateAppender;
        _logger = logger;
    }

    /// <summary>
    /// Creates (or opens) a DuckDB database at <paramref name="dbPath"/> and initialises
    /// schema version 1, returning a ready-to-use writer.
    /// </summary>
    public static async Task<DuckDbStorageWriter> CreateAsync(
        string dbPath,
        ILogger<DuckDbStorageWriter> logger,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dbPath);
        ArgumentNullException.ThrowIfNull(logger);
        ct.ThrowIfCancellationRequested();

        var connection = new DuckDBConnection($"Data Source={dbPath}");
        connection.Open();

        await InitialiseSchemaAsync(connection, ct).ConfigureAwait(false);

        var eventsAppender = connection.CreateAppender("events");
        var stateAppender = connection.CreateAppender("slow_state");

        logger.LogDebug("DuckDbStorageWriter opened at {DbPath}", dbPath);
        return new DuckDbStorageWriter(connection, eventsAppender, stateAppender, logger);
    }

    private static async Task InitialiseSchemaAsync(DuckDBConnection connection, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await Task.Run(() =>
        {
            Execute(connection, SchemaV1.CreateEventsTable);
            Execute(connection, SchemaV1.CreateSlowStateTable);
            Execute(connection, SchemaV1.CreateSchemaMetaTable);

            // CreateIndexes contains multiple statements separated by newlines — execute each
            foreach (var stmt in SplitStatements(SchemaV1.CreateIndexes))
            {
                Execute(connection, stmt);
            }

            // Insert schema meta row only if it doesn't exist
            // Use a DateTime parameter for created_at to avoid TIMESTAMPTZ → TIMESTAMP_NS cast issues.
            using var insertCmd = connection.CreateCommand();
            insertCmd.CommandText = $"""
                INSERT INTO _schema_meta (schema_version, tracer_version, created_at)
                SELECT {SchemaV1.Version}, '{TracerVersion.Current}', $created_at
                WHERE NOT EXISTS (SELECT 1 FROM _schema_meta)
                """;
            insertCmd.Parameters.Add(new DuckDBParameter("created_at", DateTime.UtcNow));
            insertCmd.ExecuteNonQuery();
        }, ct).ConfigureAwait(false);
    }

    private static void Execute(DuckDBConnection connection, string sql)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static IEnumerable<string> SplitStatements(string sql) =>
        sql.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
           .Where(s => s.Length > 0);

    /// <inheritdoc/>
    public Task AppendEventAsync(EventRecord record, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        ct.ThrowIfCancellationRequested();

        lock (_lock)
        {
            WriteEventRow(record);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task AppendStateAsync(StateSampleRecord record, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        ct.ThrowIfCancellationRequested();

        if (record.Rate == StateSampleRate.Fast)
            throw new NotSupportedException("Fast-rate state samples are not supported by DuckDbStorageWriter.");

        lock (_lock)
        {
            WriteStateRow(record);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task AppendBatchAsync(IReadOnlyList<DiagnosticRecord> records, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(records);
        ct.ThrowIfCancellationRequested();

        lock (_lock)
        {
            foreach (var record in records)
            {
                ct.ThrowIfCancellationRequested();
                switch (record)
                {
                    case EventRecord ev:
                        WriteEventRow(ev);
                        break;
                    case StateSampleRecord ss when ss.Rate == StateSampleRate.Slow:
                        WriteStateRow(ss);
                        break;
                    case StateSampleRecord:
                        // silently skip fast-rate state samples
                        break;
                    default:
                        _logger.LogWarning("Unknown record type {Type} in batch; skipping.", record.GetType().Name);
                        break;
                }
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task FlushAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        lock (_lock)
        {
            _eventsAppender.Close();
            _stateAppender.Close();

            _eventsAppender = _connection.CreateAppender("events");
            _stateAppender = _connection.CreateAppender("slow_state");
        }

        _logger.LogDebug("DuckDbStorageWriter flushed.");
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
        }

        try
        {
            _eventsAppender.Close();
            _stateAppender.Close();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error closing appenders during dispose.");
        }

        _connection.Dispose();
        await Task.CompletedTask.ConfigureAwait(false);
    }

    // ── private write helpers ─────────────────────────────────────────────────

    private void WriteEventRow(EventRecord r)
    {
        var row = _eventsAppender.CreateRow();
        row.AppendValue((ulong?)r.EventId.Value);
        row.AppendValue((ulong?)r.TraceId.Value);
        if (r.ParentEventId.HasValue)
            row.AppendValue((ulong?)r.ParentEventId.Value.Value);
        else
            row.AppendNullValue();
        row.AppendValue((ulong?)r.SequenceNumber);
        row.AppendValue((DateTime?)Mapping.WallclockToDateTime(r.PublishWallclock));
        row.AppendValue((DateTime?)Mapping.WallclockToDateTime(r.ReceiveWallclock));
        row.AppendValue(r.PublisherNode.Value);
        row.AppendValue(r.SubscriberNode.Value);
        row.AppendValue(r.Topic.Value);
        if (r.EntityId.HasValue)
            row.AppendValue(r.EntityId.Value.Value);
        else
            row.AppendNullValue();
        if (r.OwningPlayerId is not null)
            row.AppendValue(r.OwningPlayerId);
        else
            row.AppendNullValue();
        if (r.ScenarioPhase is not null)
            row.AppendValue(r.ScenarioPhase);
        else
            row.AppendNullValue();
        if (r.Severity.HasValue)
            row.AppendValue(r.Severity.Value.ToString());
        else
            row.AppendNullValue();
        if (r.NotableLabel is not null)
            row.AppendValue(r.NotableLabel);
        else
            row.AppendNullValue();
        row.AppendValue(r.PayloadJson);
        row.EndRow();
    }

    private void WriteStateRow(StateSampleRecord r)
    {
        var row = _stateAppender.CreateRow();
        row.AppendValue((ulong?)r.SequenceNumber);
        row.AppendValue((DateTime?)Mapping.WallclockToDateTime(r.PublishWallclock));
        row.AppendValue((DateTime?)Mapping.WallclockToDateTime(r.ReceiveWallclock));
        row.AppendValue(r.PublisherNode.Value);
        row.AppendValue(r.SubscriberNode.Value);
        row.AppendValue(r.Topic.Value);
        row.AppendValue(r.InstanceKey);
        if (r.TraceId.HasValue)
            row.AppendValue((ulong?)r.TraceId.Value.Value);
        else
            row.AppendNullValue();
        row.AppendValue(r.PayloadJson);
        row.EndRow();
    }
}
