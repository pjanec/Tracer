# BATCH-22 INSTRUCTIONS

## Tasks Covered

- **TRC-P5-001** — LiveMultiIntervalReader & IntervalSetTracker

## Context

Phase 4 is fully complete. This is the first batch of Phase 5.

Phase 5 delivers the engineer Timeline View with live multi-interval queries. The foundational piece is `IntervalSetTracker` (tracks which intervals are currently eligible for query) and `LiveMultiIntervalReader` (a connection pool whose connections have multiple DuckDB databases attached). Once built, all four Phase-3 query services are migrated to use `LiveMultiIntervalReader`, and `ReadOnlyConnectionPool` is removed from the Observer's DI.

**Phase 5 design document**: `docs/tracer_phase5_design.md` §3 is the primary reference for this batch.
**Task detail**: `docs/TASK-DETAIL.md` section `TRC-P5-001`.

---

## Key Design Decisions

1. **Option B (from §3.5)**: All query services (`SessionQueryService`, `ScenarioQueryService`, `TopologyQueryService`, `EventLookupService`) migrate to `LiveMultiIntervalReader`. `ReadOnlyConnectionPool` is kept in the codebase but removed from Observer DI.

2. **Retention coordination**: `RetentionManager` gains an optional `IntervalSetTracker` dependency. Before deleting an interval directory it calls `_tracker.OnIntervalEvictedAsync(dir, ct)` and waits 30 seconds to allow in-flight queries to complete against the old pool.

3. **Testability**: `IntervalSetTracker` and `LiveMultiIntervalReader` are NOT sealed; their key methods are `virtual` to allow test subclasses (following the same pattern as the existing `ReadOnlyConnectionPool`).

4. **SQL migration**: Each query service changes `FROM events WHERE ...` to `FROM ({pooled.BuildEventsUnionSql()}) t WHERE ...`. `PooledMultiIntervalConnection.BuildEventsUnionSql()` returns a UNION ALL of all attached interval databases (or `SELECT NULL WHERE FALSE` if none attached).

---

## File-by-File Plan

### New Files

#### `src/Tracer.Storage.DuckDB.MultiInterval/IntervalSetTracker.cs`

```csharp
using Microsoft.Extensions.Logging;
using Tracer.Agent.Lifecycle;
using Tracer.Agent.Storage;
using Tracer.Core.Domain;

namespace Tracer.Storage.DuckDB.MultiInterval;

/// <summary>
/// Tracks the set of intervals eligible for live querying:
/// the active interval plus the N most-recent completed ones.
/// Notifies subscribers when the set changes.
/// NOT sealed — test subclasses may override Initialize/Rotate/Evict for lifecycle testing.
/// </summary>
public class IntervalSetTracker
{
    private readonly IntervalRotator _rotator;
    private readonly int _completedIntervalsToKeep;
    private readonly object _lock = new();
    private readonly List<IntervalReference> _currentSet = new();
    private readonly ILogger<IntervalSetTracker> _logger;

    public event Func<IntervalSetSnapshot, CancellationToken, Task>? SetChanged;

    public IntervalSetTracker(
        IntervalRotator rotator,
        int completedIntervalsToKeep,
        ILogger<IntervalSetTracker> logger)
    {
        _rotator = rotator;
        _completedIntervalsToKeep = completedIntervalsToKeep;
        _logger = logger;
    }

    /// <summary>
    /// Initializes the set with the active interval plus existing completed intervals
    /// (up to the cap). Called by ObserverHostedService after startup recovery and
    /// after the active interval has been opened.
    /// </summary>
    public virtual async Task InitializeAsync(CancellationToken ct)
    {
        var active = _rotator.CurrentDirectory
            ?? throw new InvalidOperationException(
                "IntervalSetTracker.InitializeAsync called before active interval was opened");

        var completed = ListCompletedIntervals()
            .OrderByDescending(d => d.Timestamp.Value)
            .Take(_completedIntervalsToKeep)
            .ToList();

        lock (_lock)
        {
            _currentSet.Clear();
            foreach (var c in completed)
                _currentSet.Add(new IntervalReference(c, IntervalRole.Completed));
            _currentSet.Add(new IntervalReference(active, IntervalRole.Active));
        }

        await NotifyAsync(ct);
    }

    /// <summary>Called by ObserverHostedService after a rotation completes.</summary>
    public virtual async Task OnIntervalRotatedAsync(CancellationToken ct)
    {
        var newActive = _rotator.CurrentDirectory
            ?? throw new InvalidOperationException("OnIntervalRotatedAsync called with no active interval");

        lock (_lock)
        {
            // Demote previously-active interval to Completed
            var idx = _currentSet.FindIndex(r => r.Role == IntervalRole.Active);
            if (idx >= 0)
            {
                var prev = _currentSet[idx];
                _currentSet[idx] = prev with { Role = IntervalRole.Completed };
            }

            // Add the new active
            _currentSet.Add(new IntervalReference(newActive, IntervalRole.Active));

            // Trim oldest completed beyond cap
            TrimOldestCompletedBeyondCap_Locked();
        }

        await NotifyAsync(ct);
    }

    /// <summary>Called by RetentionManager after it has decided to evict an interval.</summary>
    public virtual async Task OnIntervalEvictedAsync(IntervalDirectory evicted, CancellationToken ct)
    {
        bool removed;
        lock (_lock)
        {
            removed = _currentSet.RemoveAll(
                r => r.Directory.Timestamp.Value == evicted.Timestamp.Value) > 0;
        }
        if (removed)
        {
            _logger.LogInformation("IntervalSetTracker: evicted {Interval}", evicted.Timestamp.Value);
            await NotifyAsync(ct);
        }
    }

    public IntervalSetSnapshot CurrentSnapshot()
    {
        lock (_lock)
        {
            return new IntervalSetSnapshot(_currentSet.ToList());
        }
    }

    protected virtual IEnumerable<IntervalDirectory> ListCompletedIntervals()
    {
        var intervalsRoot = Path.Combine(_rotator.CurrentDirectory!.DataRoot, "intervals");
        if (!Directory.Exists(intervalsRoot))
            return Enumerable.Empty<IntervalDirectory>();

        return Directory.EnumerateDirectories(intervalsRoot)
            .Where(d =>
            {
                var name = Path.GetFileName(d);
                if (!IntervalTimestamp.TryParse(name, out _)) return false;
                return File.Exists(Path.Combine(d, "_ready"));
            })
            .Select(d =>
            {
                var name = Path.GetFileName(d);
                IntervalTimestamp.TryParse(name, out var ts);
                return new IntervalDirectory(_rotator.CurrentDirectory!.DataRoot, ts);
            })
            .Where(d => d.Timestamp.Value != _rotator.CurrentDirectory?.Timestamp.Value);
    }

    private void TrimOldestCompletedBeyondCap_Locked()
    {
        var completed = _currentSet
            .Where(r => r.Role == IntervalRole.Completed)
            .OrderByDescending(r => r.Directory.Timestamp.Value)
            .ToList();

        for (int i = _completedIntervalsToKeep; i < completed.Count; i++)
            _currentSet.Remove(completed[i]);
    }

    private async Task NotifyAsync(CancellationToken ct)
    {
        var snap = CurrentSnapshot();
        var handlers = SetChanged;
        if (handlers is null) return;
        foreach (var handler in handlers.GetInvocationList()
                     .Cast<Func<IntervalSetSnapshot, CancellationToken, Task>>())
        {
            try { await handler(snap, ct); }
            catch (Exception ex)
            {
                _logger.LogError(ex, "IntervalSetTracker subscriber threw");
            }
        }
    }
}

public sealed record IntervalReference(IntervalDirectory Directory, IntervalRole Role);

public enum IntervalRole { Active, Completed }

public sealed record IntervalSetSnapshot(IReadOnlyList<IntervalReference> Intervals)
{
    public IntervalReference? Active =>
        Intervals.FirstOrDefault(r => r.Role == IntervalRole.Active);

    public IEnumerable<IntervalReference> Completed =>
        Intervals.Where(r => r.Role == IntervalRole.Completed);
}
```

