using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Tracer.Core.Domain;
using Tracer.Core.Identity;
using Tracer.Core.Queries;
using Tracer.Core.Records;
using Tracer.Core.Time;
using Tracer.Storage.DuckDB;
using Xunit;

namespace Tracer.Tests.Unit.Storage;

public sealed class AppenderTests : IAsyncDisposable
{
    private readonly string _dbPath;

    public AppenderTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".db");
    }

    public async ValueTask DisposeAsync()
    {
        await Task.CompletedTask;
        try { File.Delete(_dbPath); } catch { /* best-effort */ }
    }

    // ── helpers ───────────────────────────────────────────────────────────────

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

    // ── tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AppendEvent_1000Records_RoundTrip()
    {
        await using var writer = await DuckDbStorageWriter.CreateAsync(
            _dbPath, NullLogger<DuckDbStorageWriter>.Instance);

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
    }

    [Fact]
    public async Task AppendEvent_NullFields_StoredAsNull()
    {
        await using var writer = await DuckDbStorageWriter.CreateAsync(
            _dbPath, NullLogger<DuckDbStorageWriter>.Instance);

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
        await using var writer = await DuckDbStorageWriter.CreateAsync(
            _dbPath, NullLogger<DuckDbStorageWriter>.Instance);

        var fastState = MakeState(StateSampleRate.Fast);
        var act = async () => await writer.AppendStateAsync(fastState, default);

        await act.Should().ThrowAsync<NotSupportedException>();
    }

    [Fact]
    public async Task AppendBatch_MixedRecords_RoutesCorrectly()
    {
        await using var writer = await DuckDbStorageWriter.CreateAsync(
            _dbPath, NullLogger<DuckDbStorageWriter>.Instance);

        var batch = new List<DiagnosticRecord>
        {
            MakeEvent(1),
            MakeEvent(2),
            MakeState(StateSampleRate.Slow),
            MakeState(StateSampleRate.Fast),  // should be silently skipped
        };

        await writer.AppendBatchAsync(batch, default);
        await writer.FlushAsync(default);
        await writer.DisposeAsync();

        await using var reader = await DuckDbStorageReader.OpenAsync(
            _dbPath, NullLogger<DuckDbStorageReader>.Instance);

        var count = await reader.CountEventsAsync(EventFilter.All, default);
        count.Should().Be(2, "only event records should be counted in events table");
    }

    [Fact]
    public async Task Writer_DisposeAsync_IsIdempotent()
    {
        var writer = await DuckDbStorageWriter.CreateAsync(
            _dbPath, NullLogger<DuckDbStorageWriter>.Instance);

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
        await using var writer = await DuckDbStorageWriter.CreateAsync(
            _dbPath, NullLogger<DuckDbStorageWriter>.Instance);

        await writer.AppendEventAsync(MakeEvent(1), default);
        // No flush yet — data is buffered in the appender

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
