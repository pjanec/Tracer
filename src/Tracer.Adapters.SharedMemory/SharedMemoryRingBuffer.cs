using System.IO.MemoryMappedFiles;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace Tracer.Adapters.SharedMemory;

/// <summary>
/// Single-producer, single-consumer ring buffer backed by a named shared memory region.
/// Implements drop-oldest back-pressure: the producer never blocks.
/// </summary>
public sealed unsafe class SharedMemoryRingBuffer : IDisposable
{
    // Header layout (fixed offsets from start of mapping):
    //  [0 ..  7]  magic "TRCRSHM\0"  (8 bytes)
    //  [8 .. 11]  version = 1         (int32)
    // [12 .. 19]  capacity            (int64)
    // [20 .. 27]  write_offset        (int64, volatile)
    // [28 .. 35]  read_offset         (int64, volatile)
    // [36 .. 39]  producer_pid        (int32)
    // [40 .. 43]  consumer_pid        (int32)
    // [44 .. 51]  producer_heartbeat  (int64)
    // [52 .. 59]  consumer_heartbeat  (int64)
    // [60 .. 67]  dropped_count       (int64, volatile)
    // [68..4095]  reserved / padding
    private const int HeaderSize = 4096;
    private const string Magic = "TRCRSHM\0";

    private const int OffsetVersion = 8;
    private const int OffsetCapacity = 12;
    private const int OffsetWriteOffset = 20;
    private const int OffsetReadOffset = 28;
    private const int OffsetProducerPid = 36;
    private const int OffsetConsumerPid = 40;
    private const int OffsetDroppedCount = 60;

    private readonly MemoryMappedFile _mmf;
    private readonly MemoryMappedViewAccessor _accessor;
    private readonly long _capacity;
    private readonly bool _isProducer;
    private readonly byte* _basePtr;
    private bool _disposed;