**Important notes**:
- `ListCompletedIntervals()` is `protected virtual` — test subclasses can override it to inject fake directories.
- The method excludes the currently-active interval by comparing `Timestamp.Value`.
- `IntervalDirectory.DataRoot` is needed to build the intervals path. Check if `IntervalDirectory` already exposes `DataRoot`. Looking at the codebase: `public string DataRoot { get; }` — yes it does.

---

#### `src/Tracer.Storage.DuckDB.MultiInterval/LiveMultiIntervalReader.cs`

```csharp
using System.Threading.Channels;
using DuckDB.NET.Data;
using Microsoft.Extensions.Logging;

namespace Tracer.Storage.DuckDB.MultiInterval;

/// <summary>
/// A connection pool whose connections have multiple DuckDB databases attached —
/// one per interval in the current <see cref="IntervalSetTracker"/> snapshot.
/// Rebuilds the pool whenever <see cref="IntervalSetTracker.SetChanged"/> fires.
/// NOT sealed — test subclasses may override InitializeAsync.
/// </summary>
public class LiveMultiIntervalReader : IAsyncDisposable
{
    private readonly IntervalSetTracker _tracker;
    private readonly ILogger<LiveMultiIntervalReader> _logger;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly int _poolSize;

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
    /// Subscribes to the tracker and builds the initial connection pool.
    /// Must be called after <see cref="IntervalSetTracker.InitializeAsync"/>.
    /// </summary>
    public virtual async Task InitializeAsync(CancellationToken ct)
    {
        _tracker.SetChanged += OnSetChangedAsync;
        await RebuildAsync(_tracker.CurrentSnapshot(), ct);
    }

    private async Task OnSetChangedAsync(IntervalSetSnapshot snap, CancellationToken ct)
    {
        await RebuildAsync(snap, ct);
    }

    private async Task RebuildAsync(IntervalSetSnapshot snapshot, CancellationToken ct)
    {
        await _refreshLock.WaitAsync(ct);
        try
        {
            var old = _connections;
            var newChannel = Channel.CreateBounded<PooledMultiIntervalConnection>(_poolSize);
            _currentSnapshot = snapshot;
            _connections = newChannel;

            // Drain and dispose the old pool
            if (old is not null)
            {
                old.Writer.TryComplete();
                while (old.Reader.TryRead(out var conn))
                {
                    try { await conn.DisposeUnderlyingAsync(); }
                    catch { /* best effort */ }
                }
            }

            // Build new pool
            for (int i = 0; i < _poolSize; i++)
            {
                var conn = await BuildConnectionAsync(snapshot, ct);
                await newChannel.Writer.WriteAsync(conn, ct);
            }

            _logger.LogInformation(
                "LiveMultiIntervalReader rebuilt — {Count} interval(s) attached",
                snapshot.Intervals.Count);
        }
        finally { _refreshLock.Release(); }
    }

    private async Task<PooledMultiIntervalConnection> BuildConnectionAsync(
        IntervalSetSnapshot snapshot, CancellationToken ct)
    {
        var rawConn = new DuckDBConnection("DataSource=:memory:");
        await rawConn.OpenAsync(ct);
        var mgr = new AttachedDatabaseManager(rawConn);

        var aliases = new List<string>();
        foreach (var ivref in snapshot.Intervals)
        {
            var file = new IntervalDbFile(
                ivref.Directory.EventsDbPath,
                $"iv_{ivref.Directory.Timestamp.Value}");
            var alias = await mgr.AttachAsync(file, ct);
            aliases.Add(alias);
        }

        return new PooledMultiIntervalConnection(rawConn, mgr, aliases, snapshot, this);
    }

    public async Task<PooledMultiIntervalConnection> AcquireAsync(CancellationToken ct)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(LiveMultiIntervalReader));
        var pool = _connections
            ?? throw new InvalidOperationException(
                "LiveMultiIntervalReader not initialized — call InitializeAsync first");
        return await pool.Reader.ReadAsync(ct);
    }

    internal async ValueTask ReturnAsync(PooledMultiIntervalConnection conn)
    {
        if (_disposed)
        {
            await conn.DisposeUnderlyingAsync();
            return;
        }
        // If snapshot has changed since the connection was issued, discard it
        if (!ReferenceEquals(conn.IssuingSnapshot, _currentSnapshot))
        {
            await conn.DisposeUnderlyingAsync();
            return;
        }
        if (_connections is { } ch)
            await ch.Writer.WriteAsync(conn);
        else
            await conn.DisposeUnderlyingAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _tracker.SetChanged -= OnSetChangedAsync;
        var pool = _connections;
        if (pool is not null)
        {
            pool.Writer.TryComplete();
            while (pool.Reader.TryRead(out var conn))
            {
                try { await conn.DisposeUnderlyingAsync(); }
                catch { /* best effort */ }
            }
        }
        _refreshLock.Dispose();
    }
}

/// <summary>
/// A connection borrowed from <see cref="LiveMultiIntervalReader"/>.
/// Disposes by returning to the pool (or discarding if the pool has been rebuilt).
/// </summary>
public sealed class PooledMultiIntervalConnection : IAsyncDisposable
{
    public DuckDBConnection Connection { get; }
    internal IntervalSetSnapshot? IssuingSnapshot { get; }

    private readonly AttachedDatabaseManager _mgr;
    private readonly IReadOnlyList<string> _aliases;
    private readonly LiveMultiIntervalReader _owner;
    private bool _returned;

    internal PooledMultiIntervalConnection(
        DuckDBConnection connection,
        AttachedDatabaseManager mgr,
        IReadOnlyList<string> aliases,
        IntervalSetSnapshot? issuingSnapshot,
        LiveMultiIntervalReader owner)
    {
        Connection = connection;
        _mgr = mgr;
        _aliases = aliases;
        IssuingSnapshot = issuingSnapshot;
        _owner = owner;
    }

    /// <summary>
    /// Builds a UNION ALL SQL string selecting all columns from each attached interval's
    /// <c>events</c> table. Returns <c>SELECT NULL WHERE FALSE</c> when no intervals are attached.
    /// </summary>
    public string BuildEventsUnionSql()
    {
        if (_aliases.Count == 0)
            return "SELECT NULL WHERE FALSE";

        var parts = _aliases.Select(alias =>
            $"SELECT * FROM {alias}.events");
        return string.Join(" UNION ALL ", parts);
    }

    public async ValueTask DisposeAsync()
    {
        if (_returned) return;
        _returned = true;
        await _owner.ReturnAsync(this);
    }

    internal async ValueTask DisposeUnderlyingAsync()
    {
        await _mgr.DisposeAsync();
        await Connection.DisposeAsync();
    }
}
```

---

### Modified Files

#### `src/Tracer.Observer/Configuration/ObserverConfig.cs`

