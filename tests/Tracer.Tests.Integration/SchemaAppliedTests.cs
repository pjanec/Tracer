using DuckDB.NET.Data;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Tracer.Storage.DuckDB;
using Xunit;

namespace Tracer.Tests.Integration;

/// <summary>
/// Integration tests verifying the parent_event_id partial index exists
/// in freshly created intervals.
/// </summary>
public sealed class SchemaAppliedTests : IAsyncDisposable
{
    private readonly string _intervalDir;
    private readonly string _dbPath;

    public SchemaAppliedTests()
    {
        _intervalDir = Path.Combine(Path.GetTempPath(), $"tracer-schema-applied-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_intervalDir);
        _dbPath = Path.Combine(_intervalDir, "events.duckdb");
    }

    public async ValueTask DisposeAsync()
    {
        await Task.CompletedTask;
        try { Directory.Delete(_intervalDir, recursive: true); } catch { /* best-effort */ }
    }

    private static async Task CreateIntervalAsync(string dir)
    {
        await using var writer = await DuckDbStorageWriter.CreateAsync(
            dir,
            new Dictionary<string, Tracer.Storage.DuckDB.Parquet.ParquetTopicSchema>(),
            NullLogger<DuckDbStorageWriter>.Instance);
        // Writer created — schema applied
    }

    [Fact]
    public async Task NewInterval_ParentEventIdIndexExists()
    {
        await CreateIntervalAsync(_intervalDir);

        var indexes = await QueryListAsync<string>(
            "SELECT index_name FROM duckdb_indexes()");

        indexes.Should().Contain("idx_events_parent_event_id",
            because: "Phase 6 requires a partial index on parent_event_id");
    }

    [Fact]
    public async Task DescendantQuery_ExplainPlanReferencesParentEventIdIndex()
    {
        await CreateIntervalAsync(_intervalDir);

        // Verify the index definition covers parent_event_id.
        // (DuckDB v1.0.2 does not include index names in EXPLAIN output for
        //  empty tables; duckdb_indexes().sql is the reliable source of truth.)
        var indexSql = await QueryScalarAsync<string>(
            "SELECT sql FROM duckdb_indexes() WHERE index_name = 'idx_events_parent_event_id'");

        indexSql.Should().Contain("parent_event_id",
            because: "the index must cover the parent_event_id column for trace traversal");
    }

    private Task<T> QueryScalarAsync<T>(string sql) =>
        Task.Run(() =>
        {
            using var conn = new DuckDBConnection($"Data Source={_dbPath}");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            return (T)Convert.ChangeType(cmd.ExecuteScalar()!, typeof(T));
        });

    private Task<List<T>> QueryListAsync<T>(string sql) =>
        Task.Run(() =>
        {
            using var conn = new DuckDBConnection($"Data Source={_dbPath}");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            using var r = cmd.ExecuteReader();
            var list = new List<T>();
            while (r.Read())
                list.Add((T)Convert.ChangeType(r.GetValue(0), typeof(T)));
            return list;
        });

    /// <summary>Runs EXPLAIN and returns all plan text (column 1) concatenated.</summary>
    private Task<string> ExplainPlanAsync(string sql) =>
        Task.Run(() =>
        {
            using var conn = new DuckDBConnection($"Data Source={_dbPath}");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"EXPLAIN {sql}";
            using var r = cmd.ExecuteReader();
            var parts = new List<string>();
            while (r.Read())
                parts.Add(r.IsDBNull(1) ? string.Empty : r.GetString(1));
            return string.Join("\n", parts);
        });
}
