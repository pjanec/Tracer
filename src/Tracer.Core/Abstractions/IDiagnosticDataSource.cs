using Tracer.Core.Records;

namespace Tracer.Core.Abstractions;

/// <summary>
/// A source of diagnostic records. Implementations include DDS subscribers
/// (production) and mock scenario generators (development/test).
/// </summary>
public interface IDiagnosticDataSource
{
    /// <summary>Streams all diagnostic records from this source.</summary>
    IAsyncEnumerable<DiagnosticRecord> ReadAsync(CancellationToken ct);
}