Add at the end of the class (before the closing brace), after `NasMockRoot`:

```csharp
    /// <summary>Configuration for the live multi-interval query window.</summary>
    public LiveQueryWindowConfig LiveQueryWindow { get; set; } = new();
```

Add new class at end of file:

```csharp
public sealed class LiveQueryWindowConfig
{
    /// <summary>
    /// Number of completed intervals to include in live queries beyond the active one.
    /// Higher = more history visible; more memory and file handles used.
    /// Default: 3.
    /// </summary>
    public int CompletedIntervalsToInclude { get; set; } = 3;
}
```

---

#### `src/Tracer.Agent/Storage/RetentionManager.cs`

The retention manager needs an optional `IntervalSetTracker` dependency and must:
1. Notify tracker BEFORE deleting the interval
2. Wait 30 seconds after notifying (to let in-flight queries against the old pool finish)
3. Then delete

Update the constructor signature:

```csharp
public RetentionManager(
    AgentConfig config,
    ILogger<RetentionManager> logger,
    IntervalSetTracker? tracker = null)
```

In `TryDeleteInterval` (or before calling it), update to async and call tracker:

```csharp
private async Task DeleteIntervalWithCoordinationAsync(
    IntervalDirectory dir, CancellationToken ct)
{
    if (_tracker is not null)
    {
        await _tracker.OnIntervalEvictedAsync(dir, ct);
        // Wait for in-flight queries against the old pool to drain
        await Task.Delay(TimeSpan.FromSeconds(30), ct);
    }
    TryDeleteDirectory(dir.RootPath);
}
```

Update `ApplyAsync` to be truly `async` (it currently returns `Task.CompletedTask`). Change it to iterate deletion with `await` calls. The signature stays `public async Task ApplyAsync(...)`.

**Note**: `RetentionManager` currently imports nothing from `Tracer.Storage.DuckDB.MultiInterval`. You need to:
1. Add a project reference from `Tracer.Agent` to `Tracer.Storage.DuckDB.MultiInterval` — but wait, `RetentionManager` is in `Tracer.Agent`. `IntervalSetTracker` is in `Tracer.Storage.DuckDB.MultiInterval`. Check if this creates a circular reference.

Looking at the dependency graph:
- `Tracer.Agent` → `Tracer.Storage.DuckDB`
- `Tracer.Storage.DuckDB.MultiInterval` → `Tracer.Agent` (needs `IntervalDirectory`, `IntervalTimestamp`)

This IS a circular dependency. `IntervalSetTracker` depends on `IntervalRotator` (in `Tracer.Agent.Lifecycle`). So putting `IntervalSetTracker` in `Tracer.Storage.DuckDB.MultiInterval` would require that project to reference `Tracer.Agent`, but `RetentionManager` (in `Tracer.Agent`) can't then reference `Tracer.Storage.DuckDB.MultiInterval`.

**Solution**: Move `IntervalSetTracker` to `Tracer.Agent` project, under `Tracer.Agent.Lifecycle` or `Tracer.Agent.Storage`. Then `RetentionManager` can access it directly, and `LiveMultiIntervalReader` (in `Tracer.Storage.DuckDB.MultiInterval`) takes the tracker's interface/event.

Or simpler: Use a callback/event interface approach. `RetentionManager` takes a `Func<IntervalDirectory, CancellationToken, Task>?` callback called `onBeforeDelete`. The `ObserverHostBuilder` wires this as `(dir, ct) => tracker.OnIntervalEvictedAsync(dir, ct)`.

**Recommended approach** (simplest, avoids circular refs):

In `RetentionManager`:
```csharp
public sealed class RetentionManager
{
    private readonly AgentConfig _config;
    private readonly ILogger<RetentionManager> _logger;
    private Func<IntervalDirectory, CancellationToken, Task>? _onBeforeDelete;

    // existing constructor
    public RetentionManager(AgentConfig config, ILogger<RetentionManager> logger)
    {
        ...
    }

    /// <summary>
    /// Optional callback called (and awaited) before an interval directory is deleted.
    /// The caller should use this to notify observers (e.g., IntervalSetTracker) to
    /// detach from the interval before deletion. After the callback returns, a fixed
    /// 30-second grace delay is applied before the directory is removed.
    /// </summary>
    public void SetPreDeletionCallback(Func<IntervalDirectory, CancellationToken, Task> callback)
    {
        _onBeforeDelete = callback;
    }
```

Then in `ObserverHostBuilder`:
```csharp
builder.Services.AddSingleton<RetentionManager>(sp =>
{
    var cfg = sp.GetRequiredService<AgentConfig>();
    var logger = sp.GetRequiredService<ILogger<RetentionManager>>();
    var rm = new RetentionManager(cfg, logger);
    // Wire pre-deletion callback after tracker is available
    var tracker = sp.GetRequiredService<IntervalSetTracker>();
    rm.SetPreDeletionCallback((dir, ct) => tracker.OnIntervalEvictedAsync(dir, ct));
    return rm;
});
```

Wait but `IntervalSetTracker` is in `Tracer.Storage.DuckDB.MultiInterval`. The `ObserverHostBuilder` is in `Tracer.Observer` which references `Tracer.Storage.DuckDB.MultiInterval` (already). So `ObserverHostBuilder` can call `tracker.OnIntervalEvictedAsync` directly.

The `RetentionManager` itself only needs the callback `Func<IntervalDirectory, CancellationToken, Task>?`, which avoids the circular dependency.

**Summary of circular dependency resolution**:
- `IntervalSetTracker` stays in `Tracer.Storage.DuckDB.MultiInterval` (it references `Tracer.Agent` for `IntervalRotator`, `IntervalDirectory`)
- `RetentionManager` stays in `Tracer.Agent` (no reference to `Tracer.Storage.DuckDB.MultiInterval`)
- `RetentionManager` exposes `SetPreDeletionCallback(Func<IntervalDirectory, CancellationToken, Task>)`
- `ObserverHostBuilder` (which can see both) wires the callback

---

#### `src/Tracer.Agent/Storage/RetentionManager.cs` — Updated Implementation

Make `ApplyAsync` actually async and add the callback mechanism:

```csharp
public sealed class RetentionManager
{
    private readonly AgentConfig _config;
    private readonly ILogger<RetentionManager> _logger;
    private Func<IntervalDirectory, CancellationToken, Task>? _onBeforeDelete;

    public RetentionManager(AgentConfig config, ILogger<RetentionManager> logger)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(logger);
        _config = config;
        _logger = logger;
    }

    public void SetPreDeletionCallback(Func<IntervalDirectory, CancellationToken, Task> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        _onBeforeDelete = callback;
    }

    public async Task ApplyAsync(IntervalTimestamp? openIntervalTimestamp, CancellationToken ct)
    {
        var intervalsRoot = Path.Combine(_config.DataRoot, "intervals");
        if (!Directory.Exists(intervalsRoot))
            return;

        var readyDirs = Directory.EnumerateDirectories(intervalsRoot)
            .Where(d =>
            {
                var name = Path.GetFileName(d);
                if (!IntervalTimestamp.TryParse(name, out _)) return false;
                return File.Exists(Path.Combine(d, "_ready"));
            })
            .OrderBy(d => Path.GetFileName(d))
            .ToList();

        var openValue = openIntervalTimestamp?.Value;

        var toDelete = new List<string>();
        var keep = _config.KeepLastNIntervals;
        if (readyDirs.Count > keep)
            toDelete.AddRange(readyDirs.Take(readyDirs.Count - keep));

        if (openValue is not null)
            toDelete.RemoveAll(d => Path.GetFileName(d) == openValue);

        foreach (var dirPath in toDelete)
        {
            ct.ThrowIfCancellationRequested();
            var name = Path.GetFileName(dirPath);
            if (!IntervalTimestamp.TryParse(name, out var ts)) continue;
            var intervalDir = new IntervalDirectory(_config.DataRoot, ts);

            if (_onBeforeDelete is not null)
            {
                await _onBeforeDelete(intervalDir, ct);
                // 30-second grace period for in-flight queries to drain
                try { await Task.Delay(TimeSpan.FromSeconds(30), ct); }
                catch (OperationCanceledException) { return; }
            }

            TryDeleteInterval(dirPath);
        }

        EnforceDiskWatermark(intervalsRoot, openValue, ct);
    }

    // ... rest of the existing code (TryDeleteInterval, EnforceDiskWatermark) remains unchanged
}
```

