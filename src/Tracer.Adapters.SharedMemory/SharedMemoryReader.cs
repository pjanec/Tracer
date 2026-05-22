using System.Runtime.Versioning;
using Tracer.Core.Records;

namespace Tracer.Adapters.SharedMemory;

/// <summary>
/// Consumer-side helper. Waits on a named semaphore and drains available records
/// from the shared memory ring buffer.
/// </summary>
public sealed class SharedMemoryReader : IDisposable
{
    private readonly SharedMemoryRingBuffer _buffer;
    private readonly SharedMemoryDiagnosticRecordCodec _codec;
    private readonly Semaphore _semaphore;

    [SupportedOSPlatform("windows")]
    public SharedMemoryReader(string name, string semaphoreName)
    {
        _buffer = SharedMemoryRingBuffer.Open(name);
        _codec = new SharedMemoryDiagnosticRecordCodec();
        _semaphore = Semaphore.OpenExisting(semaphoreName);
    }

    /// <summary>Drains all currently available records without blocking.</summary>
    public IEnumerable<DiagnosticRecord> ReadAvailable()
    {
        while (true)
        {
            var bytes = _buffer.TryRead();
            if (bytes is null) yield break;
            var record = _codec.Decode(bytes);
            if (record is not null)
                yield return record;
        }
    }

    /// <summary>
    /// Waits up to <paramref name="timeout"/> for data, then drains all available records.
    /// Returns an empty enumerable on timeout.
    /// </summary>
    public IEnumerable<DiagnosticRecord> WaitAndRead(TimeSpan timeout)
    {
        _semaphore.WaitOne(timeout);
        return ReadAvailable();
    }

    /// <summary>Returns the cumulative number of records dropped by the ring buffer.</summary>
    public long GetDroppedCount() => _buffer.GetDroppedCount();

    public void Dispose()
    {
        _buffer.Dispose();
        _semaphore.Dispose();
    }
}
