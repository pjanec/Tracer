using DuckDB.NET.Data;
using Microsoft.Extensions.Logging;
using Tracer.Core.Abstractions;
using Tracer.Core.Queries;
using Tracer.Core.Records;
using Tracer.Storage.DuckDB.Internal;
using Tracer.Storage.DuckDB.Queries;
using TracerEventId = Tracer.Core.Identity.EventId;

namespace Tracer.Storage.DuckDB;

/// <summary>
/// DuckDB-backed implementation of <see cref="IDiagnosticStorageReader"/>.
/// Opens the database in READ_ONLY mode.
/// </summary>
public sealed class DuckDbStorageReader : IDiagnosticStorageReader
{
    private readonly DuckDBConnection _connection;
    private readonly ILogger<DuckDbStorageReader> _logger;
    private bool _disposed;

    private DuckDbStorageReader(DuckDBConnection connection, ILogger<DuckDbStorageReader> logger)
    {
        _connection = connection;
        _logger = logger;
    }

    /// <summary>
    /// Opens a DuckDB database at <paramref name="dbPath"/> for reading.
    /// </summary>
    public static async Task<DuckDbStorageReader> OpenAsync(
        string dbPath,
        ILogger<DuckDbStorageReader> logger,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dbPath);
        ArgumentNullException.ThrowIfNull(logger);
        ct.ThrowIfCancellationRequested();

        var connection = new DuckDBConnection($"Data Source={dbPath};ACCESS_MODE=READ_ONLY");
        await Task.Run(() => connection.Open(), ct).ConfigureAwait(false);

        logger.LogDebug("DuckDbStorageReader opened at {DbPath}", dbPath);
        return new DuckDbStorageReader(connection, logger);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<EventRecord>> QueryEventsAsync(
        EventQuery query,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var (sql, parameters) = EventQueryBuilder.Build(query);

        return await Task.Run(() =>
        {
            var results = new List<EventRecord>();
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = sql;
            foreach (var p in parameters)
                cmd.Parameters.Add(p);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                ct.ThrowIfCancellationRequested();
                results.Add(Mapping.MapEventRecord(reader));
            }
            return (IReadOnlyList<EventRecord>)results;
        }, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<EventRecord?> GetEventAsync(TracerEventId eventId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        return await Task.Run(() =>
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT * FROM events WHERE event_id = $eid LIMIT 1";
            cmd.Parameters.Add(new DuckDBParameter("eid", eventId.Value));
            using var reader = cmd.ExecuteReader();
            return reader.Read() ? Mapping.MapEventRecord(reader) : null;
        }, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<long> CountEventsAsync(EventFilter filter, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ct.ThrowIfCancellationRequested();

        var (sql, parameters) = EventQueryBuilder.BuildCount(filter);

        return await Task.Run(() =>
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = sql;
            foreach (var p in parameters)
                cmd.Parameters.Add(p);
            var result = cmd.ExecuteScalar();
            return Convert.ToInt64(result);
        }, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Counts the number of rows in the <c>slow_state</c> table.
    /// Returns 0 if the table does not exist or cannot be read.
    /// </summary>
    public async Task<long> CountSlowStateAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        return await Task.Run(() =>
        {
            try
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM slow_state";
                var result = cmd.ExecuteScalar();
                return Convert.ToInt64(result);
            }
            catch
            {
                return 0L;
            }
        }, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        _connection.Dispose();
        await Task.CompletedTask.ConfigureAwait(false);
        _logger.LogDebug("DuckDbStorageReader disposed.");
    }
}