---

#### `src/Tracer.Observer/Lifecycle/ObserverHostedService.cs`

Replace the current `ReadOnlyConnectionPool`-based implementation with one using `IntervalSetTracker` and `LiveMultiIntervalReader`.

New constructor and class body:

```csharp
public sealed class ObserverHostedService : BackgroundService
{
    private readonly IStartupRecovery _recovery;
    private readonly IntervalRotator _rotator;
    private readonly IntervalScheduler _scheduler;
    private readonly ObserverIngestionPipeline _ingestion;
    private readonly IntervalSetTracker _tracker;
    private readonly LiveMultiIntervalReader _multiReader;
    private readonly RetentionManager _retention;
    private readonly IClock _clock;
    private readonly ILogger<ObserverHostedService> _logger;

    public ObserverHostedService(
        IStartupRecovery recovery,
        IntervalRotator rotator,
        IntervalScheduler scheduler,
        ObserverIngestionPipeline ingestion,
        IntervalSetTracker tracker,
        LiveMultiIntervalReader multiReader,
        RetentionManager retention,
        IClock clock,
        ILogger<ObserverHostedService> logger)
    { ... }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("TracerObserver starting");

        // 1. Recovery — finalize any orphaned intervals from previous run
        await _recovery.RecoverAsync(stoppingToken);

        // 2. Open the current interval
        await _rotator.OpenCurrentAsync(stoppingToken);

        // 2a. Initialize multi-interval tracker (builds initial snapshot)
        await _tracker.InitializeAsync(stoppingToken);

        // 3. Initialize the multi-interval reader pool against the current snapshot
        await _multiReader.InitializeAsync(stoppingToken);

        // 4. Start ingestion and retention in background
        var ingestionTask = _ingestion.RunAsync(stoppingToken);
        var retentionTask = RetentionLoopAsync(stoppingToken);

        // 5. Rotation loop runs on this task
        await RotationLoopAsync(stoppingToken);

        // 6. Shutdown propagates to background tasks
        await Task.WhenAll(ingestionTask, retentionTask);

        // 7. Final rotation to close the current interval cleanly
        await _rotator.RotateAsync(ManifestFinalizationReason.GracefulShutdown, CancellationToken.None);

        _logger.LogInformation("TracerObserver stopped");
    }

    private async Task RotationLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var timeUntilBoundary = _scheduler.TimeUntilNextBoundary();
            if (timeUntilBoundary > TimeSpan.Zero)
            {
                try { await Task.Delay(timeUntilBoundary, ct); }
                catch (OperationCanceledException) { return; }
            }

            await _rotator.RotateAsync(ManifestFinalizationReason.ScheduledRotation, ct);

            // Tracker observes the new rotator state; SetChanged fires → LiveMultiIntervalReader rebuilds
            try
            {
                await _tracker.OnIntervalRotatedAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Tracker update failed after rotation");
            }
        }
    }

    private async Task RetentionLoopAsync(CancellationToken ct)
    {
        var interval = TimeSpan.FromMinutes(5);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await _retention.ApplyAsync(_rotator.CurrentDirectory?.Timestamp, ct);
                await Task.Delay(interval, ct);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Retention pass failed; continuing");
                try { await Task.Delay(interval, ct); }
                catch (OperationCanceledException) { return; }
            }
        }
    }
}
```

Add required usings: `Tracer.Storage.DuckDB.MultiInterval`

---

#### `src/Tracer.Observer/ObserverHostBuilder.cs`

1. Replace `builder.Services.AddSingleton<ReadOnlyConnectionPool>();` with:

```csharp
// ── Multi-interval query infrastructure ──────────────────────────────────
builder.Services.AddSingleton<IntervalSetTracker>(sp =>
    new IntervalSetTracker(
        sp.GetRequiredService<IntervalRotator>(),
        sp.GetRequiredService<ObserverConfig>().LiveQueryWindow.CompletedIntervalsToInclude,
        sp.GetRequiredService<ILogger<IntervalSetTracker>>()));

builder.Services.AddSingleton<LiveMultiIntervalReader>();

// Wire the pre-deletion callback on RetentionManager so it notifies the tracker
// before deleting interval directories (30-second grace delay applied inside ApplyAsync)
builder.Services.AddSingleton<RetentionManager>(sp =>
{
    var cfg = sp.GetRequiredService<AgentConfig>();
    var logger = sp.GetRequiredService<ILogger<RetentionManager>>();
    var rm = new RetentionManager(cfg, logger);
    var tracker = sp.GetRequiredService<IntervalSetTracker>();
    rm.SetPreDeletionCallback((dir, ct) => tracker.OnIntervalEvictedAsync(dir, ct));
    return rm;
});
```

2. Change the four query service registrations to resolve `LiveMultiIntervalReader` (they now take it as a constructor parameter — see query service changes below).

**Note**: `RetentionManager` was previously registered via `builder.Services.AddSingleton<RetentionManager>()`. Remove that line and replace with the factory above.

3. Add usings: `Tracer.Storage.DuckDB.MultiInterval`

---

#### `src/Tracer.WebApi/Queries/SessionQueryService.cs`

Change constructor dependency from `ReadOnlyConnectionPool` to `LiveMultiIntervalReader`:

```csharp
public sealed class SessionQueryService(LiveMultiIntervalReader multiReader)
{
    private readonly LiveMultiIntervalReader _multiReader = multiReader;
```

Change all occurrences of `await _pool.AcquireAsync(ct)` → `await _multiReader.AcquireAsync(ct)`.
Change `await using var pooled = await _pool.AcquireAsync(ct);` → `await using var pooled = await _multiReader.AcquireAsync(ct);`.

Update SQL: every `FROM events` → `FROM ({pooled.BuildEventsUnionSql()}) t`

For example, the session start query becomes:
```sql
SELECT json_extract_string(payload, '$.sessionId') as session_id,
       publish_wallclock,
       json_extract_string(payload, '$.scenarioId') as scenario_id,
       json_extract_string(payload, '$.label') as label
FROM ({pooled.BuildEventsUnionSql()}) t
WHERE topic = 'system.session_start'
  AND json_extract_string(payload, '$.sessionId') IS NOT NULL
ORDER BY publish_wallclock DESC
```

**Repeat this pattern for ALL SQL statements in SessionQueryService that use `FROM events`.**

There are multiple SQL blocks in `SessionQueryService.ListAsync` (session starts, session ends, event count/nodes per session). Each `FROM events` needs to become `FROM ({pooled.BuildEventsUnionSql()}) t`.

