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
            "idx_events_parent_event_id",
            "idx_events_entity",
            "idx_events_player",
            "idx_events_topic_time",
            "idx_state_instance_time",
            "idx_state_topic"
        ];

        foreach (var name in expected)
            indexes.Should().Contain(name, $"index '{name}' should be created by SchemaV1");
    }

    [Fact]
    public async Task AllIndexes_AreCreated_IncludesSlowStateEntityTimeIndex()
    {
        await using (await CreateWriterAsync(_intervalDir))
        { }

        var indexes = await QueryListAsync<string>(
            "SELECT index_name FROM duckdb_indexes() WHERE table_name = 'slow_state' AND index_name = 'idx_slow_state_entity_time'");

        indexes.Should().ContainSingle(
            "idx_slow_state_entity_time should appear exactly once in duckdb_indexes() for slow_state");
    }

    [Fact]
    public async Task CreateIndexes_IsIdempotent_SlowStateIndex()
    {
        // Run CreateAsync (which calls CreateIndexes) twice
        await using (await CreateWriterAsync(_intervalDir))
        { }
        // Second writer open on the same directory — must not throw
        await using (await CreateWriterAsync(_intervalDir))
        { }

        var indexes = await QueryListAsync<string>(
            "SELECT index_name FROM duckdb_indexes() WHERE index_name = 'idx_slow_state_entity_time'");

        indexes.Should().ContainSingle("idx_slow_state_entity_time should exist exactly once after two CreateAsync calls");
    }

    [Fact]
    public void SchemaV1_CreateIndexes_ContainsPhase7CommentBlock()
    {
        Tracer.Storage.DuckDB.Schema.SchemaV1.CreateIndexes
            .Should().Contain("-- Phase 7");
    }

    [Fact]
    public async Task SlowStateEntityQuery_WithIndex_CompletesUnder200ms()
    {
        // Write 50,000 slow-state rows for 10 distinct entity IDs
        const int rowsPerEntity = 5000;
        const int entityCount = 10;

        await using (var writer = await CreateWriterAsync(_intervalDir))
        {
            for (int e = 0; e < entityCount; e++)
            {
                for (int r = 0; r < rowsPerEntity; r++)
                {
                    var record = new Tracer.Core.Records.StateSampleRecord
                    {
                        SequenceNumber = (ulong)(e * rowsPerEntity + r),
                        PublishWallclock = Tracer.Core.Time.WallclockTime.Zero + TimeSpan.FromSeconds(r),
                        ReceiveWallclock = Tracer.Core.Time.WallclockTime.Zero + TimeSpan.FromSeconds(r),
                        PublisherNode = new Tracer.Core.Identity.AgentId("publisher"),
                        SubscriberNode = new Tracer.Core.Identity.AgentId("subscriber"),
                        Topic = new Tracer.Core.Domain.TopicName("test.topic"),
                        InstanceKey = $"entity-{e}",
                        Rate = Tracer.Core.Records.StateSampleRate.Slow,
                        PayloadJson = "{}",
                    };
                    await writer.AppendStateAsync(record, default);
                }
            }
            await writer.FlushAsync(default);
        }

        // Query entity-1 rows via a raw DuckDB connection (bypassing the writer) to measure index performance
        var t1 = Tracer.Core.Time.WallclockTime.Zero + TimeSpan.FromSeconds(100);
        var t2 = Tracer.Core.Time.WallclockTime.Zero + TimeSpan.FromSeconds(200);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await Task.Run(() =>
        {
            using var conn = new DuckDBConnection($"Data Source={_dbPath}");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT COUNT(*) FROM slow_state
                WHERE instance_key = $entityId
                  AND publish_wallclock >= $t1
                  AND publish_wallclock < $t2
                """;
            cmd.Parameters.Add(new DuckDBParameter("entityId", "entity-1"));
            cmd.Parameters.Add(new DuckDBParameter("t1", t1.ToDateTimeOffset().UtcDateTime));
            cmd.Parameters.Add(new DuckDBParameter("t2", t2.ToDateTimeOffset().UtcDateTime));
            var count = (long)cmd.ExecuteScalar()!;
            count.Should().BeGreaterThan(0, "rows for entity-1 should exist in time range");
        });
        sw.Stop();

        sw.ElapsedMilliseconds.Should().BeLessThan(200,
            "indexed instance_key+time query should complete under 200ms");
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
