using Microsoft.Extensions.Logging.Abstractions;
using Tracer.Adapters.Mock;
using Tracer.Adapters.Mock.Scenarios;
using Tracer.Core.Abstractions;
using Tracer.Core.Records;
using Tracer.Storage.DuckDB;
using Tracer.Storage.DuckDB.Parquet;

namespace Tracer.TestHarness;

/// <summary>
/// Integration-test scaffolding that wires
/// <see cref="MockDataSource"/> → <see cref="DuckDbStorageWriter"/> → <see cref="DuckDbStorageReader"/>
/// into a single disposable unit backed by a temporary directory.
/// </summary>
public sealed class TracerStackFixture : IAsyncDisposable
{
    public MockDataSource DataSource { get; private set; } = null!;
    public DuckDbStorageWriter Writer { get; private set; } = null!;

    /// <summary>Non-null after <see cref="RunScenarioAsync"/> completes.</summary>
    public IDiagnosticStorageReader? Reader { get; private set; }

    /// <summary>Absolute path to the DuckDB file inside the temp directory.</summary>
    public string DbPath { get; private set; } = null!;

    /// <summary>Count of <see cref="EventRecord"/> instances written during the last <see cref="RunScenarioAsync"/>.</summary>
    public long EventsWrittenCount { get; private set; }

    private string _tempDir = null!;
    private bool _disposed;

    // ── factory ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a fixture backed by a fresh temp directory.
    /// Does NOT run the scenario — call <see cref="RunScenarioAsync"/> to do that.
    /// </summary>
    public static async Task<TracerStackFixture> CreateAsync(
        string scenarioName,
        int seed = 42,
        TimeSpan? duration = null,
        InMemoryStackOptions? options = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(scenarioName);
        options ??= new InMemoryStackOptions();

        var fixture = new TracerStackFixture();
        fixture._tempDir = Path.Combine(Path.GetTempPath(), $"tracer-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(fixture._tempDir);

        fixture.DbPath = Path.Combine(fixture._tempDir, "events.duckdb");

        var config = new ScenarioConfig
        {
            Seed = seed,
            Duration = duration ?? TimeSpan.FromMinutes(5),
            NodeCount = options.NodeCount,
            EntityCount = options.EntityCount,
            EventsPerSecond = options.EventsPerSecond,
        };

        fixture.DataSource = new MockDataSource(scenarioName, config);
        fixture.Writer = await DuckDbStorageWriter.CreateAsync(
            fixture._tempDir,
            new Dictionary<string, ParquetTopicSchema>(),
            NullLogger<DuckDbStorageWriter>.Instance,
            ct).ConfigureAwait(false);

        return fixture;
    }

    // ── scenario execution ──────────────────────────────────────────────────

    /// <summary>
    /// Iterates <see cref="DataSource"/>, writes every record, flushes the writer,
    /// then opens a read-only <see cref="DuckDbStorageReader"/> on the same file.
    /// </summary>
    public async Task RunScenarioAsync(CancellationToken ct = default)
    {
        long count = 0;
        await foreach (var record in DataSource.ReadAsync(ct).ConfigureAwait(false))
        {
            switch (record)
            {
                case EventRecord ev:
                    await Writer.AppendEventAsync(ev, ct).ConfigureAwait(false);
                    count++;
                    break;
                case StateSampleRecord state when state.Rate == StateSampleRate.Slow:
                    await Writer.AppendStateAsync(state, ct).ConfigureAwait(false);
                    break;
            }
        }

        EventsWrittenCount = count;
        await Writer.FlushAsync(ct).ConfigureAwait(false);

        Reader = await DuckDbStorageReader.OpenAsync(
            DbPath,
            NullLogger<DuckDbStorageReader>.Instance,
            ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Closes the current reader (if any) and reopens a fresh read-only reader
    /// against the same database file. Useful for verifying persistence across
    /// writer/reader lifecycle boundaries.
    /// </summary>
    public async Task ReopenReaderAsync(CancellationToken ct = default)
    {
        if (Reader is not null)
        {
            await Reader.DisposeAsync().ConfigureAwait(false);
            Reader = null;
        }

        Reader = await DuckDbStorageReader.OpenAsync(
            DbPath,
            NullLogger<DuckDbStorageReader>.Instance,
            ct).ConfigureAwait(false);
    }

    // ── disposal ────────────────────────────────────────────────────────────

    /// <summary>
    /// Closes the reader and writer, then deletes the temp directory.
    /// Safe to call multiple times — subsequent calls are no-ops.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        if (Reader is not null)
        {
            await Reader.DisposeAsync().ConfigureAwait(false);
            Reader = null;
        }

        if (Writer is not null)
            await Writer.DisposeAsync().ConfigureAwait(false);

        try
        {
            if (_tempDir is not null && Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
            // Best-effort cleanup; ignore errors.
        }
    }
}