**Important**: `pooled` is acquired ONCE at the top of `ListAsync` and used throughout. This works because `PooledMultiIntervalConnection` is disposed via `await using` at method exit, returning the connection to the pool.

Add using: `using Tracer.Storage.DuckDB.MultiInterval;`
Remove using: `using Tracer.WebApi.Lifecycle;` (if it was only there for `ReadOnlyConnectionPool`)

---

#### `src/Tracer.WebApi/Queries/ScenarioQueryService.cs`

Same pattern as above. Change constructor, change `_pool` → `_multiReader`, change `FROM events` → `FROM ({pooled.BuildEventsUnionSql()}) t` in ALL SQL statements.

**Note**: `ScenarioQueryService` has multiple methods each acquiring their own connection. Some have multiple SQL statements. Update all of them.

---

#### `src/Tracer.WebApi/Queries/TopologyQueryService.cs`

Same pattern. The topology query is:
```sql
SELECT publisher_node, MIN(publish_wallclock), MAX(publish_wallclock), COUNT(*)
FROM events
GROUP BY publisher_node
ORDER BY publisher_node
```
→
```sql
SELECT publisher_node, MIN(publish_wallclock), MAX(publish_wallclock), COUNT(*)
FROM ({pooled.BuildEventsUnionSql()}) t
GROUP BY publisher_node
ORDER BY publisher_node
```

---

#### `src/Tracer.WebApi/Queries/EventLookupService.cs`

Same pattern. The event lookup query:
```sql
SELECT ... FROM events WHERE event_id = $id LIMIT 1
```
→
```sql
SELECT ... FROM ({pooled.BuildEventsUnionSql()}) t WHERE event_id = $id LIMIT 1
```

---

#### `src/Tracer.TestHarness/Observer/ObserverFixture.cs`

Replace the `Pool` property:
```csharp
// REMOVE:
public ReadOnlyConnectionPool Pool =>
    App.Services.GetRequiredService<ReadOnlyConnectionPool>();

// ADD:
public LiveMultiIntervalReader MultiReader =>
    App.Services.GetRequiredService<LiveMultiIntervalReader>();
```

Remove `using Tracer.WebApi.Lifecycle;` if it was only for `ReadOnlyConnectionPool`.
Add `using Tracer.Storage.DuckDB.MultiInterval;`

---

#### `src/Tracer.TestHarness/Observer/WebApiFixture.cs`

Replace the `ReadOnlyConnectionPool` registration with `LiveMultiIntervalReader` + a fake `IntervalSetTracker`.

Since `WebApiFixture` is a lightweight fixture with no actual DuckDB or intervals, we need a minimal `LiveMultiIntervalReader` that doesn't crash when acquired without real databases attached. The simplest approach: register a no-op tracker and override `InitializeAsync` with a do-nothing subclass.

**Approach**: Create a `NoOpLiveMultiIntervalReader` inner class in `WebApiFixture` that extends `LiveMultiIntervalReader` and overrides `InitializeAsync` to build a pool with a single in-memory empty connection (no databases attached). Or simply use the real `LiveMultiIntervalReader` but with a fake tracker that provides an empty snapshot.

Simplest viable approach:
```csharp
// Register a stub tracker that always provides an empty snapshot
builder.Services.AddSingleton<IntervalSetTracker>(sp =>
    new NoOpIntervalSetTracker());

builder.Services.AddSingleton<LiveMultiIntervalReader>(sp =>
{
    var reader = new NoOpLiveMultiIntervalReader(
        sp.GetRequiredService<IntervalSetTracker>(),
        sp.GetRequiredService<ILogger<LiveMultiIntervalReader>>());
    return reader;
});
```

Where `NoOpIntervalSetTracker` and `NoOpLiveMultiIntervalReader` are private inner classes of `WebApiFixture` that return empty snapshots and no-op initialization respectively.

Actually, the cleanest approach: make the `WebApiFixture` explicitly use an empty-snapshot reader that doesn't try to connect to DuckDB. Implement a private `StubLiveMultiIntervalReader` subclass with `InitializeAsync` doing nothing and `AcquireAsync` throwing a clear error (since endpoint tests via `WebApiFixture` don't hit DuckDB — they're unit-level HTTP tests).

---

#### `tests/Tracer.Tests.Unit/Observer/ObserverHostedServiceTests.cs`

Rewrite the test doubles to use the new `IntervalSetTracker` + `LiveMultiIntervalReader` API.

The five existing tests need updating:

1. **`OnStart_RecoveryRunsBeforeIntervalOpen`**: Create `TrackingIntervalSetTracker : IntervalSetTracker` that appends to an order list in `InitializeAsync`. Verify recovery appears before tracker-init.

2. **`OnStart_PoolInitializedAfterIntervalOpen`**: Verify `tracker.InitializeCalled` is true after start.

3. **`OnGracefulShutdown_FinalRotationHasGracefulReason`**: This test doesn't need `pool` at all. Replace the `TrackingPool` with a simple tracking tracker.

4. **`PoolRefreshFailure_Logged_HostNotCrashed`**: Create `FailingIntervalSetTracker : IntervalSetTracker` where `OnIntervalRotatedAsync` throws. Verify the service continues.

5. **`OnStart_ServiceStartsWithoutException`**: Use simple tracking tracker.

**TrackingIntervalSetTracker** (private inner class):
```csharp
private sealed class TrackingIntervalSetTracker : IntervalSetTracker
{
    public bool InitializeCalled { get; private set; }

    public TrackingIntervalSetTracker(IntervalRotator rotator, ILogger<IntervalSetTracker> logger)
        : base(rotator, 0, logger) { }

    public override Task InitializeAsync(CancellationToken ct)
    {
        InitializeCalled = true;
        return Task.CompletedTask;
    }

    public override Task OnIntervalRotatedAsync(CancellationToken ct) => Task.CompletedTask;
    public override Task OnIntervalEvictedAsync(IntervalDirectory dir, CancellationToken ct) => Task.CompletedTask;
}
```

**TrackingLiveMultiIntervalReader** (private inner class):
```csharp
private sealed class TrackingLiveMultiIntervalReader : LiveMultiIntervalReader
{
    public TrackingLiveMultiIntervalReader(IntervalSetTracker tracker, ILogger<LiveMultiIntervalReader> logger)
        : base(tracker, logger) { }

    public override Task InitializeAsync(CancellationToken ct) => Task.CompletedTask;
}
```

**FailingIntervalSetTracker** (for the failure test):
```csharp
private sealed class FailingIntervalSetTracker : IntervalSetTracker
{
    public FailingIntervalSetTracker(IntervalRotator rotator, ILogger<IntervalSetTracker> logger)
        : base(rotator, 0, logger) { }

    public override Task InitializeAsync(CancellationToken ct) => Task.CompletedTask;
    public override Task OnIntervalRotatedAsync(CancellationToken ct) =>
        throw new InvalidOperationException("Simulated tracker failure");
    public override Task OnIntervalEvictedAsync(IntervalDirectory dir, CancellationToken ct) => Task.CompletedTask;
}
```

Add usings: `Tracer.Storage.DuckDB.MultiInterval`
Remove usings for `ReadOnlyConnectionPool` if no longer needed.

---

### New Test Files

#### `tests/Tracer.Tests.Unit/MultiInterval/IntervalSetTrackerTests.cs`

