using DuckDB.NET.Data;

namespace Tracer.Storage.DuckDB.MultiInterval;

/// <summary>
/// Queries across multiple DuckDB interval files by attaching them to a single in-memory connection.
/// </summary>
public sealed class MultiIntervalReader : IAsyncDisposable
{
    private readonly DuckDBConnection _connection;
    private readonly AttachedDatabaseManager _manager;

    private MultiIntervalReader(DuckDBConnection connection, AttachedDatabaseManager manager)
    {
        _connection = connection;
        _manager = manager;
    }

    /// <summary>Live attachments from the underlying <see cref="AttachedDatabaseManager"/>.</summary>
    public IReadOnlyDictionary<string, string> Attachments => _manager.Attachments;

    /// <summary>Internal access to the raw connection for tests.</summary>
    internal DuckDBConnection Connection => _connection;

    /// <summary>
    /// Opens an in-memory DuckDB connection, attaches all provided files, and returns
    /// a ready <see cref="MultiIntervalReader"/>.
    /// </summary>
    public static async Task<MultiIntervalReader> CreateAsync(
        IEnumerable<IntervalDbFile> files,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(files);

        var connection = new DuckDBConnection("DataSource=:memory:");
        await connection.OpenAsync(ct);

        var manager = new AttachedDatabaseManager(connection);
        foreach (var file in files)
            await manager.AttachAsync(file, ct);

        return new MultiIntervalReader(connection, manager);
    }

    /// <summary>
    /// Builds a UNION ALL SQL string selecting all columns plus a <c>__source_alias</c>
    /// literal from each attached database's <c>events</c> table.
    /// Returns the sentinel <c>"SELECT NULL WHERE FALSE"</c> when no files are attached.
    /// </summary>
    public string BuildEventsUnionSql()
    {
        if (_manager.Attachments.Count == 0)
            return "SELECT NULL WHERE FALSE";

        var parts = _manager.Attachments.Keys.Select(alias =>
            $"SELECT *, '{alias}' AS __source_alias FROM {alias}.events");

        return string.Join("\nUNION ALL\n", parts);
    }

    /// <summary>
    /// Builds a UNION ALL SQL string over each attached database's <c>slow_state</c> table.
    /// Returns the sentinel <c>"SELECT NULL WHERE FALSE"</c> when no files are attached.
    /// </summary>
    public string BuildSlowStateUnionSql(string whereClause = "", string orderByClause = "", int? limit = null)
    {
        if (_manager.Attachments.Count == 0)
            return "SELECT NULL WHERE FALSE";

        var parts = _manager.Attachments.Keys.Select(alias =>
            $"SELECT *, '{alias}' AS __source_alias FROM {alias}.slow_state {whereClause}");

        var sql = string.Join("\nUNION ALL\n", parts);
        if (!string.IsNullOrEmpty(orderByClause)) sql += "\n" + orderByClause;
        if (limit.HasValue) sql += $"\nLIMIT {limit.Value}";
        return sql;
    }

    public async ValueTask DisposeAsync()
    {
        await _manager.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
