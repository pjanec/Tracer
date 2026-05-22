using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Tracer.Storage.SavedViews.Schema;

namespace Tracer.Storage.SavedViews;

public sealed class SqliteSavedViewStore : ISavedViewStore
{
    private readonly string _dbPath;
    private readonly ILogger<SqliteSavedViewStore> _logger;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public SqliteSavedViewStore(string dbPath, ILogger<SqliteSavedViewStore> logger)
    {
        _dbPath = dbPath;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_dbPath)!);
        await using var conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync(ct);

        var statements = SavedViewsSchema.CreateSql
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var stmt in statements)
        {
            if (string.IsNullOrWhiteSpace(stmt)) continue;
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = stmt;
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    public async Task<IReadOnlyList<SavedViewRecord>> ListAsync(SavedViewFilter filter, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(filter);
        await using var conn = new SqliteConnection($"Data Source={_dbPath};Mode=ReadOnly");
        await conn.OpenAsync(ct);

        var (sql, parameters) = BuildSelectSql(filter);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (k, v) in parameters)
            cmd.Parameters.AddWithValue(k, v ?? (object)DBNull.Value);

        var results = new List<SavedViewRecord>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            results.Add(MapRecord(reader));
        return results;
    }

    public async Task<SavedViewRecord?> GetAsync(string savedViewId, CancellationToken ct)
    {
        await using var conn = new SqliteConnection($"Data Source={_dbPath};Mode=ReadOnly");
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM saved_views WHERE saved_view_id = $id";
        cmd.Parameters.AddWithValue("$id", savedViewId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? MapRecord(reader) : null;
    }

    public async Task<SavedViewRecord> CreateAsync(SavedViewRecord record, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(record);
        var withDefaults = record with
        {
            SavedViewId = string.IsNullOrEmpty(record.SavedViewId)
                ? Ulid.NewUlid().ToString()
                : record.SavedViewId,
            CreatedAtUtc = record.CreatedAtUtc == default ? DateTimeOffset.UtcNow : record.CreatedAtUtc,
        };

        await _writeLock.WaitAsync(ct);
        try
        {
            await using var conn = new SqliteConnection($"Data Source={_dbPath}");
            await conn.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO saved_views (
                    saved_view_id, session_id, kind, view_type, url, label, description,
                    persona, author, created_at, last_opened_at, open_count)
                VALUES (
                    $id, $sid, $kind, $vtype, $url, $label, $desc,
                    $persona, $author, $created, $lastOpened, $openCount)
                """;
            BindRecordParameters(cmd, withDefaults);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        finally { _writeLock.Release(); }
        return withDefaults;
    }

    public async Task<SavedViewRecord?> UpdateAsync(SavedViewRecord record, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(record);
        await _writeLock.WaitAsync(ct);
        try
        {
            await using var conn = new SqliteConnection($"Data Source={_dbPath}");
            await conn.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                UPDATE saved_views SET
                    session_id = $sid, kind = $kind, view_type = $vtype, url = $url,
                    label = $label, description = $desc, persona = $persona,
                    author = $author, last_opened_at = $lastOpened, open_count = $openCount
                WHERE saved_view_id = $id
                """;
            BindRecordParameters(cmd, record);
            var affected = await cmd.ExecuteNonQueryAsync(ct);
            return affected > 0 ? record : null;
        }
        finally { _writeLock.Release(); }
    }

    public async Task<bool> DeleteAsync(string savedViewId, CancellationToken ct)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            await using var conn = new SqliteConnection($"Data Source={_dbPath}");
            await conn.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM saved_views WHERE saved_view_id = $id";
            cmd.Parameters.AddWithValue("$id", savedViewId);
            return await cmd.ExecuteNonQueryAsync(ct) > 0;
        }
        finally { _writeLock.Release(); }
    }

    public async Task RecordOpenedAsync(string savedViewId, CancellationToken ct)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            await using var conn = new SqliteConnection($"Data Source={_dbPath}");
            await conn.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                UPDATE saved_views
                SET open_count = open_count + 1, last_opened_at = $now
                WHERE saved_view_id = $id
                """;
            cmd.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
            cmd.Parameters.AddWithValue("$id", savedViewId);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        finally { _writeLock.Release(); }
    }

    internal static (string Sql, IReadOnlyList<(string, object?)> Parameters) BuildSelectSql(
        SavedViewFilter filter)
    {
        var clauses = new List<string>();
        var ps = new List<(string, object?)>();
        if (filter.SessionId is not null) { clauses.Add("session_id = $sid");   ps.Add(("$sid",     filter.SessionId)); }
        if (filter.Kind is { } k)         { clauses.Add("kind = $kind");         ps.Add(("$kind",    k.ToString())); }
        if (filter.ViewType is not null)  { clauses.Add("view_type = $vtype");   ps.Add(("$vtype",   filter.ViewType)); }
        if (filter.Persona is not null)   { clauses.Add("persona = $persona");   ps.Add(("$persona", filter.Persona)); }

        var where = clauses.Count == 0 ? "" : "WHERE " + string.Join(" AND ", clauses);

        var orderBy = filter.OrderBy == "recent"
            ? "last_opened_at DESC NULLS LAST, created_at DESC"
            : "created_at DESC";

        var limit = Math.Min(filter.Limit, 500);
        var sql = $"SELECT * FROM saved_views {where} ORDER BY {orderBy} LIMIT $limit";
        ps.Add(("$limit", (object?)limit));
        return (sql, ps);
    }

    private static void BindRecordParameters(SqliteCommand cmd, SavedViewRecord r)
    {
        cmd.Parameters.AddWithValue("$id",          r.SavedViewId);
        cmd.Parameters.AddWithValue("$sid",         r.SessionId);
        cmd.Parameters.AddWithValue("$kind",        r.Kind.ToString());
        cmd.Parameters.AddWithValue("$vtype",       r.ViewType);
        cmd.Parameters.AddWithValue("$url",         r.Url);
        cmd.Parameters.AddWithValue("$label",       r.Label);
        cmd.Parameters.AddWithValue("$desc",        (object?)r.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$persona",     r.Persona);
        cmd.Parameters.AddWithValue("$author",      (object?)r.Author ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$created",     r.CreatedAtUtc.ToString("O"));
        cmd.Parameters.AddWithValue("$lastOpened",  r.LastOpenedAtUtc?.ToString("O") ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$openCount",   r.OpenCount);
    }

    private static SavedViewRecord MapRecord(DbDataReader r)
    {
        return new SavedViewRecord
        {
            SavedViewId    = r.GetString(r.GetOrdinal("saved_view_id")),
            SessionId      = r.GetString(r.GetOrdinal("session_id")),
            Kind           = Enum.Parse<SavedViewKind>(r.GetString(r.GetOrdinal("kind"))),
            ViewType       = r.GetString(r.GetOrdinal("view_type")),
            Url            = r.GetString(r.GetOrdinal("url")),
            Label          = r.GetString(r.GetOrdinal("label")),
            Description    = r.IsDBNull(r.GetOrdinal("description")) ? null : r.GetString(r.GetOrdinal("description")),
            Persona        = r.GetString(r.GetOrdinal("persona")),
            Author         = r.IsDBNull(r.GetOrdinal("author")) ? null : r.GetString(r.GetOrdinal("author")),
            CreatedAtUtc   = DateTimeOffset.Parse(r.GetString(r.GetOrdinal("created_at"))),
            LastOpenedAtUtc = r.IsDBNull(r.GetOrdinal("last_opened_at"))
                ? (DateTimeOffset?)null
                : DateTimeOffset.Parse(r.GetString(r.GetOrdinal("last_opened_at"))),
            OpenCount      = r.GetInt32(r.GetOrdinal("open_count")),
        };
    }
}
