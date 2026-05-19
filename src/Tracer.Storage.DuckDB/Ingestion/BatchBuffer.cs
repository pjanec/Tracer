using Tracer.Core.Records;

namespace Tracer.Storage.DuckDB.Ingestion;

/// <summary>
/// Buffers diagnostic records before a batch flush to the appender.
/// </summary>
internal sealed class BatchBuffer
{
    private const int InitialCapacity = 256;

    private readonly int _maxRecords;
    private readonly TimeSpan _maxAge;
    private readonly List<DiagnosticRecord> _records;
    private DateTime _firstAddedAt;

    /// <summary>
    /// Constructs a <see cref="BatchBuffer"/>.
    /// </summary>
    /// <param name="maxRecords">Flush when this many records are buffered.</param>
    /// <param name="maxAge">Flush when the oldest record has been buffered this long.</param>
    public BatchBuffer(int maxRecords, TimeSpan maxAge)
    {
        _maxRecords = maxRecords;
        _maxAge = maxAge;
        _records = new List<DiagnosticRecord>(InitialCapacity);
    }

    /// <summary>
    /// Returns true if the buffer has reached its record or age threshold.
    /// </summary>
    public bool ShouldFlush =>
        _records.Count >= _maxRecords
        || (_records.Count > 0 && DateTime.UtcNow - _firstAddedAt >= _maxAge);

    /// <summary>Adds a record to the buffer.</summary>
    public void Add(DiagnosticRecord record)
    {
        if (_records.Count == 0)
            _firstAddedAt = DateTime.UtcNow;
        _records.Add(record);
    }

    /// <summary>
    /// Returns all buffered records and resets the buffer to empty.
    /// </summary>
    public IReadOnlyList<DiagnosticRecord> DrainAll()
    {
        var copy = _records.ToArray();
        _records.Clear();
        return copy;
    }
}
