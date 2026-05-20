using System.Threading.Channels;
using DuckDB.NET.Data;
using Microsoft.Extensions.Logging;

namespace Tracer.Storage.DuckDB.MultiInterval;

/// <summary>
/// A pool of in-memory DuckDB connections, each with all current intervals attached.
/// Subscribes to <see cref="IntervalSetTracker.SetChanged"/> and atomically rebuilds
/// the pool whenever the interval set changes.
/// </summary>
public class LiveMultiIntervalReader : IAsyncDisposable
{
    private readonly IntervalSetTracker _tracker;
    private readonly ILogger<LiveMultiIntervalReader> _logger;
    private readonly int _poolSize;
    private readonly SemaphoreSlim _rebuildLock = new(1, 1);

        private Channel<PooledMultiIntervalConnection>? _connections;
    private IntervalSetSnapshot? _currentSnapshot;
    private bool _disposed;

    public LiveMultiIntervalReader(
        IntervalSetTracker tracker,
        ILogger<LiveMultiIntervalReader> logger,
        int poolSize = 8)
    {
        ArgumentNullException.ThrowIfNull(tracker);
        ArgumentNullException.ThrowIfNull(logger);
        _tracker = tracker;
        _logger = logger;
        _poolSize = poolSize;
    }

    /// <summary>
    /// Subscribes to <see cref="IntervalSetTracker.SetChanged"/> and builds the initial pool.
    /// Must be called before <see cref="AcquireAsync"/>.
    /// </summary>
    public virtual async Task InitializeAsync(CancellationToken ct)
    {
        _tracker.SetChanged += OnSetChangedAsync;
        await RebuildAsync(_tracker.CurrentSnapshot(), ct);
    }

    /// <summary>Returns a pooled connection. The caller must dispose it to return it to the pool.</summary>
    public async Task<PooledMultiIntervalConnection> AcquireAsync(CancellationToken ct)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(LiveMultiIntervalReader));

        // Retry loop: if a concurrent RebuildAsync completes the pool we read, re-read
        // the current pool and try again.
        while (true)
        {
            var pool = _connections
                ?? throw new InvalidOperationException(
                    "LiveMultiIntervalReader not initialized — call InitializeAsync first.");

            try
            {
                return await pool.Reader.ReadAsync(ct);
            }
            catch (ChannelClosedException) when (!_disposed)
            {
                // RebuildAsync swapped the pool and completed the old one while we were
                // waiting.  Try again with the freshly-installed pool.
            }
        }
    }

    internal async ValueTask ReturnAsync(PooledMultiIntervalConnection conn)
    {
        if (_disposed || !ReferenceEquals(conn.IssuingSnapshot, _currentSnapshot))
        {
            await conn.DisposeUnderlyingAsync();
            return;
        }

        var pool = _connections;
        if (pool is null || !pool.Writer.TryWrite(conn))
            await conn.DisposeUnderlyingAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _tracker.SetChanged -= OnSetChangedAsync;

        var pool = Interlocked.Exchange(ref _connections, null);
        if (pool is not null)
        {
            pool.Writer.TryComplete();
            while (pool.Reader.TryRead(out var conn))
                await conn.DisposeUnderlyingAsync();
        }

        _rebuildLock.Dispose();
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private async Task OnSetChangedAsync(IntervalSetSnapshot snapshot, CancellationToken ct)
        => await RebuildAsync(snapshot, ct);

    private async Task RebuildAsync(IntervalSetSnapshot snapshot, CancellationToken ct)
    {
        await _rebuildLock.WaitAsync(ct);
        try
        {
            _logger.LogDebug(
                "LiveMultiIntervalReader rebuilding pool for {Count} intervals",
                snapshot.Intervals.Count);

            // 1. Swap in a new empty pool immediately so AcquireAsync callers block on it.
            //    Update _currentSnapshot so stale checked-out connections get disposed on return.
            var newPool = Channel.CreateBounded<PooledMultiIntervalConnection>(_poolSize);
            var oldPool = Interlocked.Exchange(ref _connections, newPool);
            _currentSnapshot = snapshot;

            // 2. Drain and dispose old connections BEFORE building new ones.
            //    This releases any DuckDB file locks on the former-active interval so
            //    the new coordinator can ATTACH it as a completed interval below.
            if (oldPool is not null)
            {
                oldPool.Writer.TryComplete();
                while (oldPool.Reader.TryRead(out var stale))
                    await stale.DisposeUnderlyingAsync();
            }

            // 3. Populate the new pool.
            var active = snapshot.Active;
            if (active is not null)
            {
                // When there is an active interval, all pool slots open the same file
                // as their primary connection, which causes DuckDB to share a single
                // catalog across all slots.  We must therefore ATTACH completed intervals
                // exactly once (via the "coordinator" slot); the remaining worker slots
                // inherit those attachments from the shared catalog without re-attaching.
                var (coordinator, aliases) = await BuildCoordinatorAsync(snapshot, ct);
                await newPool.Writer.WriteAsync(coordinator, ct);

                for (int i = 1; i < _poolSize; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    var worker = await BuildWorkerAsync(active, aliases, snapshot, ct);
                    await newPool.Writer.WriteAsync(worker, ct);
                }
            }
            else
            {
                // No active interval: each slot uses an isolated in-memory connection
                // and independently ATTACHes the (non-write-locked) completed files.
                // There is no shared catalog, so multiple ATTACHes of the same file
                // across slots do not conflict.
                for (int i = 0; i < _poolSize; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    var conn = await BuildMemoryConnectionAsync(snapshot, ct);
                    await newPool.Writer.WriteAsync(conn, ct);
                }
            }
        }
        finally
        {
            _rebuildLock.Release();
        }
    }

    /// <summary>
    /// Builds the first (coordinator) connection for an active-interval pool.
    /// Opens the active file as primary, ATTACHes all completed intervals, and
    /// returns the connection together with the deterministic alias list.
    /// </summary>
    private async Task<(PooledMultiIntervalConnection, IReadOnlyList<string>)> BuildCoordinatorAsync(
        IntervalSetSnapshot snapshot, CancellationToken ct)
    {
        var active = snapshot.Active!;
        var completed = snapshot.Completed.ToList();

        var conn = new DuckDBConnection(
            $"Data Source={active.Directory.EventsDbPath};ACCESS_MODE=READ_ONLY");
        await conn.OpenAsync(ct);

        var manager = new AttachedDatabaseManager(conn);
        var aliases = new List<string>();

        foreach (var ivref in completed)
        {
            var file = new IntervalDbFile(
                ivref.Directory.EventsDbPath,
                $"iv_{ivref.Directory.Timestamp.Value}");
            var alias = await manager.AttachAsync(file, ct);
            aliases.Add(alias);
        }

        IReadOnlyList<string> readOnlyAliases = aliases.AsReadOnly();
        var pooled = new PooledMultiIntervalConnection(
            this, conn, manager, readOnlyAliases, snapshot, hasActive: true);
        return (pooled, readOnlyAliases);
    }

    /// <summary>
    /// Builds a worker connection for an active-interval pool.
    /// Opens the same active file (inheriting the coordinator's shared catalog and its
    /// ATTACH state) but does NOT manage any attachments itself.
    /// </summary>
    private async Task<PooledMultiIntervalConnection> BuildWorkerAsync(
        IntervalReference active,
        IReadOnlyList<string> aliases,
        IntervalSetSnapshot snapshot,
        CancellationToken ct)
    {
        var conn = new DuckDBConnection(
            $"Data Source={active.Directory.EventsDbPath};ACCESS_MODE=READ_ONLY");
        await conn.OpenAsync(ct);

        // Pass null manager — workers do not own ATTACHments and must not DETACH on dispose.
        return new PooledMultiIntervalConnection(
            this, conn, null, aliases, snapshot, hasActive: true);
    }

    /// <summary>
    /// Builds a standalone in-memory connection for the no-active-interval case.
    /// Each slot is fully isolated and independently ATTACHes all completed files.
    /// </summary>
    private async Task<PooledMultiIntervalConnection> BuildMemoryConnectionAsync(
        IntervalSetSnapshot snapshot, CancellationToken ct)
    {
        var completed = snapshot.Completed.ToList();

        var conn = new DuckDBConnection("DataSource=:memory:");
        await conn.OpenAsync(ct);

        var manager = new AttachedDatabaseManager(conn);
        var aliases = new List<string>();

        foreach (var ivref in completed)
        {
            var file = new IntervalDbFile(
                ivref.Directory.EventsDbPath,
                $"iv_{ivref.Directory.Timestamp.Value}");
            var alias = await manager.AttachAsync(file, ct);
            aliases.Add(alias);
        }

        return new PooledMultiIntervalConnection(
            this, conn, manager, aliases, snapshot, hasActive: false);
    }
}

