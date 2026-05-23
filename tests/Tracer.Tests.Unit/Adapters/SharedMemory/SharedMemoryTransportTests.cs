using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Tracer.Adapters.SharedMemory;
using Tracer.Adapters.SharedMemory.Configuration;
using Tracer.Core.Abstractions;
using Tracer.Core.Domain;
using Tracer.Core.Identity;
using Tracer.Core.Records;
using Tracer.Core.Time;
using Xunit;

namespace Tracer.Tests.Unit.Adapters.SharedMemory;

public sealed class SharedMemoryTransportTests : IDisposable
{
    private readonly string _memName = $"test-shm-{Guid.NewGuid():N}";
    private readonly string _semName = $"test-sem-{Guid.NewGuid():N}";
    private const long Capacity = 1024 * 1024;  // 1 MB

    public void Dispose() { }

    private SharedMemoryConfig MakeConfig() => new()
    {
        SharedMemoryName = _memName,
        SemaphoreName = _semName,
        CapacityBytes = Capacity,
    };

    private static EventRecord MakeEventRecord(int i) => new()
    {
        SequenceNumber = (ulong)i,
        PublishWallclock = WallclockTime.Zero,
        ReceiveWallclock = WallclockTime.Zero,
        PublisherNode = new AgentId("pub"),
        SubscriberNode = new AgentId("sub"),
        Topic = new TopicName("topic.event"),
        EventId = new EventId((ulong)i),
        TraceId = new TraceId((ulong)i),
        PayloadJson = "{}",
    };

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ReadAsync_RecordsWrittenByWriter_AreYielded()
    {
        // Arrange: write 3 records via SharedMemoryWriter
        using var writer = new SharedMemoryWriter(_memName, _semName, Capacity);
        for (var i = 1; i <= 3; i++)
            writer.Write(MakeEventRecord(i));

        var transport = new SharedMemoryTransport(MakeConfig(),
            NullLogger<SharedMemoryTransport>.Instance);

        // Act: read until we have 3 records or timeout
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var received = new List<DiagnosticRecord>();

        await foreach (var record in transport.ReadAsync(cts.Token))
        {
            received.Add(record);
            if (received.Count >= 3) cts.Cancel();
        }

        // Assert
        received.Should().HaveCount(3);

        // Verify field-level round-trip through encode/decode
        received[0].Should().BeOfType<EventRecord>().Which.SequenceNumber.Should().Be(1UL);
        received[1].Should().BeOfType<EventRecord>().Which.SequenceNumber.Should().Be(2UL);
        received[2].Should().BeOfType<EventRecord>().Which.SequenceNumber.Should().Be(3UL);
        received.OfType<EventRecord>().Should().AllSatisfy(r =>
        {
            r.Topic.Should().Be(new TopicName("topic.event"));
            r.PublisherNode.Should().Be(new AgentId("pub"));
        });
    }

    [Fact]
    public void GetHealth_ReturnsTransportHealth()
    {
        var transport = new SharedMemoryTransport(MakeConfig(),
            NullLogger<SharedMemoryTransport>.Instance);

        var health = transport.GetHealth();

        health.Capacity.Should().Be((int)Capacity);
        health.TotalReceived.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public void GetHealth_Initially_ReturnsTotalDroppedZero()
    {
        var transport = new SharedMemoryTransport(MakeConfig(),
            NullLogger<SharedMemoryTransport>.Instance);

        var health = transport.GetHealth();

        health.TotalDropped.Should().Be(0L);
    }

    [Fact]
    public async Task ReadAsync_CancelledImmediately_DoesNotThrow()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var transport = new SharedMemoryTransport(
            new SharedMemoryConfig
            {
                SharedMemoryName = _memName,
                SemaphoreName = _semName,
                CapacityBytes = Capacity,
            },
            NullLogger<SharedMemoryTransport>.Instance);

        // Need to create the ring first so SharedMemoryReader doesn't fail to open.
        using var _ = SharedMemoryRingBuffer.Create(_memName, Capacity);
        using var __ = new Semaphore(0, int.MaxValue, _semName);

        var received = new List<DiagnosticRecord>();
        var act = async () =>
        {
            await foreach (var r in transport.ReadAsync(cts.Token))
                received.Add(r);
        };

        await act.Should().NotThrowAsync();
        received.Should().BeEmpty();
    }
}
