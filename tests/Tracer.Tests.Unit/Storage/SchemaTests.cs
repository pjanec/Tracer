using DuckDB.NET.Data;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Tracer.Storage.DuckDB;
using Xunit;

namespace Tracer.Tests.Unit.Storage;

public sealed class SchemaTests : IAsyncDisposable
{
    private readonly string _dbPath;

    public SchemaTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".db");
    }

    public async ValueTask DisposeAsync()
    {
        await Task.CompletedTask;
        try { File.Delete(_dbPath); } catch { /* best-effort */ }
    }

    [Fact]
    public async Task CreateAsync_FreshDatabase_WritesSchemaMetaRow()
    {
        await using (await DuckDbStorageWriter.CreateAsync(
            _dbPath, NullLogger<DuckDbStorageWriter>.Instance))
        { /* schema initialised */ }

        long count = await QueryScalarAsync<long>(
            "SELECT COUNT(*) FROM _schema_meta");
        count.Should().Be(1);
    }

    [Fact]
    public async Task CreateAsync_ExistingDatabase_IsIdempotent()
    {
        // First open — creates schema
        await using (await DuckDbStorageWriter.CreateAsync(
            _dbPath, NullLogger<DuckDbStorageWriter>.Instance))
        { }

        // Second open — schema must not duplicate the meta row
        await using (await DuckDbStorageWriter.CreateAsync(
            _dbPath, NullLogger<DuckDbStorageWriter>.Instance))
        { }

        long count = await QueryScalarAsync<long>(
            "SELECT COUNT(*) FROM _schema_meta");
        count.Should().Be(1, "meta row should not be duplicated on re-open");
    }

    [Fact]
    public async Task SchemaV1_Version_IsOne()
    {
        await using (await DuckDbStorageWriter.CreateAsync(
            _dbPath, NullLogger<DuckDbStorageWriter>.Instance))
        { }

        int version = await QueryScalarAsync<int>(
            "SELECT schema_version FROM _schema_meta LIMIT 1");
        version.Should().Be(1);
    }

    [Fact]
    public async Task AllIndexes_AreCreated()
    {
        await using (await DuckDbStorageWriter.CreateAsync(
            _dbPath, NullLogger<DuckDbStorageWriter>.Instance))
        { }

        var indexes = await QueryListAsync<string>(
            "SELECT index_name FROM duckdb_indexes()");

        string[] expected =
        [
            "idx_events_trace",
            "idx_events_parent",
            "idx_events_entity",
            "idx_events_player",
            "idx_events_topic_time",
            "idx_state_instance_time",
            "idx_state_topic"
        ];

        foreach (var name in expected)
            indexes.Should().Contain(name, $"index '{name}' should be created by SchemaV1");
    }

    // ── helpers ───────────────────────────────────────────────────────────────

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
}
