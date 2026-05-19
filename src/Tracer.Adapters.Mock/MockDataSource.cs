using System.Runtime.CompilerServices;
using Tracer.Core.Abstractions;
using Tracer.Core.Records;

namespace Tracer.Adapters.Mock;

/// <summary>
/// A data source that emits pre-configured diagnostic records for testing.
/// </summary>
public sealed class MockDataSource : IDiagnosticDataSource
{
    private readonly IReadOnlyList<DiagnosticRecord> _records;

    /// <summary>Initialises the source with a fixed set of records.</summary>
    public MockDataSource(IReadOnlyList<DiagnosticRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        _records = records;
    }

    /// <summary>Initialises an empty source.</summary>
    public MockDataSource() : this(Array.Empty<DiagnosticRecord>()) { }

    /// <inheritdoc/>
    public async IAsyncEnumerable<DiagnosticRecord> ReadAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var record in _records)
        {
            ct.ThrowIfCancellationRequested();
            yield return record;
            await Task.Yield();
        }
    }
}