    private SharedMemoryRingBuffer(
        MemoryMappedFile mmf,
        MemoryMappedViewAccessor accessor,
        long capacity,
        bool isProducer)
    {
        _mmf = mmf;
        _accessor = accessor;
        _capacity = capacity;
        _isProducer = isProducer;

        byte* ptr = null;
        accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref ptr);
        _basePtr = ptr;
    }

    /// <summary>Creates and initialises a new named shared memory ring buffer (producer side).</summary>
    [SupportedOSPlatform("windows")]
    public static SharedMemoryRingBuffer Create(string name, long capacity)
    {
        var totalSize = HeaderSize + capacity;
        var mmf = MemoryMappedFile.CreateOrOpen(name, totalSize);
        var accessor = mmf.CreateViewAccessor(0, totalSize);

        var magic = Encoding.ASCII.GetBytes(Magic);
        accessor.WriteArray(0, magic, 0, magic.Length);
        accessor.Write(OffsetVersion, 1);
        accessor.Write(OffsetCapacity, capacity);
        accessor.Write(OffsetWriteOffset, 0L);
        accessor.Write(OffsetReadOffset, 0L);
        accessor.Write(OffsetProducerPid, Environment.ProcessId);
        accessor.Write(OffsetConsumerPid, 0);
        accessor.Write(OffsetDroppedCount, 0L);

        return new SharedMemoryRingBuffer(mmf, accessor, capacity, isProducer: true);
    }

    /// <summary>Opens an existing named shared memory ring buffer (consumer side).</summary>
    [SupportedOSPlatform("windows")]
    public static SharedMemoryRingBuffer Open(string name)
    {
        var mmf = MemoryMappedFile.OpenExisting(name);

        // Read header to get capacity
        using var headerAccessor = mmf.CreateViewAccessor(0, HeaderSize);
        var magic = new byte[8];
        headerAccessor.ReadArray(0, magic, 0, 8);
        if (Encoding.ASCII.GetString(magic) != Magic)
            throw new InvalidOperationException("Shared memory region has invalid magic bytes.");
        var capacity = headerAccessor.ReadInt64(OffsetCapacity);

        var accessor = mmf.CreateViewAccessor(0, HeaderSize + capacity);
        accessor.Write(OffsetConsumerPid, Environment.ProcessId);

        return new SharedMemoryRingBuffer(mmf, accessor, capacity, isProducer: false);
    }

    /// <summary>
    /// Producer: writes record bytes to the buffer.
    /// Uses drop-oldest policy when the buffer is full.
    /// Returns <c>false</c> if the record is too large to ever fit.
    /// </summary>
    public bool TryWrite(ReadOnlySpan<byte> record)
    {
        if (!_isProducer) throw new InvalidOperationException("Cannot write from consumer side.");
        long required = record.Length + 4L;
        if (required > _capacity) return false;

        var writeOff = ReadAtomicLong(OffsetWriteOffset);
        var readOff = ReadAtomicLong(OffsetReadOffset);

        // Check if we need to wrap before writing
        if (writeOff + required > _capacity)
        {
            // Write padding marker if there is room for 4 bytes
            if (_capacity - writeOff >= 4)
                WriteLengthAt(writeOff, 0);
            writeOff = 0L;
        }

        // Drop-oldest: advance readOff until there is room
        while (FreeSpace(writeOff, readOff) < required)
        {
            readOff = AdvancePastRecord(readOff);
            IncrementDropped();
        }

        // Commit dropped read advances
        WriteAtomicLong(OffsetReadOffset, readOff);

        // Write the record
        WriteLengthAt(writeOff, record.Length);
        fixed (byte* src = record)
            Buffer.MemoryCopy(src, _basePtr + HeaderSize + writeOff + 4, record.Length, record.Length);

        var newWriteOff = writeOff + required;
        if (newWriteOff >= _capacity) newWriteOff = 0;
        WriteAtomicLong(OffsetWriteOffset, newWriteOff);

        return true;
    }

    /// <summary>
    /// Consumer: reads and removes the next record.
    /// Returns <c>null</c> if the buffer is empty.
    /// </summary>
    public byte[]? TryRead()
    {
        if (_isProducer) throw new InvalidOperationException("Cannot read from producer side.");

    retry:
        var writeOff = ReadAtomicLong(OffsetWriteOffset);
        var readOff = ReadAtomicLong(OffsetReadOffset);

        if (writeOff == readOff) return null;

        // Handle case where less than 4 bytes remain before capacity boundary
        if (readOff + 4 > _capacity)
        {
            WriteAtomicLong(OffsetReadOffset, 0L);
            goto retry;
        }

        var length = ReadLengthAt(readOff);
        if (length == 0)
        {
            // Padding marker: wrap to start
            WriteAtomicLong(OffsetReadOffset, 0L);
            goto retry;
        }

        var result = new byte[length];
        fixed (byte* dst = result)
            Buffer.MemoryCopy(_basePtr + HeaderSize + readOff + 4, dst, length, length);

        var newReadOff = readOff + 4 + length;
        if (newReadOff >= _capacity) newReadOff = 0;
        WriteAtomicLong(OffsetReadOffset, newReadOff);

        return result;
    }

    /// <summary>Returns the cumulative number of records dropped due to overflow.</summary>
    public long GetDroppedCount() => ReadAtomicLong(OffsetDroppedCount);

    private long FreeSpace(long write, long read)
    {
        var used = write >= read ? write - read : _capacity - read + write;
        return _capacity - used;
    }

    private long AdvancePastRecord(long readOff)
    {
        if (readOff + 4 > _capacity) return 0L;
        var len = ReadLengthAt(readOff);
        if (len == 0) return 0L;
        var next = readOff + 4 + len;
        return next >= _capacity ? 0L : next;
    }

    private void IncrementDropped()
    {
        ref long dropped = ref Unsafe.AsRef<long>(_basePtr + OffsetDroppedCount);
        Interlocked.Increment(ref dropped);
    }

    private long ReadAtomicLong(int offset) =>
        Volatile.Read(ref Unsafe.AsRef<long>(_basePtr + offset));

    private void WriteAtomicLong(int offset, long value) =>
        Volatile.Write(ref Unsafe.AsRef<long>(_basePtr + offset), value);

    private int ReadLengthAt(long bufferOffset) =>
        Volatile.Read(ref Unsafe.AsRef<int>(_basePtr + HeaderSize + bufferOffset));

    private void WriteLengthAt(long bufferOffset, int length) =>
        Volatile.Write(ref Unsafe.AsRef<int>(_basePtr + HeaderSize + bufferOffset), length);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _accessor.SafeMemoryMappedViewHandle.ReleasePointer();
        _accessor.Dispose();
        _mmf.Dispose();
    }
}