7 tests as specified in TRC-P5-001 success condition 1. Use a `FakeIntervalSetTracker` (or configure a real one with fake rotator). The challenge is that `IntervalSetTracker` depends on `IntervalRotator` which has many dependencies.

**Approach**: subclass `IntervalSetTracker` and override `ListCompletedIntervals()` to inject test data without needing a real rotator. Use a `FakeRotator` for the constructor parameter:

```csharp
private static IntervalSetTracker CreateTracker(
    string dataRoot,
    string? activeTimestamp,
    IEnumerable<string>? completedTimestamps = null,
    int cap = 10)
{
    var agentConfig = new AgentConfig { NodeId = "t", DataRoot = dataRoot, ... };
    // We need a rotator but don't want to actually open intervals
    // Inject a TestIntervalSetTracker that overrides ListCompletedIntervals()
    return new TestIntervalSetTracker(dataRoot, activeTimestamp, completedTimestamps ?? [], cap);
}

private sealed class TestIntervalSetTracker : IntervalSetTracker
{
    private readonly string _dataRoot;
    private readonly string? _activeTimestamp;
    private readonly IEnumerable<string> _completedTimestamps;

    public TestIntervalSetTracker(
        string dataRoot,
        string? activeTimestamp,
        IEnumerable<string> completedTimestamps,
        int cap)
        : base(CreateFakeRotator(dataRoot, activeTimestamp), cap, NullLogger<IntervalSetTracker>.Instance)
    {
        _dataRoot = dataRoot;
        _activeTimestamp = activeTimestamp;
        _completedTimestamps = completedTimestamps;
    }

    protected override IEnumerable<IntervalDirectory> ListCompletedIntervals()
    {
        return _completedTimestamps
            .Where(ts => IntervalTimestamp.TryParse(ts, out _))
            .Select(ts => new IntervalDirectory(_dataRoot, new IntervalTimestamp(ts)));
    }

    private static IntervalRotator CreateFakeRotator(string dataRoot, string? activeTimestamp)
    {
        // Build a real IntervalRotator but don't call OpenCurrentAsync
        // Use fake dependencies
        var config = new AgentConfig
        {
            NodeId = "test",
            DataRoot = dataRoot,
            LogsRoot = dataRoot,
            IntervalDuration = TimeSpan.FromHours(1),
            KeepLastNIntervals = 4,
            DiskWatermarkPercent = 10,
        };
        var clock = new SystemClock();
        var scheduler = new IntervalScheduler(clock, config);
        var upload = new NoOpUploadService();
        var dispatcher = new UploadIntentDispatcher(upload, NullLogger<UploadIntentDispatcher>.Instance);
        var rotator = new IntervalRotator(scheduler, config, dispatcher, clock,
            NullLogger<IntervalRotator>.Instance);

        if (activeTimestamp is not null)
        {
            // Manually set CurrentDirectory via reflection or make it settable
            // Since IntervalDirectory needs to be set, use the internal setter
            // IntervalRotator.CurrentDirectory is publicly readable but set privately
            // We need a different approach
        }

        return rotator;
    }
}
```

Actually this is getting complex because `IntervalRotator.CurrentDirectory` is set in `OpenCurrentAsync`. We can't easily set it without calling that method (which actually creates DuckDB files).

**Simpler approach for tests**: Use real temp directories and call `_rotator.OpenCurrentAsync` in test setup. This is an "integration-style" unit test:

```csharp
private async Task<IntervalSetTracker> CreateTrackerWithActiveIntervalAsync(
    string dataRoot, int cap = 3, CancellationToken ct = default)
{
    // create real rotator and open current interval
    var config = new AgentConfig { ... DataRoot = dataRoot ... };
    var clock = new SimulatedClock(...);
    var scheduler = new IntervalScheduler(clock, config);
    var rotator = new IntervalRotator(scheduler, config, ...);
    await rotator.OpenCurrentAsync(ct);
    
    var tracker = new IntervalSetTracker(rotator, cap, NullLogger<IntervalSetTracker>.Instance);
    return tracker;
}
```

**Recommended**: Since `IntervalSetTracker.ListCompletedIntervals` is `protected virtual`, create a `TestIntervalSetTracker` that overrides it to return pre-set `IntervalDirectory` instances. The active interval comes from the real rotator's `CurrentDirectory`. Use a minimal rotator setup.

Here is the recommended test structure for `IntervalSetTrackerTests.cs`:

