using System.Data.Common;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Tracer.Storage.SavedQueries.Schema;

namespace Tracer.Storage.SavedQueries;

public sealed class SqliteSavedQueryStore : ISavedQueryStore, IDisposable
{
    private readonly string _dbPath;
    private readonly ILogger<SqliteSavedQueryStore> _logger;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private bool _disposed;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public SqliteSavedQueryStore(string dbPath, ILogger<SqliteSavedQueryStore> logger)
    {
        _dbPath = dbPath;
        _logger = logger;
        InitializeSync();
    }

    private void InitializeSync()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_dbPath)!);
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        var statements = SavedQueriesSchema.CreateSql
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var stmt in statements)
        {
            if (string.IsNullOrWhiteSpace(stmt)) continue;
            using var cmd = conn.CreateCommand();
            cmd.CommandText = stmt;
            cmd.ExecuteNonQuery();
        }
    }

    public async Task<IReadOnlyList<SavedQueryRecord>> ListAsync(SavedQueryFilter filter, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(filter);
        await using var conn = new SqliteConnection($"Data Source={_dbPath};Mode=ReadOnly");
        await conn.OpenAsync(ct);

        var (sql, parameters) = BuildSelectSql(filter);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (k, v) in parameters)
            cmd.Parameters.AddWithValue(k, v ?? (object)DBNull.Value);

        var results = new List<SavedQueryRecord>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            results.Add(MapRecord(reader));
        return results;
    }

    public async Task<SavedQueryRecord?> GetAsync(string savedQueryId, CancellationToken ct)
    {
        await using var conn = new SqliteConnection($"Data Source={_dbPath};Mode=ReadOnly");
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM saved_queries WHERE saved_query_id = $id";
        cmd.Parameters.AddWithValue("$id", savedQueryId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? MapRecord(reader) : null;
    }

    public async Task<SavedQueryRecord> CreateAsync(SavedQueryRecord record, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(record);
        var withDefaults = record with
        {
            SavedQueryId = string.IsNullOrEmpty(record.SavedQueryId)
                ? Ulid.NewUlid().ToString()
                : record.SavedQueryId,
            CreatedAtUtc = record.CreatedAtUtc == default ? DateTimeOffset.UtcNow : record.CreatedAtUtc,
        };

        await _writeLock.WaitAsync(ct);
        try
        {
            await using var conn = new SqliteConnection($"Data Source={_dbPath}");
            await conn.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO saved_queries (
                    saved_query_id, label, description, sql_text, parameters_json, tags_json,
                    is_built_in, is_favorite, author, created_at, last_run_at, run_count)
                VALUES (
                    $id, $label, $desc, $sql, $params, $tags,
                    $builtIn, $fav, $author, $createdAt, $lastRun, $runCount)
                """;
            BindRecord(cmd, withDefaults);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        finally { _writeLock.Release(); }
        return withDefaults;
    }

    public async Task<SavedQueryRecord?> UpdateAsync(SavedQueryRecord record, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(record);

        // Check if built-in
        var existing = await GetAsync(record.SavedQueryId, ct);
        if (existing is null) return null;
        if (existing.IsBuiltIn)
            throw new InvalidOperationException("Built-in queries are read-only; clone first");

        await _writeLock.WaitAsync(ct);
        try
        {
            await using var conn = new SqliteConnection($"Data Source={_dbPath}");
            await conn.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                UPDATE saved_queries SET
                    label = $label, description = $desc, sql_text = $sql,
                    parameters_json = $params, tags_json = $tags,
                    is_favorite = $fav, author = $author,
                    last_run_at = $lastRun, run_count = $runCount
                WHERE saved_query_id = $id
                """;
            BindRecord(cmd, record);
            var affected = await cmd.ExecuteNonQueryAsync(ct);
            return affected > 0 ? record : null;
        }
        finally { _writeLock.Release(); }
    }

    public async Task<bool> DeleteAsync(string savedQueryId, CancellationToken ct)
    {
        var existing = await GetAsync(savedQueryId, ct);
        if (existing is null) return false;
        if (existing.IsBuiltIn)
            throw new InvalidOperationException("Built-in queries are read-only; clone first");

        await _writeLock.WaitAsync(ct);
        try
        {
            await using var conn = new SqliteConnection($"Data Source={_dbPath}");
            await conn.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM saved_queries WHERE saved_query_id = $id";
            cmd.Parameters.AddWithValue("$id", savedQueryId);
            return await cmd.ExecuteNonQueryAsync(ct) > 0;
        }
        finally { _writeLock.Release(); }
    }

    public async Task IncrementRunCountAsync(string savedQueryId, CancellationToken ct)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            await using var conn = new SqliteConnection($"Data Source={_dbPath}");
            await conn.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                UPDATE saved_queries
                SET run_count = run_count + 1, last_run_at = $now
                WHERE saved_query_id = $id
                """;
            cmd.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
            cmd.Parameters.AddWithValue("$id", savedQueryId);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        finally { _writeLock.Release(); }
    }

    public async Task<SavedQueryRecord?> ToggleFavoriteAsync(string savedQueryId, CancellationToken ct)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            await using var conn = new SqliteConnection($"Data Source={_dbPath}");
            await conn.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                UPDATE saved_queries
                SET is_favorite = CASE WHEN is_favorite = 1 THEN 0 ELSE 1 END
                WHERE saved_query_id = $id
                """;
            cmd.Parameters.AddWithValue("$id", savedQueryId);
            var rows = await cmd.ExecuteNonQueryAsync(ct);
            if (rows == 0) return null;
        }
        finally { _writeLock.Release(); }

        return await GetAsync(savedQueryId, ct);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    internal static (string Sql, IReadOnlyList<(string, object?)> Parameters) BuildSelectSql(
        SavedQueryFilter filter)
    {
        var clauses = new List<string>();
        var ps = new List<(string, object?)>();

        if (filter.IsBuiltIn is { } bi) { clauses.Add("is_built_in = $builtIn"); ps.Add(("$builtIn", bi ? 1 : 0)); }
        if (filter.IsFavorite is { } fav) { clauses.Add("is_favorite = $fav"); ps.Add(("$fav", fav ? 1 : 0)); }
        if (filter.Author is not null) { clauses.Add("author = $author"); ps.Add(("$author", filter.Author)); }
        if (filter.Tag is not null) { clauses.Add("(',' || tags_json || ',') LIKE $tag"); ps.Add(("$tag", $"%\"{filter.Tag}\"%")); }

        var where = clauses.Count == 0 ? "" : "WHERE " + string.Join(" AND ", clauses);
        var sql = $"SELECT * FROM saved_queries {where} ORDER BY created_at DESC";
        return (sql, ps);
    }

    private static void BindRecord(SqliteCommand cmd, SavedQueryRecord r)
    {
        cmd.Parameters.AddWithValue("$id",        r.SavedQueryId);
        cmd.Parameters.AddWithValue("$label",     r.Label);
        cmd.Parameters.AddWithValue("$desc",      (object?)r.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$sql",       r.Sql);
        cmd.Parameters.AddWithValue("$params",    JsonSerializer.Serialize(r.Parameters, JsonOpts));
        cmd.Parameters.AddWithValue("$tags",      JsonSerializer.Serialize(r.Tags, JsonOpts));
        cmd.Parameters.AddWithValue("$builtIn",   r.IsBuiltIn ? 1 : 0);
        cmd.Parameters.AddWithValue("$fav",       r.IsFavorite ? 1 : 0);
        cmd.Parameters.AddWithValue("$author",    (object?)r.Author ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$createdAt", r.CreatedAtUtc.ToString("O"));
        cmd.Parameters.AddWithValue("$lastRun",   r.LastRunAtUtc?.ToString("O") ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$runCount",  r.RunCount);
    }

    private static SavedQueryRecord MapRecord(DbDataReader r)
    {
        var paramsJson = r.GetString(r.GetOrdinal("parameters_json"));
        var tagsJson   = r.GetString(r.GetOrdinal("tags_json"));

        var parameters = JsonSerializer.Deserialize<List<SavedQueryParameter>>(paramsJson, JsonOpts)
                         ?? new List<SavedQueryParameter>();
        var tags = JsonSerializer.Deserialize<List<string>>(tagsJson, JsonOpts)
                   ?? new List<string>();

        var lastRunOrd = r.GetOrdinal("last_run_at");
        DateTimeOffset? lastRun = r.IsDBNull(lastRunOrd)
            ? null
            : DateTimeOffset.Parse(r.GetString(lastRunOrd));

        return new SavedQueryRecord
        {
            SavedQueryId = r.GetString(r.GetOrdinal("saved_query_id")),
            Label        = r.GetString(r.GetOrdinal("label")),
            Description  = r.IsDBNull(r.GetOrdinal("description")) ? null : r.GetString(r.GetOrdinal("description")),
            Sql          = r.GetString(r.GetOrdinal("sql_text")),
            Parameters   = parameters,
            Tags         = tags,
            IsBuiltIn    = r.GetInt32(r.GetOrdinal("is_built_in")) != 0,
            IsFavorite   = r.GetInt32(r.GetOrdinal("is_favorite")) != 0,
            Author       = r.IsDBNull(r.GetOrdinal("author")) ? null : r.GetString(r.GetOrdinal("author")),
            CreatedAtUtc = DateTimeOffset.Parse(r.GetString(r.GetOrdinal("created_at"))),
            LastRunAtUtc = lastRun,
            RunCount     = r.GetInt32(r.GetOrdinal("run_count")),
        };
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _writeLock.Dispose();
    }
}
