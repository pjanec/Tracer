using Tracer.WebApi.Queries;

namespace Tracer.WebApi.Contracts.Dto;

// ── Request DTOs ──────────────────────────────────────────────────────────────

public sealed record SqlExecuteRequestDto
{
    public required string Sql { get; init; }
    public IReadOnlyDictionary<string, object?>? Parameters { get; init; }
    public int? TimeoutSeconds { get; init; }
    public int? MaxRows { get; init; }
}

public sealed record SqlExplainRequestDto
{
    public required string Sql { get; init; }
}

// ── Response DTOs ─────────────────────────────────────────────────────────────

public sealed record SqlExecuteResultDto
{
    public required string State { get; init; }
    public IReadOnlyList<SqlColumnInfoDto>? Columns { get; init; }
    public IReadOnlyList<IReadOnlyList<object?>>? Rows { get; init; }
    public string? ErrorMessage { get; init; }
    public required long ElapsedMs { get; init; }
    public required bool Truncated { get; init; }
}

public sealed record SqlColumnInfoDto
{
    public required string Name { get; init; }
    public required string DuckType { get; init; }
}

public sealed record SqlSchemaDto
{
    public required IReadOnlyList<SqlTableInfoDto> Tables { get; init; }
    public required DateTimeOffset RefreshedAtUtc { get; init; }
    public required IReadOnlyList<string> DialectNotes { get; init; }
}

public sealed record SqlTableInfoDto
{
    public required string Name { get; init; }
    public required IReadOnlyList<SqlColumnInfoDto> Columns { get; init; }
}

public sealed record SqlExplainResultDto
{
    public required string PlanText { get; init; }
}

public sealed record ViewSqlTemplateResultDto
{
    public required string Sql { get; init; }
    public required string Description { get; init; }
}

// ── Mapper ────────────────────────────────────────────────────────────────────

public static class SqlDtoMapper
{
    public static SqlExecuteResultDto MapResult(SqlExecutionResult r)
    {
        ArgumentNullException.ThrowIfNull(r);
        return new()
    {
        State        = r.State.ToString(),
        Columns      = r.Columns?.Select(c => new SqlColumnInfoDto { Name = c.Name, DuckType = c.DuckType }).ToList(),
        Rows         = r.Rows,
        ErrorMessage = r.ErrorMessage,
        ElapsedMs    = r.ElapsedMs,
        Truncated    = r.Truncated,
        };
    }

    public static SqlSchemaDto MapSchema(SqlSchemaSnapshot snap)
    {
        ArgumentNullException.ThrowIfNull(snap);
        return new()
    {
        Tables = snap.Tables.Select(t => new SqlTableInfoDto
        {
            Name    = t.Name,
            Columns = t.Columns.Select(c => new SqlColumnInfoDto { Name = c.Name, DuckType = c.DuckType }).ToList(),
        }).ToList(),
        RefreshedAtUtc = snap.RefreshedAtUtc,
        DialectNotes   = snap.DialectNotes,
        };
    }
}
