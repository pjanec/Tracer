using DuckDB.NET.Data;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Tracer.Storage.DuckDB;
using Tracer.Storage.DuckDB.Parquet;
using Xunit;

namespace Tracer.Tests.Unit.Storage;

public sealed class SchemaTests : IAsyncDisposable
{
    private readonly string _intervalDir;
    private readonly string _dbPath;

    public SchemaTests()
    {
        _intervalDir = Path.Combine(Path.GetTempPath(), $"tracer-schema-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_intervalDir);
        _dbPath = Path.Combine(_intervalDir, "events.duckdb");
    }

    public async ValueTask DisposeAsync()
    {
        await Task.CompletedTask;
        try { Directory.Delete(_intervalDir, recursive: true); } catch { /* best-effort */ }
    }

    private static Task<DuckDbStorageWriter> CreateWriterAsync(string dir) =>
        DuckDbStorageWriter.CreateAsync(
            dir,
            new Dictionary<string, ParquetTopicSchema>(),
            NullLogger<DuckDbStorageWriter>.Instance);

    [Fact]
    public async Task CreateAsync_FreshDatabase_WritesSchemaMetaRow()
    {
        await using (await CreateWriterAsync(_intervalDir))
        { /* schema initialised */ }

        long count = await QueryScalarAsync<long>(
            "SELECT COUNT(*) FROM _schema_meta");
        count.Should().Be(1);
    }

    [Fact]
    public async Task CreateAsync_ExistingDatabase_IsIdempotent()
    {
        // First open — creates schema
        await using (await CreateWriterAsync(_intervalDir))
        { }

        // Second open — schema must not duplicate the meta row
        await using (await CreateWriterAsync(_intervalDir))
        { }

        long count = await QueryScalarAsync<long>(
            "SELECT COUNT(*) FROM _schema_meta");
        count.Should().Be(1, "meta row should not be duplicated on re-open");
    }

    [Fact]
    public async Task SchemaV1_Version_IsOne()
    {
        await using (await CreateWriterAsync(_intervalDir))
        { }

        int version = await QueryScalarAsync<int>(
            "SELECT schema_version FROM _schema_meta LIMIT 1");
        version.Should().Be(1);
    }

    [Fact]
    public async Task AllIndexes_AreCreated()
    {
        await using (await CreateWriterAsync(_intervalDir))
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
