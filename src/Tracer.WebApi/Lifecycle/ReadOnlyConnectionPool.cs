using System.Threading.Channels;
using DuckDB.NET.Data;
using Microsoft.Extensions.Logging;

namespace Tracer.WebApi.Lifecycle;

/// <summary>
/// A pool of read-only DuckDB connections for serving HTTP queries.
/// Rotation-aware: when the active interval changes, the pool rebuilds its
/// connection set against the new path.
/// Not sealed to allow test subclasses to override rotation behavior.
/// </summary>
public class ReadOnlyConnectionPool : IAsyncDisposable
{
    private readonly ILogger<ReadOnlyConnectionPool> _logger;
    private readonly int _poolSize;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    private Channel<DuckDBConnection>? _connections;
    private string? _currentDbPath;
    private bool _disposed;

    public ReadOnlyConnectionPool(ILogger<ReadOnlyConnectionPool> logger)
    {
        _logger = logger;
        _poolSize = 8;
    }

    /// <summary>
    /// Initial pool open against the current interval's events.duckdb.
    /// Called by ObserverHostedService after the first interval is opened.
    /// </summary>
    public virtual async Task InitializeAsync(string activeIntervalDbPath, CancellationToken ct)
    {
        await SwitchToAsync(activeIntervalDbPath, ct);
    }

    /// <summary>
    /// Called by ObserverHostedService when IntervalRotator has switched to a new interval.
    /// Drains and disposes the old pool, builds a fresh pool against the new path.
    /// </summary>
    public virtual async Task OnIntervalRotatedAsync(string newActiveDbPath, CancellationToken ct)
    {
        await SwitchToAsync(newActiveDbPath, ct);
    }

    private async Task SwitchToAsync(string newPath, CancellationToken ct)
    {
        await _refreshLock.WaitAsync(ct);
        try
        {
            if (_currentDbPath == newPath) return;

            _logger.LogInformation(
                "ReadOnlyConnectionPool switching from {Old} to {New}",
                _currentDbPath ?? "<none>", newPath);

            var old = _connections;
            _connections = Channel.CreateBounded<DuckDBConnection>(_poolSize);

            if (old is not null)
            {
                old.Writer.TryComplete();
                while (old.Reader.TryRead(out var conn))
                {
                    try { await conn.DisposeAsync(); } catch { /* best effort */ }
                }
            }

            _currentDbPath = newPath;

            for (int i = 0; i < _poolSize; i++)
            {
                var conn = new DuckDBConnection($"Data Source={newPath};ACCESS_MODE=READ_ONLY");
                await conn.OpenAsync(ct);
                await _connections.Writer.WriteAsync(conn, ct);
            }
        }
        finally { _refreshLock.Release(); }
    }

    public async Task<PooledConnection> AcquireAsync(CancellationToken ct)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(ReadOnlyConnectionPool));
        var pool = _connections ?? throw new InvalidOperationException(
            "ReadOnlyConnectionPool not initialized — InitializeAsync must be called first");
        var conn = await pool.Reader.ReadAsync(ct);
        return new PooledConnection(conn, pool, this);
    }

    internal async ValueTask ReturnAsync(DuckDBConnection conn, Channel<DuckDBConnection> ownerPool)
    {
        if (_disposed)
        {
            try { await conn.DisposeAsync(); } catch { }
            return;
        }
        if (ownerPool != _connections)
        {
            try { await conn.DisposeAsync(); } catch { }
            return;
        }
        await ownerPool.Writer.WriteAsync(conn);
    }

    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        var pool = _connections;
        if (pool is null) return;
        pool.Writer.TryComplete();
        while (pool.Reader.TryRead(out var conn))
        {
            try { await conn.DisposeAsync(); } catch { }
        }
        _refreshLock.Dispose();
    }

    public sealed class PooledConnection : IAsyncDisposable
    {
        public DuckDBConnection Connection { get; }
        private readonly Channel<DuckDBConnection> _ownerPool;
        private readonly ReadOnlyConnectionPool _pool;
        private bool _disposed;

        internal PooledConnection(DuckDBConnection connection, Channel<DuckDBConnection> ownerPool, ReadOnlyConnectionPool pool)
        {
            Connection = connection;
            _ownerPool = ownerPool;
            _pool = pool;
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;
            await _pool.ReturnAsync(Connection, _ownerPool);
        }
    }
}
