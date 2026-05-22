using DuckDB.NET.Data;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Tracer.Core.Time;
using Tracer.Storage.Parquet;
using Xunit;

namespace Tracer.Tests.Integration;

/// <summary>
/// Integration tests that write real Parquet files via DuckDB and read them back
/// through <see cref="ParquetReader"/> to assert exact round-trip equality.
/// No WebApi, no TestHarness — pure Parquet I/O.
/// </summary>
public sealed class FastStateParquetRoundTripTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ParquetReader _reader;

    public FastStateParquetRoundTripTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"fast-state-rt-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _reader = new ParquetReader(NullLogger<ParquetReader>.Instance);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort */ }
    }

    // ── Test 1: Exact sample equality ────────────────────────────────────────

    [Fact]
    public async Task ReadTimeSeriesAsync_ExactSampleEquality()
    {
        // Write 5 known samples at t = 1s, 2s, 3s, 4s, 5s; x = 10, 20, 30, 40, 50
        var path = await WriteParquetAsync("exact_equality", """
            INSERT INTO t VALUES
                (TIMESTAMP '1970-01-01 00:00:01', 'ent-A', 10.0),
                (TIMESTAMP '1970-01-01 00:00:02', 'ent-A', 20.0),
                (TIMESTAMP '1970-01-01 00:00:03', 'ent-A', 30.0),
                (TIMESTAMP '1970-01-01 00:00:04', 'ent-A', 40.0),
                (TIMESTAMP '1970-01-01 00:00:05', 'ent-A', 50.0)
            """);

        var from = WallclockTime.Zero;
        var to = WallclockTime.Zero + TimeSpan.FromSeconds(10);

        var result = await _reader.ReadTimeSeriesAsync(path, "ent-A", ["x"], from, to, 5000, default);

        result.Samples.Count.Should().Be(5);
        result.Downsampled.Should().BeFalse();

        // Verify exact values
        double[] expectedX = [10.0, 20.0, 30.0, 40.0, 50.0];
        for (int i = 0; i < 5; i++)
        {
            result.Samples[i].Values["x"].Should().BeApproximately(expectedX[i], 0.001,
                because: $"sample {i} should have x = {expectedX[i]}");
        }

        // Verify ascending order by timestamp
        for (int i = 1; i < result.Samples.Count; i++)
        {
            result.Samples[i].PublishWallclock.Should()
                .BeGreaterOrEqualTo(result.Samples[i - 1].PublishWallclock);
        }

        // Verify expected timestamps
        var expectedSeconds = new[] { 1, 2, 3, 4, 5 };
        for (int i = 0; i < 5; i++)
        {
            var expected = WallclockTime.Zero + TimeSpan.FromSeconds(expectedSeconds[i]);
            result.Samples[i].PublishWallclock.Should().Be(expected,
                because: $"sample {i} should be at t = {expectedSeconds[i]}s");
        }
    }

    // ── Test 2: Multi-interval merge ─────────────────────────────────────────

    [Fact]
    public async Task ReadTimeSeriesAsync_MultiInterval_MergesBothFiles()
    {
        // FileA: 3 samples at t=1s, 2s, 3s
        var fileA = await WriteParquetAsync("multi_a", """
            INSERT INTO t VALUES
                (TIMESTAMP '1970-01-01 00:00:01', 'ent-A', 1.0),
                (TIMESTAMP '1970-01-01 00:00:02', 'ent-A', 2.0),
                (TIMESTAMP '1970-01-01 00:00:03', 'ent-A', 3.0)
            """);

        // FileB: 3 samples at t=4s, 5s, 6s
        var fileB = await WriteParquetAsync("multi_b", """
            INSERT INTO t VALUES
                (TIMESTAMP '1970-01-01 00:00:04', 'ent-A', 4.0),
                (TIMESTAMP '1970-01-01 00:00:05', 'ent-A', 5.0),
                (TIMESTAMP '1970-01-01 00:00:06', 'ent-A', 6.0)
            """);

        var from = WallclockTime.Zero;
        var to = WallclockTime.Zero + TimeSpan.FromSeconds(7);

        var result = await _reader.ReadTimeSeriesAsync(
            [fileA, fileB], "ent-A", ["x"], from, to, 5000, default);

        result.TotalSamples.Should().Be(6);
        result.Samples.Count.Should().Be(6);
        result.Downsampled.Should().BeFalse();

        // Verify ascending order
        for (int i = 1; i < result.Samples.Count; i++)
        {
            result.Samples[i].PublishWallclock.Should()
                .BeGreaterOrEqualTo(result.Samples[i - 1].PublishWallclock);
        }

        // First sample from fileA, last from fileB
        result.Samples[0].PublishWallclock.Should()
            .Be(WallclockTime.Zero + TimeSpan.FromSeconds(1));
        result.Samples[5].PublishWallclock.Should()
            .Be(WallclockTime.Zero + TimeSpan.FromSeconds(6));
    }

    // ── Test 3: Time-range filter ─────────────────────────────────────────────

    [Fact]
    public async Task ReadTimeSeriesAsync_TimeRangeFilter_ExcludesOutOfRange()
    {
        // Write 10 samples at t=1s..10s
        var path = await WriteParquetAsync("time_filter", """
            INSERT INTO t VALUES
                (TIMESTAMP '1970-01-01 00:00:01', 'ent-A', 1.0),
                (TIMESTAMP '1970-01-01 00:00:02', 'ent-A', 2.0),
                (TIMESTAMP '1970-01-01 00:00:03', 'ent-A', 3.0),
                (TIMESTAMP '1970-01-01 00:00:04', 'ent-A', 4.0),
                (TIMESTAMP '1970-01-01 00:00:05', 'ent-A', 5.0),
                (TIMESTAMP '1970-01-01 00:00:06', 'ent-A', 6.0),
                (TIMESTAMP '1970-01-01 00:00:07', 'ent-A', 7.0),
                (TIMESTAMP '1970-01-01 00:00:08', 'ent-A', 8.0),
                (TIMESTAMP '1970-01-01 00:00:09', 'ent-A', 9.0),
                (TIMESTAMP '1970-01-01 00:00:10', 'ent-A', 10.0)
            """);

        // The reader uses >= from AND < to (half-open interval).
        // To include t=3,4,5,6,7 (5 samples), we need from=3s, to=8s.
        var from = WallclockTime.Zero + TimeSpan.FromSeconds(3);
        var to = WallclockTime.Zero + TimeSpan.FromSeconds(8);

        var result = await _reader.ReadTimeSeriesAsync(path, "ent-A", ["x"], from, to, 5000, default);

        // Expect exactly 5 samples: t=3,4,5,6,7
        result.Samples.Count.Should().Be(5);
        result.Downsampled.Should().BeFalse();

        // All returned samples must be within [from, to)
        foreach (var sample in result.Samples)
        {
            sample.PublishWallclock.Should().BeGreaterOrEqualTo(from,
                because: "samples before 'from' must be excluded");
            sample.PublishWallclock.Should().BeLessThan(to,
                because: "samples at or after 'to' must be excluded");
        }

        // Verify ascending order
        for (int i = 1; i < result.Samples.Count; i++)
        {
            result.Samples[i].PublishWallclock.Should()
                .BeGreaterOrEqualTo(result.Samples[i - 1].PublishWallclock);
        }
    }

    // ── Helper: write a Parquet file via in-memory DuckDB ────────────────────

    private async Task<string> WriteParquetAsync(string name, string insertSql)
    {
        var path = Path.Combine(_tempDir, $"{name}.parquet");
        var escapedPath = path.Replace("\\", "/");

        await using var conn = new DuckDBConnection("Data Source=:memory:");
        await conn.OpenAsync();

        await using var createCmd = conn.CreateCommand();
        createCmd.CommandText = "CREATE TABLE t (publish_wallclock TIMESTAMP, instance_key VARCHAR, x DOUBLE)";
        await createCmd.ExecuteNonQueryAsync();

        await using var insertCmd = conn.CreateCommand();
        insertCmd.CommandText = insertSql;
        await insertCmd.ExecuteNonQueryAsync();

        await using var copyCmd = conn.CreateCommand();
        copyCmd.CommandText = $"COPY t TO '{escapedPath}' (FORMAT PARQUET)";
        await copyCmd.ExecuteNonQueryAsync();

        return path;
    }
}
