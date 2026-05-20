using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Parquet;
using Tracer.Core.Domain;
using Tracer.Core.Identity;
using Tracer.Core.Records;
using Tracer.Core.Time;
using Tracer.Storage.DuckDB;
using Tracer.Storage.DuckDB.Parquet;
using Xunit;

namespace Tracer.Tests.Unit.Storage;

public sealed class FastStateParquetWriterTests : IAsyncDisposable
{
    private readonly string _tempDir;

    public FastStateParquetWriterTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"tracer-fsw-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public async ValueTask DisposeAsync()
    {
        await Task.CompletedTask;
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort */ }
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static WallclockTime T(int secondsOffset = 0) =>
        new WallclockTime(1_700_000_000_000_000_000L + (long)secondsOffset * 1_000_000_000L);

    private static StateSampleRecord MakeFastSample(string instanceKey = "e1", string payloadJson = "{}") =>
        new StateSampleRecord
        {
            SequenceNumber = 1,
            PublishWallclock = T(0),
            ReceiveWallclock = T(1),
            PublisherNode = new AgentId("pub"),
            SubscriberNode = new AgentId("sub"),
            Topic = new TopicName("topic.transforms"),
            InstanceKey = instanceKey,
            Rate = StateSampleRate.Fast,
            PayloadJson = payloadJson,
        };

    // ── tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_FileExistsOnDisk()
    {
        var path = Path.Combine(_tempDir, "test.parquet");
        var schema = WellKnownTopicSchemas.Transforms;

        await using var writer = await FastStateParquetWriter.CreateAsync(
            path, schema, NullLogger.Instance);

        File.Exists(path).Should().BeTrue("file should be created on disk immediately");
    }

    [Fact]
    public async Task Append100_DisposeAsync_TotalRowsIs100()
    {
        var path = Path.Combine(_tempDir, "rows.parquet");
        var schema = WellKnownTopicSchemas.Transforms;

        var writer = await FastStateParquetWriter.CreateAsync(
            path, schema, NullLogger.Instance);

        for (var i = 0; i < 100; i++)
            await writer.AppendAsync(MakeFastSample(), default);

        await writer.DisposeAsync();

        writer.TotalRowsWritten.Should().Be(100);
    }

    [Fact]
    public async Task DisposeAsync_IsIdempotent()
    {
        var path = Path.Combine(_tempDir, "idempotent.parquet");
        var schema = WellKnownTopicSchemas.Transforms;

        var writer = await FastStateParquetWriter.CreateAsync(
            path, schema, NullLogger.Instance);

        await writer.AppendAsync(MakeFastSample(), default);

        var act = async () =>
        {
            await writer.DisposeAsync();
            await writer.DisposeAsync();
        };

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public void ColumnExtractor_KnownJsonPath_ExtractsCorrectValue()
    {
        var schema = WellKnownTopicSchemas.Transforms;
        var record = MakeFastSample(payloadJson: """{"position":{"x":1.5,"y":-2.0,"z":0.0},"orientation":{"w":1.0,"x":0.0,"y":0.0,"z":0.0}}""");

        var row = ColumnExtractor.ExtractRow(record, schema);

        // Standard cols: publish_wallclock, receive_wallclock, publisher_node, instance_key, sequence_number
        row[2].Should().Be("pub");   // publisher_node
        row[3].Should().Be("e1");    // instance_key

        // Schema cols start at index 5
        // pos_x at index 5 (first schema column is Transforms.Columns[0])
        var posX = (float?)row[5];
        posX.Should().BeApproximately(1.5f, 0.001f);
    }

    [Fact]
    public async Task DuckDbStorageWriter_AppendFastStateAsync_NonFastRate_ThrowsArgumentException()
    {
        var intervalDir = Path.Combine(_tempDir, "interval");

        await using var writer = await DuckDbStorageWriter.CreateAsync(
            intervalDir,
            WellKnownTopicSchemas.ToDictionary(),
            NullLogger<DuckDbStorageWriter>.Instance);

        var slowRecord = new StateSampleRecord
        {
            SequenceNumber = 1,
            PublishWallclock = T(0),
            ReceiveWallclock = T(1),
            PublisherNode = new AgentId("pub"),
            SubscriberNode = new AgentId("sub"),
            Topic = new TopicName("topic.transforms"),
            InstanceKey = "e1",
            Rate = StateSampleRate.Slow,
            PayloadJson = "{}",
        };

        var act = async () => await writer.AppendFastStateAsync(slowRecord, default);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*fast-rate*");
    }
}
