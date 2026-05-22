using Tracer.Storage.DuckDB.MultiInterval;

namespace Tracer.WebApi.Queries;

/// <summary>
/// Introspects the attached DuckDB intervals and exposes a cached schema snapshot
/// for SQL Console autocomplete. Cache is invalidated when <see cref="IntervalSetTracker.SetChanged"/> fires.
/// </summary>
public sealed class SqlSchemaService
{
    private readonly LiveMultiIntervalReader _reader;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private volatile SqlSchemaSnapshot? _cache;

    public SqlSchemaService(LiveMultiIntervalReader reader)
    {
        _reader = reader;
    }

    public async Task<SqlSchemaSnapshot> GetAsync(CancellationToken ct = default)
    {
        if (_cache is not null) return _cache;

        await _refreshLock.WaitAsync(ct);
        try
        {
            if (_cache is not null) return _cache;
            _cache = await BuildAsync(ct);
            return _cache;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    public async Task InvalidateAsync()
    {
        await _refreshLock.WaitAsync();
        try
        {
            _cache = null;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private async Task<SqlSchemaSnapshot> BuildAsync(CancellationToken ct)
    {
        await using var conn = await _reader.AcquireAsync(ct);

        // Get the list of attached database aliases via PRAGMA database_list
        var aliases = new List<string>();
        await Task.Run(() =>
        {
            using var cmd = conn.Connection.CreateCommand();
            cmd.CommandText = "PRAGMA database_list";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var name = r.GetString(1); // "name" column
                if (name != "memory" && name != "main" && !string.IsNullOrEmpty(name))
                    aliases.Add(name);
            }
        }, ct);

        if (aliases.Count == 0)
        {
            return new SqlSchemaSnapshot
            {
                Tables = Array.Empty<SqlTableInfo>(),
                RefreshedAtUtc = DateTimeOffset.UtcNow,
                DialectNotes = BuildDialectNotes(),
            };
        }

        var firstAlias = aliases[0];
        var tableNames = new List<string>();
        await Task.Run(() =>
        {
            using var cmd = conn.Connection.CreateCommand();
            cmd.CommandText = $"""
                SELECT table_name
                FROM {firstAlias}.information_schema.tables
                WHERE table_schema = 'main'
                ORDER BY table_name
                """;
            using var r = cmd.ExecuteReader();
            while (r.Read())
                tableNames.Add(r.GetString(0));
        }, ct);

        var tables = new List<SqlTableInfo>();
        foreach (var tableName in tableNames)
        {
            var columns = new List<SqlColumnInfo>();
            await Task.Run(() =>
            {
                using var cmd = conn.Connection.CreateCommand();
                cmd.CommandText = $"DESCRIBE {firstAlias}.{tableName}";
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    columns.Add(new SqlColumnInfo(r.GetString(0), r.GetString(1)));
            }, ct);
            tables.Add(new SqlTableInfo(tableName, columns));
        }

        return new SqlSchemaSnapshot
        {
            Tables = tables,
            RefreshedAtUtc = DateTimeOffset.UtcNow,
            DialectNotes = BuildDialectNotes(),
        };
    }

    private static IReadOnlyList<string> BuildDialectNotes() => new[]
    {
        "Use `events`, `slow_state` as table names (exposed as views over interval storage)",
        "Functions: time_bucket, approx_quantile, json_extract_string, list_aggregate",
        "Use APPROX_QUANTILE for fast percentile estimates on large data",
        "Use time_bucket(INTERVAL '5 seconds', publish_wallclock) for time-series grouping",
    };
}

public sealed record SqlSchemaSnapshot
{
    public required IReadOnlyList<SqlTableInfo> Tables { get; init; }
    public required DateTimeOffset RefreshedAtUtc { get; init; }
    public IReadOnlyList<string> DialectNotes { get; init; } = Array.Empty<string>();
}

public sealed record SqlTableInfo(string Name, IReadOnlyList<SqlColumnInfo> Columns);
