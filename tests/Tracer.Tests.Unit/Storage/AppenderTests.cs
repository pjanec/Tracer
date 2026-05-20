using DuckDB.NET.Data;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Tracer.Core.Domain;
using Tracer.Core.Identity;
using Tracer.Core.Queries;
using Tracer.Core.Records;
using Tracer.Core.Time;
using Tracer.Storage.DuckDB;
using Tracer.Storage.DuckDB.Parquet;
using Xunit;

namespace Tracer.Tests.Unit.Storage;

public sealed class AppenderTests : IAsyncDisposable
{
    private readonly string _intervalDir;
    private readonly string _dbPath;

    public AppenderTests()
    {
        _intervalDir = Path.Combine(Path.GetTempPath(), $"tracer-appender-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_intervalDir);
        _dbPath = Path.Combine(_intervalDir, "events.duckdb");
    }

    public async ValueTask DisposeAsync()
    {
        await Task.CompletedTask;
        try { Directory.Delete(_intervalDir, recursive: true); } catch { /* best-effort */ }
    }

    // â”€â”€ helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private static WallclockTime T(int secondsOffset = 0) =>
        new WallclockTime(1_700_000_000_000_000_000L + (long)secondsOffset * 1_000_000_000L);

    private static EventRecord MakeEvent(ulong seq = 1, bool withOptional = true) =>
        new EventRecord
        {
            EventId = new EventId(seq),
            TraceId = new TraceId(42),
            ParentEventId = withOptional ? new EventId(seq > 1 ? seq - 1 : seq) : null,
            SequenceNumber = seq,
            PublishWallclock = T((int)seq),
            ReceiveWallclock = T((int)seq + 1),
            PublisherNode = new AgentId("pub"),
            SubscriberNode = new AgentId("sub"),
            Topic = new TopicName("test.topic"),
            EntityId = withOptional ? new EntityId("entity-1") : null,
            OwningPlayerId = withOptional ? "player-1" : null,
            ScenarioPhase = withOptional ? "phase-1" : null,
            Severity = withOptional ? Severity.Info : null,
            NotableLabel = withOptional ? "label-1" : null,
            PayloadJson = $"{{\"seq\":{seq}}}",
        };

    private static StateSampleRecord MakeState(StateSampleRate rate) =>
        new StateSampleRecord
        {
            SequenceNumber = 1,
            PublishWallclock = T(),
            ReceiveWallclock = T(1),
            PublisherNode = new AgentId("pub"),
            SubscriberNode = new AgentId("sub"),
            Topic = new TopicName("state.topic"),
            InstanceKey = "entity-1",
            Rate = rate,
            PayloadJson = "{}",
        };

    private Task<DuckDbStorageWriter> CreateWriterAsync() =>
        DuckDbStorageWriter.CreateAsync(
            _intervalDir,
            new Dictionary<string, ParquetTopicSchema>(),
            NullLogger<DuckDbStorageWriter>.Instance);

    // â”€â”€ tests â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public async Task AppendEvent_1000Records_RoundTrip()
    {
        await using var writer = await CreateWriterAsync();

        for (ulong i = 1; i <= 1000; i++)
            await writer.AppendEventAsync(MakeEvent(i), default);

        await writer.FlushAsync(default);
        await writer.DisposeAsync();

        await using var reader = await DuckDbStorageReader.OpenAsync(
            _dbPath, NullLogger<DuckDbStorageReader>.Instance);

        var count = await reader.CountEventsAsync(EventFilter.All, default);
        count.Should().Be(1000);

        var events = await reader.QueryEventsAsync(
            new EventQuery { Filter = EventFilter.All, Limit = 10 }, default);
        events.Should().HaveCount(10);
        events[0].PayloadJson.Should().Contain("seq");

        // Verify specific fields for a known record (seq=500)
        var known = await reader.GetEventAsync(new EventId(500), default);
        known.Should().NotBeNull();
        known!.EventId.Value.Should().Be(500UL);
        known.TraceId.Value.Should().Be(42UL);
        known.PublisherNode.Value.Should().Be("pub");
        known.Topic.Value.Should().Be("test.topic");
        known.PayloadJson.Should().Be("{\"seq\":500}");
    }

    [Fact]
    public async Task AppendEvent_NullFields_StoredAsNull()
    {
        await using var writer = await CreateWriterAsync();

        var ev = MakeEvent(1, withOptional: false);
        await writer.AppendEventAsync(ev, default);
        await writer.FlushAsync(default);
        await writer.DisposeAsync();

        await using var reader = await DuckDbStorageReader.OpenAsync(
            _dbPath, NullLogger<DuckDbStorageReader>.Instance);

        var result = await reader.GetEventAsync(new EventId(1), default);

        result.Should().NotBeNull();
        result!.ParentEventId.Should().BeNull();
        result.EntityId.Should().BeNull();
        result.OwningPlayerId.Should().BeNull();
        result.ScenarioPhase.Should().BeNull();
        result.Severity.Should().BeNull();
        result.NotableLabel.Should().BeNull();
    }

    [Fact]
    public async Task AppendState_FastRate_ThrowsNotSupported()
    {
        await using var writer = await CreateWriterAsync();

        var fastState = MakeState(StateSampleRate.Fast);
        var act = async () => await writer.AppendStateAsync(fastState, default);

        await act.Should().ThrowAsync<NotSupportedException>();
    }

    [Fact]
    public async Task AppendBatch_MixedRecords_RoutesCorrectly()
    {
        await using var writer = await CreateWriterAsync();

        // 5 events + 3 slow-state + 1 fast-state (fast silently skipped by AppendBatchAsync)
        var batch = new List<DiagnosticRecord>
        {
            MakeEvent(1),
            MakeEvent(2),
            MakeEvent(3),
            MakeEvent(4),
            MakeEvent(5),
            MakeState(StateSampleRate.Slow),
            MakeState(StateSampleRate.Slow),
            MakeState(StateSampleRate.Slow),
            MakeState(StateSampleRate.Fast),  // should be silently skipped
        };

        await writer.AppendBatchAsync(batch, default);
        await writer.FlushAsync(default);
        await writer.DisposeAsync();

        await using var reader = await DuckDbStorageReader.OpenAsync(
            _dbPath, NullLogger<DuckDbStorageReader>.Instance);

        var eventCount = await reader.CountEventsAsync(EventFilter.All, default);
        eventCount.Should().Be(5, "only the 5 event records should be in the events table");

        // Verify slow_state table via raw DuckDB connection
        var slowStateCount = await QueryScalarAsync<long>("SELECT COUNT(*) FROM slow_state");
        slowStateCount.Should().Be(3, "exactly 3 slow-state records should be in slow_state table");
    }

    // â”€â”€ raw query helper â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private Task<T> QueryScalarAsync<T>(string sql) =>
        Task.Run(() =>
        {
            using var conn = new DuckDBConnection($"Data Source={_dbPath}");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            return (T)Convert.ChangeType(cmd.ExecuteScalar()!, typeof(T));
        });

    [Fact]
    public async Task Writer_DisposeAsync_IsIdempotent()
    {
        var writer = await CreateWriterAsync();

        var act = async () =>
        {
            await writer.DisposeAsync();
            await writer.DisposeAsync(); // second dispose should not throw
        };

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Reader_SeesData_OnlyAfterWriterFlush()
    {
        await using var writer = await CreateWriterAsync();

        await writer.AppendEventAsync(MakeEvent(1), default);
        // No flush yet â€” data is buffered in the appender

        long countBefore;
        await using (var reader = await DuckDbStorageReader.OpenAsync(
            _dbPath, NullLogger<DuckDbStorageReader>.Instance))
        {
            countBefore = await reader.CountEventsAsync(EventFilter.All, default);
        }

        countBefore.Should().Be(0, "data not visible until FlushAsync is called");

        await writer.FlushAsync(default);

        long countAfter;
        await using (var reader = await DuckDbStorageReader.OpenAsync(
            _dbPath, NullLogger<DuckDbStorageReader>.Instance))
        {
            countAfter = await reader.CountEventsAsync(EventFilter.All, default);
        }

        countAfter.Should().Be(1, "data should be visible after FlushAsync");
    }
}
