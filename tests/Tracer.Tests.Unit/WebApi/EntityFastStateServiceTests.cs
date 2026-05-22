using DuckDB.NET.Data;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Tracer.Agent.Storage;
using Tracer.Bundle.Format;
using Tracer.Core.Time;
using Tracer.Storage.DuckDB.MultiInterval;
using Tracer.Storage.Parquet;
using Tracer.WebApi.Queries;
using Xunit;

namespace Tracer.Tests.Unit.WebApi;

/// <summary>Tests for <see cref="EntityFastStateService"/> using real temp Parquet files.</summary>
public sealed class EntityFastStateServiceTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly ParquetReader _parquet;

    public EntityFastStateServiceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"efss-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
        _parquet = new ParquetReader(NullLogger<ParquetReader>.Instance);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempRoot, recursive: true); } catch { /* best-effort */ }
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static EntityFastStateService MakeService(
        ParquetReader reader,
        params IntervalDirectory[] intervals)
    {
        var snapshot = new IntervalSetSnapshot(
            intervals
                .Select(d => new IntervalReference(d, IntervalRole.Completed))
                .ToList()
                .AsReadOnly());
        var tracker = new StubTracker(snapshot);
        var locator = new FastStateFileLocator(tracker);
        return new EntityFastStateService(reader, locator, NullLogger<EntityFastStateService>.Instance);
    }

    private static EntityFastStateService MakeEmptyService(ParquetReader reader)
    {
        var tracker = new StubTracker(new IntervalSetSnapshot([]));
        var locator = new FastStateFileLocator(tracker);
        return new EntityFastStateService(reader, locator, NullLogger<EntityFastStateService>.Instance);
    }

    private IntervalDirectory CreateIntervalDir(string name)
    {
        var dir = Path.Combine(_tempRoot, name);
        Directory.CreateDirectory(dir);
        var fakeDb = Path.Combine(dir, "events.duckdb");
        File.WriteAllText(fakeDb, "");
        return IntervalDirectory.ForEventsDb(fakeDb);
    }

    private async Task<string> CreateParquetFileAsync(
        IntervalDirectory iv,
        string topic,
        string entityId,
        int sampleCount = 20,
        int startSecond = 1)
    {
        // topic is used as-is as the directory name (caller must pass already-safe-encoded name
        // to mirror what LocateFilesBySafeTopicName expects).
        var safeEntity = BundleNaming.SafeFileName(entityId);
        var dir = Path.Combine(iv.FastStateDirectory, topic, safeEntity);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "samples.parquet");
        var escapedPath = path.Replace("\\", "/");

        await using var conn = new DuckDBConnection("Data Source=:memory:");
        await conn.OpenAsync();

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("CREATE TABLE t (publish_wallclock TIMESTAMP, instance_key VARCHAR, x FLOAT, y FLOAT);");
        sb.Append("INSERT INTO t VALUES ");
        for (int i = 0; i < sampleCount; i++)
        {
            var sec = startSecond + i;
            var h = sec / 3600;
            var m = (sec % 3600) / 60;
            var s = sec % 60;
            if (i > 0) sb.Append(", ");
            sb.Append($"(TIMESTAMP '1970-01-01 {h:D2}:{m:D2}:{s:D2}', '{entityId}', {(float)i}, {(float)(i * 2)})");
        }
        sb.Append(';');

        foreach (var stmt in sb.ToString().Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
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

    // ── Tests ─────────────────────────────────────────────────────────────

    [Fact]
    public void GetAvailableTopics_DelegatesToLocator()
    {
        var iv = CreateIntervalDir("iv-topics");
        var safeTopic = BundleNaming.SafeFileName("pos");
        var safeEntity = BundleNaming.SafeFileName("ent-A");
        var topicDir = Path.Combine(iv.FastStateDirectory, safeTopic, safeEntity);
        Directory.CreateDirectory(topicDir);

        var service = MakeService(_parquet, iv);
        var topics = service.GetAvailableTopics("ent-A");

        topics.Should().Contain(safeTopic);
    }

    [Fact]
    public async Task GetSchemaAsync_NoFiles_ReturnsNull()
    {
        var service = MakeEmptyService(_parquet);
        var schema = await service.GetSchemaAsync("ent-A", "pos", CancellationToken.None);
        schema.Should().BeNull();
    }

    [Fact]
    public async Task GetSchemaAsync_ValidFile_ExcludesInfrastructureColumns()
    {
        var iv = CreateIntervalDir("iv-schema");
        var safeTopic = BundleNaming.SafeFileName("pos");
        await CreateParquetFileAsync(iv, safeTopic, "ent-A");

        var service = MakeService(_parquet, iv);
        var schema = await service.GetSchemaAsync("ent-A", safeTopic, CancellationToken.None);

        schema.Should().NotBeNull();
        schema!.Columns.Should().NotContain(c => c.Name == "publish_wallclock");
        schema.Columns.Should().NotContain(c => c.Name == "instance_key");
        schema.Columns.Should().Contain(c => c.Name == "x");
        schema.Columns.Should().Contain(c => c.Name == "y");
    }

    [Fact]
    public async Task ReadAsync_NoFiles_ReturnsEmptyResult()
    {
        var service = MakeEmptyService(_parquet);
        var from = WallclockTime.Zero;
        var to = WallclockTime.Zero + TimeSpan.FromHours(1);

        var result = await service.ReadAsync("ent-A", "pos", ["x"], from, to, 5000, CancellationToken.None);

        result.Samples.Should().BeEmpty();
        result.TotalSamples.Should().Be(0);
        result.Downsampled.Should().BeFalse();
    }

    [Fact]
    public async Task ReadAsync_SingleFile_ReturnsCorrectData()
    {
        var iv = CreateIntervalDir("iv-single");
        var safeTopic = BundleNaming.SafeFileName("pos");
        await CreateParquetFileAsync(iv, safeTopic, "ent-A", sampleCount: 20);

        var service = MakeService(_parquet, iv);
        var from = WallclockTime.Zero;
        var to = WallclockTime.Zero + TimeSpan.FromHours(1);

        var result = await service.ReadAsync("ent-A", safeTopic, ["x"], from, to, 5000, CancellationToken.None);

        result.Samples.Count.Should().Be(20);
        result.EntityId.Should().Be("ent-A");
        result.Topic.Should().Be(safeTopic);
        result.Downsampled.Should().BeFalse();
    }

    [Fact]
    public async Task ReadAsync_MultipleFiles_TotalSamplesSummed()
    {
        var iv1 = CreateIntervalDir("iv-multi1");
        var iv2 = CreateIntervalDir("iv-multi2");
        var safeTopic = BundleNaming.SafeFileName("pos");
        await CreateParquetFileAsync(iv1, safeTopic, "ent-B", sampleCount: 10, startSecond: 1);
        await CreateParquetFileAsync(iv2, safeTopic, "ent-B", sampleCount: 10, startSecond: 11);

        var service = MakeService(_parquet, iv1, iv2);
        var from = WallclockTime.Zero;
        var to = WallclockTime.Zero + TimeSpan.FromHours(1);

        var result = await service.ReadAsync("ent-B", safeTopic, ["x"], from, to, 5000, CancellationToken.None);

        result.TotalSamples.Should().Be(20);
    }

    [Fact]
    public async Task ReadAsync_DownsamplingPropagated()
    {
        var iv = CreateIntervalDir("iv-downsample");
        var safeTopic = BundleNaming.SafeFileName("pos");
        await CreateParquetFileAsync(iv, safeTopic, "ent-C", sampleCount: 200);

        var service = MakeService(_parquet, iv);
        var from = WallclockTime.Zero;
        var to = WallclockTime.Zero + TimeSpan.FromHours(1);

        var result = await service.ReadAsync("ent-C", safeTopic, ["x"], from, to, maxSamples: 50, CancellationToken.None);

        result.Downsampled.Should().BeTrue();
        result.Samples.Count.Should().BeLessOrEqualTo(50);
    }

    // ── Stub ──────────────────────────────────────────────────────────────

    private sealed class StubTracker : IntervalSetTracker
    {
        private readonly IntervalSetSnapshot _snapshot;

        public StubTracker(IntervalSetSnapshot snapshot)
            : base(null!, 0, NullLogger<IntervalSetTracker>.Instance)
        {
            _snapshot = snapshot;
        }

        public override IntervalSetSnapshot CurrentSnapshot() => _snapshot;
    }
}
