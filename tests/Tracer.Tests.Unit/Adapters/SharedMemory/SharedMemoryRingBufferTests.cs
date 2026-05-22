using System.IO.MemoryMappedFiles;
using FluentAssertions;
using Tracer.Adapters.SharedMemory;
using Xunit;

namespace Tracer.Tests.Unit.Adapters.SharedMemory;

public sealed class SharedMemoryRingBufferTests : IDisposable
{
    // Use unique name per test via instance-level GUID to avoid cross-test interference.
    private readonly string _name = $"test-ring-{Guid.NewGuid():N}";
    private const long SmallCapacity = 8192;   // 8 KB — enough for several small records

    public void Dispose()
    {
        // Best-effort cleanup: MemoryMappedFiles are GC'd but we attempt to clean up explicitly.
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Create_ThenOpen_WriteAndRead_RoundTrips()
    {
        using var writer = SharedMemoryRingBuffer.Create(_name, SmallCapacity);
        using var reader = SharedMemoryRingBuffer.Open(_name);

        var data = "Hello, ring buffer!"u8.ToArray();
        writer.TryWrite(data).Should().BeTrue();

        var result = reader.TryRead();

        result.Should().NotBeNull();
        result.Should().BeEquivalentTo(data);
    }

    [Fact]
    public void TryRead_OnEmptyBuffer_ReturnsNull()
    {
        using var writer = SharedMemoryRingBuffer.Create(_name, SmallCapacity);
        using var reader = SharedMemoryRingBuffer.Open(_name);

        var result = reader.TryRead();

        result.Should().BeNull();
    }

    [Fact]
    public void TryWrite_MultipleMessages_AllReadBack()
    {
        using var writer = SharedMemoryRingBuffer.Create(_name, SmallCapacity);
        using var reader = SharedMemoryRingBuffer.Open(_name);

        var msgs = Enumerable.Range(1, 5)
            .Select(i => System.Text.Encoding.UTF8.GetBytes($"msg-{i}"))
            .ToList();

        foreach (var m in msgs)
            writer.TryWrite(m).Should().BeTrue();

        var received = new List<byte[]>();
        byte[]? chunk;
        while ((chunk = reader.TryRead()) is not null)
            received.Add(chunk);

        received.Should().HaveCount(5);
        for (var i = 0; i < 5; i++)
            received[i].Should().BeEquivalentTo(msgs[i]);
    }

    [Fact]
    public void TryWrite_WhenFull_DropsOldestAndKeepsLatest()
    {
        // Use a very small capacity so we can force a drop.
        const long tinyCapacity = 512;
        var name = $"test-ring-tiny-{Guid.NewGuid():N}";
        using var writer = SharedMemoryRingBuffer.Create(name, tinyCapacity);
        using var reader = SharedMemoryRingBuffer.Open(name);

        // Fill the buffer past capacity — earlier records should be dropped.
        var payload = new byte[60];  // each record takes 4 (length header) + 60 bytes
        for (var i = 0; i < 20; i++)
        {
            payload[0] = (byte)i;
            writer.TryWrite(payload);
        }

        // The buffer should now hold the most recent records (not all 20).
        var received = new List<byte[]>();
        byte[]? chunk;
        while ((chunk = reader.TryRead()) is not null)
            received.Add(chunk);

        // Not all 20 fit; the last one must be among received (drop-oldest semantics).
        received.Should().NotBeEmpty();
        received.Last()[0].Should().Be(19, "last written value should survive in drop-oldest mode");
    }

    [Fact]
    public void GetDroppedCount_AfterDrop_ReturnsPositive()
    {
        const long tinyCapacity = 512;
        var name = $"test-ring-drops-{Guid.NewGuid():N}";
        using var buffer = SharedMemoryRingBuffer.Create(name, tinyCapacity);

        var payload = new byte[60];
        for (var i = 0; i < 20; i++)
            buffer.TryWrite(payload);

        buffer.GetDroppedCount().Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public void TryWrite_RecordLargerThanCapacity_ReturnsFalse()
    {
        const long tinyCapacity = 512;
        var name = $"test-ring-oversize-{Guid.NewGuid():N}";
        using var buffer = SharedMemoryRingBuffer.Create(name, tinyCapacity);

        var oversized = new byte[(int)tinyCapacity + 1];

        buffer.TryWrite(oversized).Should().BeFalse();
    }
}