```csharp
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Tracer.Adapters.Mock;
using Tracer.Agent.Configuration;
using Tracer.Agent.Lifecycle;
using Tracer.Agent.Storage;
using Tracer.Agent.Time;
using Tracer.Agent.Upload;
using Tracer.Core.Abstractions;
using Tracer.Core.Domain;
using Tracer.Core.Time;
using Tracer.Storage.DuckDB.MultiInterval;
using Xunit;

namespace Tracer.Tests.Unit.MultiInterval;

public sealed class IntervalSetTrackerTests : IAsyncDisposable
{
    private readonly string _tempDir;

    public IntervalSetTrackerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"ist-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public async Task InitializeAsync_NoCompletedIntervals_SnapshotContainsOnlyActive()
    {
        await using var rotator = CreateRotator(_tempDir);
        await rotator.OpenCurrentAsync(default);

        var tracker = new IntervalSetTracker(rotator, completedIntervalsToKeep: 3,
            NullLogger<IntervalSetTracker>.Instance);
        await tracker.InitializeAsync(default);

        var snap = tracker.CurrentSnapshot();
        snap.Intervals.Should().HaveCount(1);
        snap.Active.Should().NotBeNull();
        snap.Completed.Should().BeEmpty();
    }

    [Fact]
    public async Task InitializeAsync_FiveCompleted_CapThree_SnapshotContainsThreeNewestPlusActive()
    {
        await using var rotator = CreateRotator(_tempDir);
        await rotator.OpenCurrentAsync(default);

        // Create 5 fake completed interval directories
        var completedTimestamps = new[]
        {
            "20260101T000000Z",
            "20260101T010000Z",
            "20260101T020000Z",
            "20260101T030000Z",
            "20260101T040000Z",
        };
        foreach (var ts in completedTimestamps)
        {
            var dir = new IntervalDirectory(_tempDir, new IntervalTimestamp(ts));
            dir.EnsureCreated();
            dir.WriteReadySentinel();
        }

        var tracker = new IntervalSetTracker(rotator, completedIntervalsToKeep: 3,
            NullLogger<IntervalSetTracker>.Instance);
        await tracker.InitializeAsync(default);

        var snap = tracker.CurrentSnapshot();
        snap.Intervals.Should().HaveCount(4); // 3 completed + 1 active
        snap.Completed.Count().Should().Be(3);
        snap.Active.Should().NotBeNull();
        // The 3 newest completed intervals should be included
        var completedTimestampValues = snap.Completed
            .Select(c => c.Directory.Timestamp.Value)
            .ToHashSet();
        completedTimestampValues.Should().Contain("20260101T040000Z");
        completedTimestampValues.Should().Contain("20260101T030000Z");
        completedTimestampValues.Should().Contain("20260101T020000Z");
        completedTimestampValues.Should().NotContain("20260101T000000Z");
        completedTimestampValues.Should().NotContain("20260101T010000Z");
    }

    [Fact]
    public async Task OnIntervalRotatedAsync_PreviousActiveBecomesCompleted()
    {
        await using var rotator = CreateRotator(_tempDir);
        await rotator.OpenCurrentAsync(default);
        var tracker = new IntervalSetTracker(rotator, completedIntervalsToKeep: 3,
            NullLogger<IntervalSetTracker>.Instance);
        await tracker.InitializeAsync(default);

        var originalActiveTs = rotator.CurrentDirectory!.Timestamp.Value;

        // Rotate (this closes current and opens a new interval)
        await rotator.RotateAsync(ManifestFinalizationReason.ScheduledRotation, default);
        await tracker.OnIntervalRotatedAsync(default);

        var snap = tracker.CurrentSnapshot();
        var completedValues = snap.Completed.Select(c => c.Directory.Timestamp.Value).ToList();
        completedValues.Should().Contain(originalActiveTs,
            "the previously-active interval must now be Completed");
        snap.Active.Should().NotBeNull("a new active interval must exist after rotation");
        snap.Active!.Directory.Timestamp.Value.Should().NotBe(originalActiveTs);
    }

    [Fact]
    public async Task OnIntervalEvictedAsync_RemovesEvictedIntervalFromSnapshot()
    {
        await using var rotator = CreateRotator(_tempDir);
        await rotator.OpenCurrentAsync(default);

        // Create 1 completed interval
        var completedTs = "20260101T000000Z";
        var completedDir = new IntervalDirectory(_tempDir, new IntervalTimestamp(completedTs));
        completedDir.EnsureCreated();
        completedDir.WriteReadySentinel();

        var tracker = new IntervalSetTracker(rotator, completedIntervalsToKeep: 3,
            NullLogger<IntervalSetTracker>.Instance);
        await tracker.InitializeAsync(default);

        tracker.CurrentSnapshot().Completed.Should().ContainSingle();

        await tracker.OnIntervalEvictedAsync(completedDir, default);

        tracker.CurrentSnapshot().Completed.Should().BeEmpty();
    }

    [Fact]
    public async Task SetChanged_FiredAfterInitialize()
    {
        await using var rotator = CreateRotator(_tempDir);
        await rotator.OpenCurrentAsync(default);
        var tracker = new IntervalSetTracker(rotator, completedIntervalsToKeep: 3,
            NullLogger<IntervalSetTracker>.Instance);

        int firedCount = 0;
        tracker.SetChanged += (snap, ct) => { firedCount++; return Task.CompletedTask; };

        await tracker.InitializeAsync(default);

        firedCount.Should().Be(1);
    }

    [Fact]
    public async Task SetChanged_FiredAfterRotation()
    {
        await using var rotator = CreateRotator(_tempDir);
        await rotator.OpenCurrentAsync(default);
        var tracker = new IntervalSetTracker(rotator, completedIntervalsToKeep: 3,
            NullLogger<IntervalSetTracker>.Instance);
        await tracker.InitializeAsync(default);

        int firedCount = 0;
        tracker.SetChanged += (snap, ct) => { firedCount++; return Task.CompletedTask; };

        await rotator.RotateAsync(ManifestFinalizationReason.ScheduledRotation, default);
        await tracker.OnIntervalRotatedAsync(default);

        firedCount.Should().Be(1);
    }

    [Fact]
    public async Task SetChanged_NotFiredIfEvictionTargetNotInSet()
    {
        await using var rotator = CreateRotator(_tempDir);
        await rotator.OpenCurrentAsync(default);
        var tracker = new IntervalSetTracker(rotator, completedIntervalsToKeep: 3,
            NullLogger<IntervalSetTracker>.Instance);
        await tracker.InitializeAsync(default);

        int firedCount = 0;
        tracker.SetChanged += (snap, ct) => { firedCount++; return Task.CompletedTask; };

        // Evict a directory that was never in the snapshot
        var notInSet = new IntervalDirectory(_tempDir, new IntervalTimestamp("20250101T000000Z"));
        await tracker.OnIntervalEvictedAsync(notInSet, default);

        firedCount.Should().Be(0);
    }

    private static IntervalRotator CreateRotator(string dataRoot)
    {
        var config = new AgentConfig
        {
            NodeId = "test",
            DataRoot = dataRoot,
            LogsRoot = dataRoot,
            IntervalDuration = TimeSpan.FromHours(1),
            KeepLastNIntervals = 4,
            DiskWatermarkPercent = 10,
        };
        var clock = new SystemClock();
        var scheduler = new IntervalScheduler(clock, config);
        var upload = new NoOpUploadService();
        var dispatcher = new UploadIntentDispatcher(upload, NullLogger<UploadIntentDispatcher>.Instance);
        return new IntervalRotator(scheduler, config, dispatcher, clock,
            NullLogger<IntervalRotator>.Instance);
    }

    public ValueTask DisposeAsync()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
        return ValueTask.CompletedTask;
    }

    private sealed class NoOpUploadService : ITelemetryUploadService
    {
        public Task<UploadIntentId> RequestUploadAsync(UploadRequest req, CancellationToken ct)
            => Task.FromResult(new UploadIntentId(Guid.NewGuid().ToString()));
        public Task<UploadStatus> GetStatusAsync(UploadIntentId id, CancellationToken ct)
            => Task.FromResult(UploadStatus.Complete);
    }
}
```

---

#### `tests/Tracer.Tests.Unit/MultiInterval/LiveMultiIntervalReaderTests.cs`

5 tests as specified in TRC-P5-001 success condition 2.

```csharp
[Fact]
public async Task InitializeAsync_BuildsPoolSizedConnections()
{
    // Use a tracker that fires SetChanged with a snapshot of 2 real DB files
    // Create 2 in-memory DuckDB files (or temp DuckDB files) as test intervals
    // Verify that after InitializeAsync, the pool has poolSize connections available
}

[Fact]
public async Task AcquireAsync_ReturnsConnection_WithCurrentIntervalsAttached()
{
    // After InitializeAsync with 2 interval files attached,
    // the acquired connection's BuildEventsUnionSql() includes both aliases
}

[Fact]
public async Task AfterRotation_NewConnectionsHaveNewSet()
{
    // Trigger tracker.SetChanged with a new snapshot (1 interval added)
    // Verify next acquired connection reflects the new set
}

[Fact]
public async Task ConnectionFromOldPool_DisposesRatherThanReturns()
{
    // Issue a connection (conn1), then trigger a rebuild
    // Dispose conn1 — it should be discarded, not returned to new pool
    // Pool count should remain _poolSize
}

[Fact]
public async Task ConcurrentAcquireAndRebuild_NoCrashOrHandleLeak()
{
    // 8 concurrent acquires while a rebuild is in progress
    // No exceptions; all connections dispose cleanly
}
```

**Implementation note**: For these tests you'll need actual DuckDB files. Create them with `DuckDbStorageWriter.CreateAsync()` in temp directories. The `IntervalSetTracker` can be a minimal subclass that provides a controlled snapshot.

Use a `TestIntervalSetTracker` subclass:
```csharp
private sealed class TestIntervalSetTracker : IntervalSetTracker
{
    private IntervalSetSnapshot _snapshot = new(Array.Empty<IntervalReference>());

    public TestIntervalSetTracker()
        : base(null!, 0, NullLogger<IntervalSetTracker>.Instance) { }

    public override Task InitializeAsync(CancellationToken ct) => Task.CompletedTask;
    public override Task OnIntervalRotatedAsync(CancellationToken ct) => Task.CompletedTask;
    public override Task OnIntervalEvictedAsync(IntervalDirectory dir, CancellationToken ct) => Task.CompletedTask;
    public override IntervalSetSnapshot CurrentSnapshot() => _snapshot;

    public async Task UpdateSnapshotAsync(IntervalSetSnapshot snap, CancellationToken ct)
    {
        _snapshot = snap;
        var h = SetChanged;
        if (h is not null) await h(snap, ct);
    }
}
```

