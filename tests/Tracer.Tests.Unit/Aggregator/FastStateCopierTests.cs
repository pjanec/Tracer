using DuckDB.NET.Data;
using FluentAssertions;
using Tracer.Aggregator.Configuration;
using Tracer.Aggregator.Consolidation;
using Tracer.Aggregator.Discovery;
using Tracer.Bundle.Format;
using Tracer.Core.Domain;
using Tracer.Core.Time;
using Xunit;

namespace Tracer.Tests.Unit.Aggregator;

public sealed class FastStateCopierTests : IAsyncDisposable
{
    private readonly List<string> _dirs = new();

    private string TempDir()
    {
        var d = Path.Combine(Path.GetTempPath(), $"fsc-{Guid.NewGuid():N}");
        Directory.CreateDirectory(d);
        _dirs.Add(d);
        return d;
    }

    public async ValueTask DisposeAsync()
    {
        await Task.CompletedTask;
        foreach (var d in _dirs)
        {
            try { if (Directory.Exists(d)) Directory.Delete(d, recursive: true); } catch { }
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static readonly DateTimeOffset _base = new(2026, 5, 19, 14, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Creates a source interval directory with a Parquet file in the fast_state/ sub-dir.
    /// Returns the interval directory (ExtractedInterval.Directory).
    /// </summary>
    private async Task<string> CreateSourceIntervalAsync(
        string topic,
        (string entity, DateTimeOffset time)[] rows)
    {
        var dir = TempDir();
        var fastStateDir = Path.Combine(dir, "fast_state");
        Directory.CreateDirectory(fastStateDir);

        // Write a Parquet file with the minimal columns FastStateCopier queries
        await using var conn = new DuckDBConnection("Data Source=:memory:");
        await conn.OpenAsync();

        await ExecAsync(conn, """
            CREATE TABLE src (
                publish_wallclock TIMESTAMPTZ,
                receive_wallclock TIMESTAMPTZ,
                publisher_node    VARCHAR,
                instance_key      VARCHAR,
                sequence_number   UBIGINT
            );
            """);

        foreach (var (entity, time) in rows)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO src VALUES ($pw, $rw, 'pub', $ik, 1);
                """;
            cmd.Parameters.Add(new DuckDBParameter("pw", time.UtcDateTime));
            cmd.Parameters.Add(new DuckDBParameter("rw", time.AddMilliseconds(1).UtcDateTime));
            cmd.Parameters.Add(new DuckDBParameter("ik", entity));
            await cmd.ExecuteNonQueryAsync();
        }

        var outPath = Path.Combine(fastStateDir, $"{topic}.parquet");
        await ExecAsync(conn, $"COPY src TO '{EscapeSql(outPath)}' (FORMAT PARQUET);");

        return dir;
    }

    private static async Task ExecAsync(DuckDBConnection conn, string sql)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    private static string EscapeSql(string s) => s.Replace("'", "''");

    private static Tracer.Core.Time.TimeRange MakeRange(DateTimeOffset start, DateTimeOffset end)
        => new(WallclockTime.FromDateTimeOffset(start), WallclockTime.FromDateTimeOffset(end));

    private static IntervalDescriptor MakeDescriptor(DateTimeOffset start, DateTimeOffset end)
        => new(IntervalTimestamp.FromUtc(start), start, end);

    private static async Task<long> CountParquetRowsAsync(string parquetPath)
    {
        await using var conn = new DuckDBConnection("Data Source=:memory:");
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM read_parquet('{EscapeSql(parquetPath)}');";
        var result = await cmd.ExecuteScalarAsync();
        return result is null ? 0L : Convert.ToInt64(result);
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CopyAsync_ScopeNone_NoFastStateDirCreated()
    {
        var srcDir = await CreateSourceIntervalAsync("sensor.data", new[]
        {
            ("entity-1", _base)
        });
        var bundleDir = TempDir();
        var desc = MakeDescriptor(_base, _base.AddHours(1));
        var sources = new[] { new ExtractedInterval("node-a", desc, srcDir) };
        var range = MakeRange(_base.AddMinutes(-5), _base.AddHours(1));

        var stats = await FastStateCopier.CopyAsync(
            sources, bundleDir, FastStateScope.None, null, range, null);

        stats.TotalRowCount.Should().Be(0);
        stats.EntityCount.Should().Be(0);
        Directory.Exists(Path.Combine(bundleDir, BundleLayout.FastStateDirectory))
            .Should().BeFalse("ScopeNone should not create any output directory");
    }

    [Fact]
    public async Task CopyAsync_ScopeAll_AllEntitiesCopied()
    {
        var srcDir = await CreateSourceIntervalAsync("sensor.data", new[]
        {
            ("entity-1", _base),
            ("entity-2", _base.AddSeconds(1)),
            ("entity-3", _base.AddSeconds(2)),
        });
        var bundleDir = TempDir();
        var desc = MakeDescriptor(_base.AddMinutes(-5), _base.AddHours(1));
        var sources = new[] { new ExtractedInterval("node-a", desc, srcDir) };
        var range = MakeRange(_base.AddMinutes(-5), _base.AddHours(1));

        var stats = await FastStateCopier.CopyAsync(
            sources, bundleDir, FastStateScope.All, null, range, null);

        stats.EntityCount.Should().Be(3, "all 3 entities should be copied");
        stats.TotalRowCount.Should().Be(3);
    }

    [Fact]
    public async Task CopyAsync_ScopeSelectedEntities_OnlySpecifiedEntitiesCopied()
    {
        var srcDir = await CreateSourceIntervalAsync("sensor.data", new[]
        {
            ("entity-1", _base),
            ("entity-2", _base.AddSeconds(1)),
            ("entity-3", _base.AddSeconds(2)),
        });
        var bundleDir = TempDir();
        var desc = MakeDescriptor(_base.AddMinutes(-5), _base.AddHours(1));
        var sources = new[] { new ExtractedInterval("node-a", desc, srcDir) };
        var range = MakeRange(_base.AddMinutes(-5), _base.AddHours(1));

        var stats = await FastStateCopier.CopyAsync(
            sources, bundleDir, FastStateScope.SelectedEntities,
            new[] { "entity-1", "entity-3" }, range, null);

        stats.EntityCount.Should().Be(2, "only entity-1 and entity-3 should be copied");
        stats.TotalRowCount.Should().Be(2);
    }

    [Fact]
    public async Task CopyAsync_MultiSource_SameEntity_MergedIntoOneSamplesParquet()
    {
        // Two source intervals both have entity-1 — rows should be merged
        var srcDir1 = await CreateSourceIntervalAsync("sensor.data", new[]
        {
            ("entity-1", _base),
            ("entity-1", _base.AddSeconds(1)),
        });
        var srcDir2 = await CreateSourceIntervalAsync("sensor.data", new[]
        {
            ("entity-1", _base.AddSeconds(30)),
        });

        var bundleDir = TempDir();
        var desc = MakeDescriptor(_base.AddMinutes(-5), _base.AddHours(1));
        var sources = new[]
        {
            new ExtractedInterval("node-a", desc, srcDir1),
            new ExtractedInterval("node-b", desc, srcDir2),
        };
        var range = MakeRange(_base.AddMinutes(-5), _base.AddHours(1));

        var stats = await FastStateCopier.CopyAsync(
            sources, bundleDir, FastStateScope.All, null, range, null);

        stats.EntityCount.Should().Be(1, "same logical entity across two sources");
        // TotalRowCount accumulates COUNT(*) of each output-file write:
        // first source: 2 rows → 2; second source: UNION ALL gives 3 rows in file → 3; total = 5
        stats.TotalRowCount.Should().Be(5);

        // Verify the output Parquet has 3 rows
        var safeTopic = BundleNaming.SafeFileName("sensor.data");
        var safeEntity = BundleNaming.SafeFileName("entity-1");
        var outParquet = Path.Combine(
            bundleDir, BundleLayout.FastStateDirectory, safeTopic, safeEntity, "samples.parquet");
        File.Exists(outParquet).Should().BeTrue();
        var fileRowCount = await CountParquetRowsAsync(outParquet);
        fileRowCount.Should().Be(3);
    }

    [Fact]
    public async Task CopyAsync_TimeRangeFilter_ExcludesOutOfRangeRows()
    {
        var inside = _base.AddMinutes(10);
        var outside = _base.AddMinutes(90); // beyond range end

        var srcDir = await CreateSourceIntervalAsync("sensor.data", new[]
        {
            ("entity-1", inside),
            ("entity-1", outside),
        });

        var bundleDir = TempDir();
        var desc = MakeDescriptor(_base, _base.AddHours(2));
        var sources = new[] { new ExtractedInterval("node-a", desc, srcDir) };
        var range = MakeRange(_base, _base.AddHours(1)); // covers only 'inside'

        var stats = await FastStateCopier.CopyAsync(
            sources, bundleDir, FastStateScope.All, null, range, null);

        stats.TotalRowCount.Should().Be(1, "only the row within the time range should be copied");
    }
}
