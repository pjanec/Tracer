using System.Diagnostics;
using DuckDB.NET.Data;
using Microsoft.Extensions.Logging;
using Tracer.Storage.DuckDB.MultiInterval;

namespace Tracer.WebApi.Queries;

public sealed class SqlExecutorService
{
    private readonly LiveMultiIntervalReader _reader;
    private readonly SqlExecutorConfig _config;
    private readonly ILogger<SqlExecutorService> _logger;

    public SqlExecutorService(
        LiveMultiIntervalReader reader,
        SqlExecutorConfig config,
        ILogger<SqlExecutorService> logger)
    {
        _reader = reader;
        _config = config;
        _logger = logger;
    }

    public async Task<SqlExecutionResult> ExecuteAsync(SqlExecutionRequest request, CancellationToken outerCt)
    {
        ArgumentNullException.ThrowIfNull(request);

        // 1. Validate
        var validation = SqlGuardrails.Validate(request.Sql);
        if (!validation.IsValid)
            return SqlExecutionResult.Rejected(validation.RejectionReason ?? "Invalid query");

        // 2. Inject row limit if absent
        var maxRows = request.MaxRows ?? _config.DefaultMaxRows;
        var timeoutSeconds = request.TimeoutSeconds ?? _config.DefaultTimeoutSeconds;
        var sqlToExecute = EnsureLimit(request.Sql, maxRows);

        // 3. Execute with timeout
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(outerCt, timeoutCts.Token);

        var sw = Stopwatch.StartNew();
        PooledMultiIntervalConnection? pooled = null;
        try
        {
            pooled = await _reader.AcquireAsync(linkedCts.Token);

            // Apply memory limit for this query
            SetMemoryLimit(pooled.Connection, _config.MaxMemoryMb);

            var columns = new List<SqlColumnInfo>();
            var rows = new List<IReadOnlyList<object?>>();

            // Run synchronous DuckDB in a Task.Run so we can handle timeout
            var runTask = Task.Run(() =>
            {
                using var cmd = pooled.Connection.CreateCommand();
                cmd.CommandText = sqlToExecute;

                if (request.Parameters is not null)
                {
                    foreach (var (name, value) in request.Parameters)
                    {
                        var paramName = name.StartsWith('$') ? name : $"${name}";
                        cmd.Parameters.Add(new DuckDBParameter(paramName, value ?? DBNull.Value));
                    }
                }

                using var reader = cmd.ExecuteReader();
                for (int i = 0; i < reader.FieldCount; i++)
                    columns.Add(new SqlColumnInfo(reader.GetName(i), reader.GetDataTypeName(i)));

                while (reader.Read())
                {
                    var row = new object?[reader.FieldCount];
                    for (int j = 0; j < reader.FieldCount; j++)
                        row[j] = reader.IsDBNull(j) ? null : reader.GetValue(j);
                    rows.Add(row);
                }
            }, linkedCts.Token);

            await runTask;

            sw.Stop();
            return new SqlExecutionResult
            {
                State    = SqlExecutionState.Succeeded,
                Columns  = columns,
                Rows     = rows,
                ElapsedMs = sw.ElapsedMilliseconds,
                Truncated = false,
            };
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            sw.Stop();
            return new SqlExecutionResult
            {
                State        = SqlExecutionState.Timeout,
                ErrorMessage = $"Query exceeded the {timeoutSeconds}-second budget",
                ElapsedMs    = sw.ElapsedMilliseconds,
            };
        }
        catch (DuckDBException dex)
        {
            sw.Stop();
            _logger.LogDebug(dex, "DuckDB error executing user SQL");
            return new SqlExecutionResult
            {
                State        = SqlExecutionState.Failed,
                ErrorMessage = dex.Message,
                ElapsedMs    = sw.ElapsedMilliseconds,
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            sw.Stop();
            _logger.LogWarning(ex, "Unexpected error executing user SQL");
            return new SqlExecutionResult
            {
                State        = SqlExecutionState.Failed,
                ErrorMessage = ex.Message,
                ElapsedMs    = sw.ElapsedMilliseconds,
            };
        }
        finally
        {
            if (pooled is not null)
                await pooled.DisposeAsync();
        }
    }

    public async Task<SqlExplainResult> ExplainAsync(string sql, CancellationToken ct)
    {
        var validation = SqlGuardrails.Validate(sql);
        if (!validation.IsValid)
            return new SqlExplainResult { Failed = true, ErrorMessage = validation.RejectionReason };

        await using var pooled = await _reader.AcquireAsync(ct);
        var sb = new System.Text.StringBuilder();

        try
        {
            await Task.Run(() =>
            {
                using var cmd = pooled.Connection.CreateCommand();
                cmd.CommandText = $"EXPLAIN {sql}";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    for (int i = 0; i < reader.FieldCount; i++)
                        sb.AppendLine(reader.GetValue(i)?.ToString() ?? "");
                }
            }, ct);
        }
        catch (DuckDBException dex)
        {
            return new SqlExplainResult { Failed = true, ErrorMessage = dex.Message };
        }

        return new SqlExplainResult { Failed = false, PlanText = sb.ToString() };
    }

    private static string EnsureLimit(string sql, int maxRows)
    {
        var trimmed = sql.TrimEnd().TrimEnd(';').TrimEnd();
        if (trimmed.Contains("LIMIT", StringComparison.OrdinalIgnoreCase))
            return sql;
        return $"{trimmed} LIMIT {maxRows}";
    }

    private static void SetMemoryLimit(DuckDBConnection conn, int memoryMb)
    {
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"PRAGMA memory_limit='{memoryMb}MB'";
            cmd.ExecuteNonQuery();
        }
        catch (Exception)
        {
            // Best effort — not fatal
        }
    }
}

public sealed record SqlExecutorConfig
{
    public int DefaultTimeoutSeconds { get; init; } = 30;
    public int DefaultMaxRows { get; init; } = 100_000;
    public int MaxMemoryMb { get; init; } = 1024;
}

public sealed record SqlExecutionRequest
{
    public required string Sql { get; init; }
    public IReadOnlyDictionary<string, object?>? Parameters { get; init; }
    public int? TimeoutSeconds { get; init; }
    public int? MaxRows { get; init; }
}

public enum SqlExecutionState { Succeeded, Failed, Timeout, Rejected }

public sealed record SqlExecutionResult
{
    public required SqlExecutionState State { get; init; }
    public IReadOnlyList<SqlColumnInfo>? Columns { get; init; }
    public IReadOnlyList<IReadOnlyList<object?>>? Rows { get; init; }
    public string? ErrorMessage { get; init; }
    public long ElapsedMs { get; init; }
    public bool Truncated { get; init; }

    public static SqlExecutionResult Rejected(string reason) => new()
    {
        State = SqlExecutionState.Rejected,
        ErrorMessage = reason,
    };
}

public sealed record SqlColumnInfo(string Name, string DuckType);

public sealed record SqlExplainResult
{
    public required bool Failed { get; init; }
    public string? PlanText { get; init; }
    public string? ErrorMessage { get; init; }
}
