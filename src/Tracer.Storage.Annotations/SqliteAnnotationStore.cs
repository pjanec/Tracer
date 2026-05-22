using System.Data.Common;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Tracer.Storage.Annotations.Schema;

namespace Tracer.Storage.Annotations;

public sealed class SqliteAnnotationStore : IAnnotationStore
{
    private readonly string _dbPath;
    private readonly ILogger<SqliteAnnotationStore> _logger;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public SqliteAnnotationStore(string dbPath, ILogger<SqliteAnnotationStore> logger)
    {
        _dbPath = dbPath;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_dbPath)!);
        await using var conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync(ct);

        // Execute each statement individually because Microsoft.Data.Sqlite
        // executes only the first statement in a single command.
        var statements = AnnotationsSchema.CreateSql
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var stmt in statements)
        {
            if (string.IsNullOrWhiteSpace(stmt)) continue;
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = stmt;
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    public async Task<IReadOnlyList<AnnotationRecord>> ListAsync(AnnotationFilter filter, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(filter);
        await using var conn = new SqliteConnection($"Data Source={_dbPath};Mode=ReadOnly");
        await conn.OpenAsync(ct);

        var (sql, parameters) = BuildSelectSql(filter);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (k, v) in parameters)
            cmd.Parameters.AddWithValue(k, v ?? (object)DBNull.Value);

        var results = new List<AnnotationRecord>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            results.Add(MapRecord(reader));
        return results;
    }

    public async Task<AnnotationRecord?> GetAsync(string annotationId, CancellationToken ct)
    {
        await using var conn = new SqliteConnection($"Data Source={_dbPath};Mode=ReadOnly");
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM annotations WHERE annotation_id = $id";
        cmd.Parameters.AddWithValue("$id", annotationId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? MapRecord(reader) : null;
    }

    public async Task<AnnotationRecord> CreateAsync(AnnotationRecord record, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(record);
        var withDefaults = record with
        {
            AnnotationId = string.IsNullOrEmpty(record.AnnotationId)
                ? Ulid.NewUlid().ToString()
                : record.AnnotationId,
            CreatedAtUtc = record.CreatedAtUtc == default ? DateTimeOffset.UtcNow : record.CreatedAtUtc,
        };

        await _writeLock.WaitAsync(ct);
        try
        {
            await using var conn = new SqliteConnection($"Data Source={_dbPath}");
            await conn.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO annotations (
                    annotation_id, session_id, kind, event_id, entity_id, trace_id,
                    target_wallclock, body, title, tags_json, author, created_at, modified_at)
                VALUES (
                    $aid, $sid, $kind, $eid, $entid, $tid,
                    $tw, $body, $title, $tags, $author, $created, $modified);
                """;
            BindRecordParameters(cmd, withDefaults);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        finally { _writeLock.Release(); }
        return withDefaults;
    }

    public async Task<AnnotationRecord?> UpdateAsync(AnnotationRecord record, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(record);
        var modified = record with { ModifiedAtUtc = DateTimeOffset.UtcNow };
        await _writeLock.WaitAsync(ct);
        try
        {
            await using var conn = new SqliteConnection($"Data Source={_dbPath}");
            await conn.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                UPDATE annotations SET
                    session_id = $sid, kind = $kind, event_id = $eid, entity_id = $entid,
                    trace_id = $tid, target_wallclock = $tw,
                    body = $body, title = $title, tags_json = $tags,
                    author = $author, modified_at = $modified
                WHERE annotation_id = $aid;
                """;
            BindRecordParameters(cmd, modified);
            var affected = await cmd.ExecuteNonQueryAsync(ct);
            return affected > 0 ? modified : null;
        }
        finally { _writeLock.Release(); }
    }

    public async Task<bool> DeleteAsync(string annotationId, CancellationToken ct)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            await using var conn = new SqliteConnection($"Data Source={_dbPath}");
            await conn.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM annotations WHERE annotation_id = $id";
            cmd.Parameters.AddWithValue("$id", annotationId);
            return await cmd.ExecuteNonQueryAsync(ct) > 0;
        }
        finally { _writeLock.Release(); }
    }

    public async Task<IReadOnlyList<AnnotationRecord>> ExportAllForSessionAsync(
        string sessionId, CancellationToken ct)
    {
        return await ListAsync(new AnnotationFilter { SessionId = sessionId, Limit = 100_000 }, ct);
    }

    internal static (string Sql, IReadOnlyList<(string, object?)> Parameters) BuildSelectSql(
        AnnotationFilter filter)
    {
        var clauses = new List<string>();
        var ps = new List<(string, object?)>();
        if (filter.SessionId is not null)  { clauses.Add("session_id = $sid");  ps.Add(("$sid",   filter.SessionId)); }
        if (filter.Kind is { } k)          { clauses.Add("kind = $kind");        ps.Add(("$kind",  k.ToString())); }
        if (filter.EventId is not null)    { clauses.Add("event_id = $eid");     ps.Add(("$eid",   filter.EventId)); }
        if (filter.EntityId is not null)   { clauses.Add("entity_id = $entid");  ps.Add(("$entid", filter.EntityId)); }
        if (filter.TraceId is not null)    { clauses.Add("trace_id = $tid");     ps.Add(("$tid",   filter.TraceId)); }
        if (filter.FromUtc is { } from)    { clauses.Add("created_at >= $from"); ps.Add(("$from",  from.ToString("O"))); }
        if (filter.ToUtc is { } to)        { clauses.Add("created_at < $to");    ps.Add(("$to",    to.ToString("O"))); }

        var where = clauses.Count == 0 ? "" : "WHERE " + string.Join(" AND ", clauses);
        var sql = $"SELECT * FROM annotations {where} ORDER BY created_at DESC LIMIT $limit;";
        ps.Add(("$limit", (object?)filter.Limit));
        return (sql, ps);
    }

    private static void BindRecordParameters(SqliteCommand cmd, AnnotationRecord r)
    {
        cmd.Parameters.AddWithValue("$aid",     r.AnnotationId);
        cmd.Parameters.AddWithValue("$sid",     r.SessionId);
        cmd.Parameters.AddWithValue("$kind",    r.Kind.ToString());
        cmd.Parameters.AddWithValue("$eid",     (object?)r.EventId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$entid",   (object?)r.EntityId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$tid",     (object?)r.TraceId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$tw",      r.TargetWallclock?.ToString("O") ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$body",    r.Body);
        cmd.Parameters.AddWithValue("$title",   (object?)r.Title ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$tags",    JsonSerializer.Serialize(r.Tags));
        cmd.Parameters.AddWithValue("$author",  (object?)r.Author ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$created", r.CreatedAtUtc.ToString("O"));
        cmd.Parameters.AddWithValue("$modified", r.ModifiedAtUtc?.ToString("O") ?? (object)DBNull.Value);
    }

    private static AnnotationRecord MapRecord(DbDataReader r)
    {
        return new AnnotationRecord
        {
            AnnotationId    = r.GetString(r.GetOrdinal("annotation_id")),
            SessionId       = r.GetString(r.GetOrdinal("session_id")),
            Kind            = Enum.Parse<AnnotationKind>(r.GetString(r.GetOrdinal("kind"))),
            EventId         = r.IsDBNull(r.GetOrdinal("event_id"))          ? null : r.GetString(r.GetOrdinal("event_id")),
            EntityId        = r.IsDBNull(r.GetOrdinal("entity_id"))         ? null : r.GetString(r.GetOrdinal("entity_id")),
            TraceId         = r.IsDBNull(r.GetOrdinal("trace_id"))          ? null : r.GetString(r.GetOrdinal("trace_id")),
            TargetWallclock = r.IsDBNull(r.GetOrdinal("target_wallclock"))
                ? (DateTimeOffset?)null
                : DateTimeOffset.Parse(r.GetString(r.GetOrdinal("target_wallclock"))),
            Body     = r.GetString(r.GetOrdinal("body")),
            Title    = r.IsDBNull(r.GetOrdinal("title"))  ? null : r.GetString(r.GetOrdinal("title")),
            Tags     = JsonSerializer.Deserialize<List<string>>(r.GetString(r.GetOrdinal("tags_json"))) ?? new(),
            Author   = r.IsDBNull(r.GetOrdinal("author")) ? null : r.GetString(r.GetOrdinal("author")),
            CreatedAtUtc  = DateTimeOffset.Parse(r.GetString(r.GetOrdinal("created_at"))),
            ModifiedAtUtc = r.IsDBNull(r.GetOrdinal("modified_at"))
                ? (DateTimeOffset?)null
                : DateTimeOffset.Parse(r.GetString(r.GetOrdinal("modified_at"))),
        };
    }
}
