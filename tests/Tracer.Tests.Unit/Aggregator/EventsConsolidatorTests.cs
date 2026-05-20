using DuckDB.NET.Data;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Tracer.Aggregator.Consolidation;
using Tracer.Aggregator.Discovery;
using Tracer.Core.Domain;
using Tracer.Core.Identity;
using Tracer.Core.Records;
using Tracer.Core.Time;
using Tracer.Storage.DuckDB;
using Xunit;

namespace Tracer.Tests.Unit.Aggregator;

public sealed class EventsConsolidatorTests : IAsyncDisposable
{
    private readonly List<string> _dirs = new();

    private string TempDir()
    {
        var d = Path.Combine(Path.GetTempPath(), $"ev-cons-{Guid.NewGuid():N}");
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

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static readonly DateTimeOffset _base = new(2026, 5, 19, 14, 0, 0, TimeSpan.Zero);

    private static EventRecord MakeEvent(DateTimeOffset when, ulong seq = 1) => new EventRecord
    {
        EventId = new EventId(seq),
        TraceId = new TraceId(1),
        SequenceNumber = seq,
        PublishWallclock = WallclockTime.FromDateTimeOffset(when),
        ReceiveWallclock = WallclockTime.FromDateTimeOffset(when.AddMilliseconds(1)),
        PublisherNode = new AgentId("pub"),
        SubscriberNode = new AgentId("sub"),
        Topic = new TopicName("test.topic"),
        PayloadJson = "{}",
    };

    /// <summary>Creates an interval directory with N events in events.duckdb.</summary>
    private async Task<string> CreateSourceIntervalAsync(
        DateTimeOffset[] eventTimes, string? dirOverride = null)
    {
        var dir = dirOverride ?? TempDir();
        await using var writer = await DuckDbStorageWriter.CreateAsync(
            dir,
            new Dictionary<string, Tracer.Storage.DuckDB.Parquet.ParquetTopicSchema>(),
            NullLogger<DuckDbStorageWriter>.Instance);

        for (int i = 0; i < eventTimes.Length; i++)
        {
            await writer.AppendEventAsync(MakeEvent(eventTimes[i], (ulong)(i + 1)));
        }
        await writer.FlushAsync();
        return dir;
    }

    private static IntervalDescriptor MakeDescriptor(DateTimeOffset start, DateTimeOffset end)
        => new(IntervalTimestamp.FromUtc(start), start, end);

    private static Tracer.Core.Time.TimeRange MakeRange(DateTimeOffset start, DateTimeOffset end)
        => new(WallclockTime.FromDateTimeOffset(start), WallclockTime.FromDateTimeOffset(end));

    private static async Task<long> CountRowsAsync(string dbPath)
    {
        await using var conn = new DuckDBConnection($"Data Source={dbPath}");
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM events;";
        var result = await cmd.ExecuteScalarAsync();
        return result is null ? 0L : Convert.ToInt64(result);
    }

    private static async Task<bool> TableHasIndexAsync(string dbPath, string indexName)
    {
        await using var conn = new DuckDBConnection($"Data Source={dbPath}");
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM duckdb_indexes() WHERE index_name = '{indexName}';";
        var result = await cmd.ExecuteScalarAsync();
        return result is not null && Convert.ToInt64(result) > 0;
    }

    private static bool HasWalFile(string dbPath) =>
        File.Exists(dbPath + ".wal");

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ConsolidateAsync_SingleSource_OutputHasSameRowCount()
    {
        var dir1 = await CreateSourceIntervalAsync(new[]
        {
            _base, _base.AddSeconds(1), _base.AddSeconds(2)
        });

        var outputPath = Path.Combine(TempDir(), "events.duckdb");
        var desc = MakeDescriptor(_base, _base.AddHours(1));
        var sources = new[] { new ExtractedInterval("node-a", desc, dir1) };
        var range = MakeRange(_base.AddMinutes(-5), _base.AddHours(1));

        await EventsConsolidator.ConsolidateAsync(sources, outputPath, range, null);

        var count = await CountRowsAsync(outputPath);
        count.Should().Be(3);
    }

    [Fact]
    public async Task ConsolidateAsync_MultipleSources_OutputHasSumOfRows()
    {
        const int eventsPerInterval = 5;
        var dirs = new List<string>();
        for (int i = 0; i < 2; i++)
        {
            var times = Enumerable.Range(0, eventsPerInterval)
                .Select(j => _base.AddSeconds(i * 100 + j))
                .ToArray();
            dirs.Add(await CreateSourceIntervalAsync(times));
        }

        var outputPath = Path.Combine(TempDir(), "events.duckdb");
        var desc = MakeDescriptor(_base, _base.AddHours(1));
        var sources = dirs
            .Select((d, i) => new ExtractedInterval($"node-{i}", desc, d))
            .ToArray();
        var range = MakeRange(_base.AddMinutes(-5), _base.AddHours(1));

        await EventsConsolidator.ConsolidateAsync(sources, outputPath, range, null);

        var count = await CountRowsAsync(outputPath);
        count.Should().Be(eventsPerInterval * 2);
    }

    [Fact]
    public async Task ConsolidateAsync_TimeRangeFilter_ExcludesRowsOutsideRange()
    {
        // 3 events: one before range, one inside, one after
        var before = _base.AddMinutes(-10);
        var inside = _base.AddMinutes(5);
        var after = _base.AddMinutes(70);

        var dir1 = await CreateSourceIntervalAsync(new[] { before, inside, after });

        var outputPath = Path.Combine(TempDir(), "events.duckdb");
        var desc = MakeDescriptor(_base.AddMinutes(-15), _base.AddHours(2));
        var sources = new[] { new ExtractedInterval("node-a", desc, dir1) };

        // Range covers only the middle event
        var range = MakeRange(_base, _base.AddHours(1));

        await EventsConsolidator.ConsolidateAsync(sources, outputPath, range, null);

        var count = await CountRowsAsync(outputPath);
        count.Should().Be(1, "only the event at _base+5min is within the range");
    }

    [Fact]
    public async Task ConsolidateAsync_OutputHasIndexes()
    {
        var dir1 = await CreateSourceIntervalAsync(new[] { _base });
        var outputPath = Path.Combine(TempDir(), "events.duckdb");
        var desc = MakeDescriptor(_base, _base.AddHours(1));
        var sources = new[] { new ExtractedInterval("node-a", desc, dir1) };
        var range = MakeRange(_base.AddMinutes(-1), _base.AddHours(1));

        await EventsConsolidator.ConsolidateAsync(sources, outputPath, range, null);

        (await TableHasIndexAsync(outputPath, "idx_events_trace")).Should().BeTrue();
        (await TableHasIndexAsync(outputPath, "idx_events_entity")).Should().BeTrue();
        (await TableHasIndexAsync(outputPath, "idx_events_topic_time")).Should().BeTrue();
    }

    [Fact]
    public async Task ConsolidateAsync_OutputIsCheckpointed_NoWalFile()
    {
        var dir1 = await CreateSourceIntervalAsync(new[] { _base });
        var outputPath = Path.Combine(TempDir(), "events.duckdb");
        var desc = MakeDescriptor(_base, _base.AddHours(1));
        var sources = new[] { new ExtractedInterval("node-a", desc, dir1) };
        var range = MakeRange(_base.AddMinutes(-1), _base.AddHours(1));

        await EventsConsolidator.ConsolidateAsync(sources, outputPath, range, null);

        HasWalFile(outputPath).Should().BeFalse("CHECKPOINT should flush the WAL");
    }
}
