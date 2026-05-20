using Microsoft.Extensions.Logging;
using Tracer.Core.Records;

namespace Tracer.Storage.DuckDB.Parquet;

/// <summary>
/// A no-op fast-state writer used for topics with no registered schema.
/// Silently drops all samples.
/// </summary>
internal sealed class NullFastStateWriter : IAsyncDisposable
{
    public static readonly NullFastStateWriter Instance = new();

    private NullFastStateWriter() { }

    /// <summary>Accepts the record and discards it.</summary>
    public Task AppendAsync(StateSampleRecord record, CancellationToken ct = default)
        => Task.CompletedTask;

    /// <summary>No-op disposal.</summary>
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
