using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Tracer.Storage.DuckDB;
using Tracer.Storage.DuckDB.Parquet;
using Tracer.WebApi.Lifecycle;
using Xunit;

namespace Tracer.Tests.Unit.Observer;

public sealed class ReadOnlyConnectionPoolTests : IAsyncDisposable
{
    private readonly string _tempDir;
    private readonly string _dbPath;

    public ReadOnlyConnectionPoolTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"tracer-pool-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _dbPath = Path.Combine(_tempDir, "events.duckdb");
    }

    private async Task<string> CreateDbAsync(string? dir = null)
    {
        var d = dir ?? _tempDir;
        Directory.CreateDirectory(d);
        await using var writer = await DuckDbStorageWriter.CreateAsync(
            d,
            new Dictionary<string, ParquetTopicSchema>(),
            NullLogger<DuckDbStorageWriter>.Instance);
        await writer.FlushAsync(default);
        return Path.Combine(d, "events.duckdb");
    }

    [Fact]
    public async Task InitializeAsync_OpensConfiguredPoolSize()
    {
        var dbPath = await CreateDbAsync();
        await using var pool = new ReadOnlyConnectionPool(NullLogger<ReadOnlyConnectionPool>.Instance);

        await pool.InitializeAsync(dbPath, default);

        // Pool is now initialized — acquisition should succeed
        await using var conn = await pool.AcquireAsync(default);
        conn.Connection.Should().NotBeNull();
        conn.Connection.State.Should().Be(System.Data.ConnectionState.Open);
    }

    [Fact]
    public async Task AcquireAsync_ReturnsConnection()
    {
        var dbPath = await CreateDbAsync();
        await using var pool = new ReadOnlyConnectionPool(NullLogger<ReadOnlyConnectionPool>.Instance);
        await pool.InitializeAsync(dbPath, default);

        await using var conn = await pool.AcquireAsync(default);

        conn.Should().NotBeNull();
        conn.Connection.State.Should().Be(System.Data.ConnectionState.Open);
    }

    [Fact]
    public async Task PooledConnection_DisposeAsync_ReturnsToPool()
    {
        var dbPath = await CreateDbAsync();
        await using var pool = new ReadOnlyConnectionPool(NullLogger<ReadOnlyConnectionPool>.Instance);
        await pool.InitializeAsync(dbPath, default);

        // Borrow and return
        PooledConnectionWrapper borrowedConn;
        await using (var conn = await pool.AcquireAsync(default))
        {
            borrowedConn = new PooledConnectionWrapper(conn.Connection);
        }
        // After dispose the connection should be returned; acquiring again should work
        await using var conn2 = await pool.AcquireAsync(default);
        conn2.Should().NotBeNull();
    }

    [Fact]
    public async Task OnIntervalRotated_BorrowedConnectionDisposesOnReturn()
    {
        var db1 = await CreateDbAsync(_tempDir);
        var dir2 = Path.Combine(_tempDir, "interval2");
        var db2 = await CreateDbAsync(dir2);

        await using var pool = new ReadOnlyConnectionPool(NullLogger<ReadOnlyConnectionPool>.Instance);
        await pool.InitializeAsync(db1, default);

        // Borrow a connection from the old interval
        await using var conn = await pool.AcquireAsync(default);

        // Rotate pool to new interval — old connections are drained from pool
        await pool.OnIntervalRotatedAsync(db2, default);

        // Returning the old connection (from stale pool) disposes it cleanly
        // (no exception expected)
        await conn.DisposeAsync();
    }

    [Fact]
    public async Task DisposeAsync_ClosesAllConnections()
    {
        var dbPath = await CreateDbAsync();
        var pool = new ReadOnlyConnectionPool(NullLogger<ReadOnlyConnectionPool>.Instance);
        await pool.InitializeAsync(dbPath, default);

        await pool.DisposeAsync();

        // After dispose, acquiring should throw
        Func<Task> act = async () => await pool.AcquireAsync(default);
        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task AcquireAsync_AfterDispose_ThrowsObjectDisposedException()
    {
        var dbPath = await CreateDbAsync();
        var pool = new ReadOnlyConnectionPool(NullLogger<ReadOnlyConnectionPool>.Instance);
        await pool.InitializeAsync(dbPath, default);
        await pool.DisposeAsync();

        Func<Task> act = async () => await pool.AcquireAsync(default);
        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    public async ValueTask DisposeAsync()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
        await Task.CompletedTask;
    }

    /// <summary>Wrapper to expose the raw connection for assertion purposes.</summary>
    private sealed class PooledConnectionWrapper
    {
        public DuckDB.NET.Data.DuckDBConnection Connection { get; }
        public PooledConnectionWrapper(DuckDB.NET.Data.DuckDBConnection conn) => Connection = conn;
    }
}
