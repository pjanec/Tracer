using DuckDB.NET.Data;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Tracer.Core.Time;
using Tracer.Storage.Parquet;
using Xunit;

namespace Tracer.Tests.Unit.Parquet;

public sealed class ParquetReaderTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ParquetReader _reader;

    public ParquetReaderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"parquet-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _reader = new ParquetReader(NullLogger<ParquetReader>.Instance);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort */ }
    }

    // ── Schema inspection ──────────────────────────────────────────────────────

    [Fact]
    public async Task InspectSchemaAsync_ThreeColumnParquet_ReturnsAllColumns()
    {
        var path = await CreateParquetAsync("single_schema", """
            CREATE TABLE t (publish_wallclock TIMESTAMP, instance_key VARCHAR, x FLOAT);
            INSERT INTO t VALUES (TIMESTAMP '1970-01-01 00:00:01', 'ent-A', 1.0);
            """);

        var schema = await _reader.InspectSchemaAsync(path, default);

        schema.Columns.Should().HaveCount(3);
        schema.Columns.Should().ContainSingle(c => c.Name == "x" && c.IsNumeric);
        schema.Columns.Should().ContainSingle(c => c.Name == "instance_key" && !c.IsNumeric);
        schema.Columns.Should().ContainSingle(c => c.Name == "publish_wallclock");
    }

    [Fact]
    public async Task InspectSchemaAsync_NonExistentPath_PropagatesException()
    {
        var act = async () => await _reader.InspectSchemaAsync("nonexistent_file.parquet", default);

        await act.Should().ThrowAsync<Exception>();
    }

    // ── Time series — no data ──────────────────────────────────────────────────

    [Fact]
    public async Task ReadTimeSeriesAsync_NarrowTimeRange_ReturnsEmpty()
    {
        var path = await CreateParquetAsync("narrow_range", """
            CREATE TABLE t (publish_wallclock TIMESTAMP, instance_key VARCHAR, x FLOAT);
            INSERT INTO t VALUES (TIMESTAMP '1970-01-01 00:00:01', 'ent-A', 1.0),
                                 (TIMESTAMP '1970-01-01 00:00:02', 'ent-A', 2.0);
            """);

        var from = WallclockTime.Zero + TimeSpan.FromSeconds(300);
        var to   = WallclockTime.Zero + TimeSpan.FromSeconds(400);

        var result = await _reader.ReadTimeSeriesAsync(path, "ent-A", ["x"], from, to, 5000, default);

        result.TotalSamples.Should().Be(0);
        result.Samples.Should().BeEmpty();
        result.Downsampled.Should().BeFalse();
    }

    // ── Below maxSamples — no downsampling ────────────────────────────────────

    [Fact]
    public async Task ReadTimeSeriesAsync_BelowMaxSamples_NoDownsampling()
    {
        const int sampleCount = 50;
        var path = await CreateParquetAsync("below_max", BuildInsertSql(sampleCount, "ent-A"));

        var from = WallclockTime.Zero;
        var to   = WallclockTime.Zero + TimeSpan.FromSeconds(sampleCount + 1);

        var result = await _reader.ReadTimeSeriesAsync(path, "ent-A", ["x"], from, to, 100, default);

        result.Samples.Count.Should().Be(sampleCount);
        result.Downsampled.Should().BeFalse();
        result.TotalSamples.Should().Be(sampleCount);
    }

    // ── Above maxSamples — stride downsampling ────────────────────────────────

    [Fact]
    public async Task ReadTimeSeriesAsync_AboveMaxSamples_StridedDownsampling()
    {
        const int sampleCount = 1000;
        var path = await CreateParquetAsync("above_max", BuildInsertSql(sampleCount, "ent-A"));

        var from = WallclockTime.Zero;
        var to   = WallclockTime.Zero + TimeSpan.FromSeconds(sampleCount + 1);

        var result = await _reader.ReadTimeSeriesAsync(path, "ent-A", ["x"], from, to, 100, default);

        result.Downsampled.Should().BeTrue();
        result.Samples.Count.Should().BeLessOrEqualTo(100);
        result.TotalSamples.Should().Be(sampleCount);

        // Verify samples are ordered ascending by timestamp
        for (int i = 1; i < result.Samples.Count; i++)
            result.Samples[i].PublishWallclock.Should().BeGreaterOrEqualTo(result.Samples[i - 1].PublishWallclock);
    }

    // ── Multiple files ────────────────────────────────────────────────────────

    [Fact]
    public async Task ReadTimeSeriesAsync_MultipleFiles_MergesRows()
    {
        const int perFile = 50;
        var pathA = await CreateParquetAsync("multi_a", BuildInsertSql(perFile, "ent-A", startSecond: 1));
        var pathB = await CreateParquetAsync("multi_b", BuildInsertSql(perFile, "ent-A", startSecond: perFile + 1));

        var from = WallclockTime.Zero;
        var to   = WallclockTime.Zero + TimeSpan.FromSeconds(perFile * 2 + 1);

        var result = await _reader.ReadTimeSeriesAsync(
            [pathA, pathB], "ent-A", ["x"], from, to, 5000, default);

        result.TotalSamples.Should().Be(perFile * 2);

        // Verify ascending order
        for (int i = 1; i < result.Samples.Count; i++)
            result.Samples[i].PublishWallclock.Should().BeGreaterOrEqualTo(result.Samples[i - 1].PublishWallclock);
    }

    // ── Null values ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ReadTimeSeriesAsync_NullNumericValue_CoercedToNull()
    {
        var path = await CreateParquetAsync("null_value", """
            CREATE TABLE t (publish_wallclock TIMESTAMP, instance_key VARCHAR, x FLOAT);
            INSERT INTO t VALUES (TIMESTAMP '1970-01-01 00:00:01', 'ent-A', NULL);
            """);

        var from = WallclockTime.Zero;
        var to   = WallclockTime.Zero + TimeSpan.FromSeconds(10);

        var result = await _reader.ReadTimeSeriesAsync(path, "ent-A", ["x"], from, to, 5000, default);

        result.Samples.Should().HaveCount(1);
        result.Samples[0].Values["x"].Should().BeNull();
    }

    // ── SafeColumnIdentifier unit tests ──────────────────────────────────────

    [Fact]
    public void SafeColumnIdentifier_PlainName_WrapsInDoubleQuotes()
    {
        var result = ParquetReader.SafeColumnIdentifier("myColumn");
        result.Should().Be("\"myColumn\"");
    }

    [Fact]
    public void SafeColumnIdentifier_EmbeddedDoubleQuote_Escaped()
    {
        // col"name → "col""name"
        var result = ParquetReader.SafeColumnIdentifier("col\"name");
        result.Should().Be("\"col\"\"name\"");
    }

    // ── EscapeSql unit tests ──────────────────────────────────────────────────

    [Fact]
    public void EscapeSql_NoSpecialChars_Unchanged()
    {
        ParquetReader.EscapeSql("path/to/file.parquet").Should().Be("path/to/file.parquet");
    }

    [Fact]
    public void EscapeSql_SingleQuoteInPath_Doubled()
    {
        ParquetReader.EscapeSql("a'b").Should().Be("a''b");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<string> CreateParquetAsync(string name, string tableAndInsertSql)
    {
        var path = Path.Combine(_tempDir, $"{name}.parquet");
        var escapedPath = path.Replace("\\", "/");

        await using var conn = new DuckDBConnection("Data Source=:memory:");
        await conn.OpenAsync();

        foreach (var stmt in tableAndInsertSql.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (stmt.Length == 0) continue;
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = stmt;
            await cmd.ExecuteNonQueryAsync();
        }

        await using var copyCmd = conn.CreateCommand();
        copyCmd.CommandText = $"COPY t TO '{escapedPath}' (FORMAT PARQUET)";
        await copyCmd.ExecuteNonQueryAsync();

        return path;
    }

    private static string BuildInsertSql(int count, string entityId, int startSecond = 1)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("CREATE TABLE t (publish_wallclock TIMESTAMP, instance_key VARCHAR, x FLOAT);");
        sb.Append("INSERT INTO t VALUES ");
        for (int i = 0; i < count; i++)
        {
            var sec = startSecond + i;
            var h = sec / 3600;
            var m = (sec % 3600) / 60;
            var s = sec % 60;
            if (i > 0) sb.Append(", ");
            sb.Append($"(TIMESTAMP '1970-01-01 {h:D2}:{m:D2}:{s:D2}', '{entityId}', {(float)i})");
        }
        sb.Append(';');
        return sb.ToString();
    }
}
