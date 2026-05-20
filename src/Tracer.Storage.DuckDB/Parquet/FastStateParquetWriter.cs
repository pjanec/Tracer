using Microsoft.Extensions.Logging;
using Parquet;
using Parquet.Data;
using Parquet.Schema;
using Tracer.Core.Records;

namespace Tracer.Storage.DuckDB.Parquet;

/// <summary>
/// Writes fast-state samples for a single topic to a Parquet file.
/// One writer is created per topic per interval.
/// </summary>
public sealed class FastStateParquetWriter : IAsyncDisposable
{
    private const int RowGroupFlushThreshold = 10_000;

    private readonly string _outputPath;
    private readonly ParquetTopicSchema _schema;
    private readonly ParquetSchema _parquetSchema;
    private readonly ILogger _logger;

    private Stream? _stream;
    private ParquetWriter? _writer;
    private readonly List<object?[]> _rowBuffer = new(RowGroupFlushThreshold);
    private long _totalRowsWritten;
    private bool _disposed;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private FastStateParquetWriter(
        string outputPath,
        ParquetTopicSchema schema,
        ILogger logger)
    {
        _outputPath = outputPath;
        _schema = schema;
        _parquetSchema = ParquetSchemas.BuildSchema(schema);
        _logger = logger;
    }

    /// <summary>
    /// Creates a new <see cref="FastStateParquetWriter"/> and initialises the Parquet file on disk.
    /// </summary>
    public static async Task<FastStateParquetWriter> CreateAsync(
        string outputPath,
        ParquetTopicSchema schema,
        ILogger logger,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(outputPath);
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(logger);
        ct.ThrowIfCancellationRequested();

        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var instance = new FastStateParquetWriter(outputPath, schema, logger);
        await instance.InitialiseAsync(ct).ConfigureAwait(false);
        return instance;
    }

    private async Task InitialiseAsync(CancellationToken ct)
    {
        _stream = File.Create(_outputPath);
        _writer = await ParquetWriter.CreateAsync(_parquetSchema, _stream, cancellationToken: ct)
            .ConfigureAwait(false);
        _logger.LogDebug("FastStateParquetWriter opened at {Path}", _outputPath);
    }

    /// <summary>
    /// Buffers a record; automatically flushes a row group when the buffer reaches threshold.
    /// </summary>
    public async Task AppendAsync(StateSampleRecord record, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();

        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _rowBuffer.Add(ColumnExtractor.ExtractRow(record, _schema));
            if (_rowBuffer.Count >= RowGroupFlushThreshold)
                await FlushRowGroupAsync().ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Total rows accepted via <see cref="AppendAsync"/> (flushed or buffered).
    /// </summary>
    public long TotalRowsWritten => Interlocked.Read(ref _totalRowsWritten);

    private async Task FlushRowGroupAsync()
    {
        // Called while _lock is held.
        if (_rowBuffer.Count == 0 || _writer is null)
            return;

        var fields = _parquetSchema.GetDataFields();
        using var rgWriter = _writer.CreateRowGroup();

        for (var fieldIdx = 0; fieldIdx < fields.Length; fieldIdx++)
        {
            var field = fields[fieldIdx];
            var values = BuildColumnArray(field, fieldIdx);
            await rgWriter.WriteColumnAsync(new DataColumn(field, values)).ConfigureAwait(false);
        }

        Interlocked.Add(ref _totalRowsWritten, _rowBuffer.Count);
        _rowBuffer.Clear();
        _logger.LogDebug("FastStateParquetWriter flushed row group to {Path}", _outputPath);
    }

    private Array BuildColumnArray(DataField field, int fieldIdx)
    {
        var count = _rowBuffer.Count;
        var elementType = field.ClrType;

        // Create strongly-typed array using the field's CLR type
        var arr = Array.CreateInstance(elementType, count);
        for (var i = 0; i < count; i++)
        {
            var val = _rowBuffer[i][fieldIdx];
            if (val is null)
            {
                // For nullable reference types (string), null is fine.
                // For value types the element default (0) is already set.
            }
            else
            {
                // Convert if the stored type doesn't exactly match (e.g. int vs float precision)
                try
                {
                    arr.SetValue(Convert.ChangeType(val, Nullable.GetUnderlyingType(elementType) ?? elementType), i);
                }
                catch
                {
                    // Use default on conversion failure
                }
            }
        }
        return arr;
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed) return;
            _disposed = true;

            if (_rowBuffer.Count > 0 && _writer is not null)
                await FlushRowGroupAsync().ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }

        if (_writer is not null)
        {
            _writer.Dispose();
            _writer = null;
        }

        if (_stream is not null)
        {
            await _stream.DisposeAsync().ConfigureAwait(false);
            _stream = null;
        }

        _lock.Dispose();
        _logger.LogDebug("FastStateParquetWriter disposed: {Path}, totalRows={Total}", _outputPath, _totalRowsWritten);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(FastStateParquetWriter));
    }
}