/// <summary>
/// A pooled multi-interval DuckDB connection. Dispose to return it to the pool.
/// </summary>
public sealed class PooledMultiIntervalConnection : IAsyncDisposable
{
    private readonly LiveMultiIntervalReader _owner;
    private readonly DuckDBConnection _connection;
    private readonly AttachedDatabaseManager? _manager; // null for worker slots
    private readonly IReadOnlyList<string> _aliases;
    private readonly bool _hasActive;

    public DuckDBConnection Connection => _connection;
    internal IntervalSetSnapshot? IssuingSnapshot { get; }

    internal PooledMultiIntervalConnection(
        LiveMultiIntervalReader owner,
        DuckDBConnection connection,
        AttachedDatabaseManager? manager,
        IReadOnlyList<string> aliases,
        IntervalSetSnapshot? issuingSnapshot,
        bool hasActive)
    {
        _owner = owner;
        _connection = connection;
        _manager = manager;
        _aliases = aliases;
        IssuingSnapshot = issuingSnapshot;
        _hasActive = hasActive;
    }

    /// <summary>
    /// Builds a UNION ALL SQL covering all intervals (active + completed).
    /// Returns <c>"SELECT NULL WHERE FALSE"</c> when there are no intervals.
    /// Use this as the body of an <c>events</c> CTE when querying across intervals.
    /// </summary>
    public string BuildEventsUnionSql()
    {
        var parts = new List<string>();
        if (_hasActive) parts.Add("SELECT * FROM main.events");
        foreach (var alias in _aliases) parts.Add($"SELECT * FROM {alias}.events");
        if (parts.Count == 0) return "SELECT NULL WHERE FALSE";
        return string.Join("\nUNION ALL\n", parts);
    }

    /// <summary>
    /// Wraps <paramref name="sql"/> with a <c>WITH events AS (...)</c> CTE that covers
    /// all intervals. Query services should use this instead of bare <c>FROM events</c>.
    /// </summary>
    public string WithEventsCte(string sql)
    {
        ArgumentException.ThrowIfNullOrEmpty(sql);
        return $"WITH events AS (\n{BuildEventsUnionSql()}\n)\n{sql}";
    }

    /// <summary>Returns this connection to the pool (or disposes it if stale).</summary>
    public async ValueTask DisposeAsync()
        => await _owner.ReturnAsync(this);

    /// <summary>Disposes the underlying DuckDB connection directly (bypassing the pool).</summary>
    internal async ValueTask DisposeUnderlyingAsync()
    {
        // Only the coordinator slot owns ATTACHments and must DETACH on dispose.
        // Worker slots (null manager) simply close their connection.
        if (_manager is not null)
            await _manager.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
