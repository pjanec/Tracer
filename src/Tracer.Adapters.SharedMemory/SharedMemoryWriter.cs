using System.Runtime.Versioning;
using Tracer.Core.Records;

namespace Tracer.Adapters.SharedMemory;

/// <summary>
/// Producer-side helper. Encodes <see cref="DiagnosticRecord"/> instances and writes them
/// to the shared memory ring buffer, signalling a named semaphore on each write.
/// </summary>
public sealed class SharedMemoryWriter : IDisposable
{
    private readonly SharedMemoryRingBuffer _buffer;
    private readonly SharedMemoryDiagnosticRecordCodec _codec;
    private readonly Semaphore _semaphore;

    [SupportedOSPlatform("windows")]
    public SharedMemoryWriter(string name, string semaphoreName, long capacity)
    {
        _buffer = SharedMemoryRingBuffer.Create(name, capacity);
        _codec = new SharedMemoryDiagnosticRecordCodec();
        _semaphore = new Semaphore(0, int.MaxValue, semaphoreName);
    }

    /// <summary>
    /// Encodes and writes a record to the ring buffer.
    /// Signals the consumer semaphore if the write succeeded.
    /// Returns <c>false</c> only if the record is too large for the buffer at all.
    /// </summary>
    public bool Write(DiagnosticRecord record)
    {
        var bytes = _codec.Encode(record);
        var written = _buffer.TryWrite(bytes);
        if (written)
            _semaphore.Release(1);
        return written;
    }

    /// <summary>Returns the cumulative number of records dropped by the ring buffer.</summary>
    public long GetDroppedCount() => _buffer.GetDroppedCount();

    public void Dispose()
    {
        _buffer.Dispose();
        _semaphore.Dispose();
    }
}