Wait — `IntervalSetTracker` takes `IntervalRotator` as a non-nullable parameter and uses `_rotator` internally in `ListCompletedIntervals`. If we pass `null!`, it would crash if `ListCompletedIntervals` is called. Since the `TestIntervalSetTracker` overrides `InitializeAsync` to do nothing (not calling `ListCompletedIntervals`), and `OnIntervalRotatedAsync` also does nothing, passing null should be safe.

Actually, we need `CurrentSnapshot()` to be callable. The base class implementation uses `_lock` and `_currentSet` (both set in the class body). Since the test subclass doesn't call `base.InitializeAsync()`, `_currentSet` remains empty. So override `CurrentSnapshot()` to return `_snapshot` directly.

---

#### `tests/Tracer.Tests.Integration/LiveMultiIntervalQueryTests.cs`

3 integration tests as specified in TRC-P5-001 success condition 5.

Use `ObserverFixture` with real data written across multiple intervals:

```csharp
[Fact]
public async Task LiveQuery_EventsSpanThreeIntervals_AllReturnedByListEndpoint()
{
    // 1. Create ObserverFixture with fast rotation (1-minute intervals)
    // 2. Write N events into interval 1
    // 3. Force rotation via rotator
    // 4. Write N events into interval 2
    // 5. Force rotation
    // 6. Write N events into interval 3 (active)
    // 7. GET /api/sessions to get session list
    // 8. GET /api/events?sessionId=X for the full session range
    // 9. Assert total events == 3*N
    // 10. Assert events are in ascending publishWallclock order
}

[Fact]
public async Task LiveQuery_AfterRotation_IncludesNewInterval()
{
    // 1. Write events, rotate, write more events
    // 2. Query returns events from both intervals
}

[Fact]
public async Task LiveQuery_AfterEviction_ExcludesEvictedInterval()
{
    // 1. Configure CompletedIntervalsToInclude=1
    // 2. Write events into 3 intervals (rotate twice)
    // 3. Run retention (KeepLastNIntervals=1 or 2)
    // 4. Query returns only events from the 1 most recent + active; not the oldest
}
```

**Note**: Since these tests use GET /api/events which doesn't exist until TRC-P5-002, there are two options:
- Either these integration tests are deferred to BATCH-23 (after /api/events is implemented)
- Or use the existing `/api/sessions` and scenario endpoints to verify data visibility

**Resolution**: The TRC-P5-001 success conditions say "GET /api/events?sessionId=X" returns events from all intervals. Since `/api/events` is not yet implemented in this batch, use an alternative verification approach:

Option A: Use the existing sessions endpoint (`GET /api/sessions`) to verify sessions are visible across interval boundaries. The session list shows eventCount which spans intervals.

Option B: Use `fixture.MultiReader.AcquireAsync()` directly in the test to query the multi-interval reader.

Option C: Defer these integration tests to BATCH-23 (when /api/events is added).

**Use Option B for the integration tests**: acquire a connection from `fixture.MultiReader` and run a DuckDB query to count events across intervals. This tests the LiveMultiIntervalReader directly without needing the events endpoint.

---

#### `tests/Tracer.Tests.Integration/RetentionCoordinationTests.cs`

1 test:

```csharp
[Fact]
public async Task Retention_WaitsBeforeDeletion()
{
    // 1. Create RetentionManager with a callback
    // 2. Write interval with _ready sentinel, configure KeepLastNIntervals=0 (delete all)
    // 3. Track when callback fires vs when directory is actually deleted
    // 4. Assert directory still exists when callback fired
    // 5. Assert directory deleted ~30s after callback (or use minimal delay in test with a mock)
}
```

**For the test, use a 100ms delay** instead of the full 30 seconds (make the delay configurable or use a TestRetentionManager that overrides the delay):
- Add a `PreDeletionDelay` property to `RetentionManager` with a default of 30 seconds but overridable for tests.

Actually, looking at the design constraint, the 30-second delay is a specific requirement. For tests, either:
- Skip the timing test (just verify callback fires before deletion)
- Make the delay configurable via constructor parameter

**Recommended**: Add `TimeSpan gracePeriod = default` parameter to `RetentionManager` constructor with default 30s, but tests can pass 100ms.

---

#### `tests/Tracer.Tests.Unit/Observer/ObserverDiTests.cs`

```csharp
[Fact]
public void QueryServices_UseLiveMultiIntervalReader_NotSinglePool()
{
    // Build the Observer application
    // Check that LiveMultiIntervalReader is registered as singleton
    // Check that ReadOnlyConnectionPool is NOT registered in Observer DI
    // Check that SessionQueryService, ScenarioQueryService, TopologyQueryService, EventLookupService
    //   are all registered
}
```

---

## Success Criteria (from TASK-DETAIL.md TRC-P5-001)

1. `IntervalSetTrackerTests` passes all 7 specified tests.
2. `LiveMultiIntervalReaderTests` passes all 5 specified tests.
3. `ObserverHostedService` calls `_tracker.InitializeAsync` then `_multiReader.InitializeAsync` before rotation loop.
4. `ReadOnlyConnectionPool` has zero DI registrations in `ObserverHostBuilder`; all four query services use `LiveMultiIntervalReader`.
5. `LiveMultiIntervalQueryTests` passes 3 integration tests (using direct reader or sessions endpoint as proxy for /api/events).
6. `RetentionManager.ApplyAsync` calls the pre-deletion callback and waits the grace period before deleting.
7. All Phase 1–4 integration tests still pass.
8. `dotnet build Tracer.sln --configuration Release` — 0 errors, 0 warnings.

---

## Checklist

- [ ] `IntervalSetTracker.cs` created
- [ ] `LiveMultiIntervalReader.cs` created (with `PooledMultiIntervalConnection`)
- [ ] `ObserverConfig.cs` updated (add `LiveQueryWindowConfig`)
- [ ] `RetentionManager.cs` updated (add `SetPreDeletionCallback`, async apply, grace period)
- [ ] `ObserverHostedService.cs` updated (uses tracker + multiReader instead of pool)
- [ ] `ObserverHostBuilder.cs` updated (registers tracker + multiReader, wires pre-deletion callback)
- [ ] `SessionQueryService.cs` migrated to `LiveMultiIntervalReader`
- [ ] `ScenarioQueryService.cs` migrated
- [ ] `TopologyQueryService.cs` migrated
- [ ] `EventLookupService.cs` migrated
- [ ] `ObserverFixture.cs` updated (`MultiReader` property instead of `Pool`)
- [ ] `WebApiFixture.cs` updated (stub tracker + reader for endpoint tests)
- [ ] `ObserverHostedServiceTests.cs` updated (uses tracking tracker + reader subclasses)
- [ ] `IntervalSetTrackerTests.cs` created (7 tests)
- [ ] `LiveMultiIntervalReaderTests.cs` created (5 tests)
- [ ] `LiveMultiIntervalQueryTests.cs` created (3 integration tests)
- [ ] `RetentionCoordinationTests.cs` created (1 test)
- [ ] `ObserverDiTests.cs` created (1 test)
- [ ] All existing Phase 1–4 tests still pass
- [ ] `dotnet build Tracer.sln --configuration Release` succeeds
