using Tracer.Core.Records;

namespace Tracer.Core.Abstractions;

/// <summary>
/// Writes diagnostic records to durable storage.
/// </summary>
public interface IDiagnosticStorageWriter : IAsyncDisposable
{
    /// <summary>Appends a single event record.</summary>
    Task AppendEventAsync(EventRecord record, CancellationToken ct);

    /// <summary>Appends a single slow-state sample record.</summary>
    Task AppendStateAsync(StateSampleRecord record, CancellationToken ct);

    /// <summary>Appends a single fast-state sample record to Parquet storage.</summary>
    Task AppendFastStateAsync(StateSampleRecord record, CancellationToken ct);

    /// <summary>Appends a batch of diagnostic records, routing each to the correct table.</summary>
    Task AppendBatchAsync(IReadOnlyList<DiagnosticRecord> records, CancellationToken ct);

    /// <summary>Flushes all buffered writes, making them visible to readers.</summary>
    Task FlushAsync(CancellationToken ct);
}
