using Microsoft.Extensions.Logging;
using Tracer.Agent.Lifecycle;
using Tracer.Agent.Storage;
using Tracer.Core.Domain;

namespace Tracer.Storage.DuckDB.MultiInterval;

/// <summary>
/// Tracks the current set of intervals (active + completed) and fires
/// <see cref="SetChanged"/> whenever the set changes.
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
    /// Scans the data directory, populates the interval set, and fires <see cref="SetChanged"/>.
    /// Must be called after the rotator has opened the current interval.
    /// </summary>
    public virtual async Task InitializeAsync(CancellationToken ct)
    {
        lock (_lock)
        {
            _currentSet.Clear();
            RebuildFromFilesystem_Locked();
        }

        _logger.LogDebug("IntervalSetTracker initialized: {Count} intervals", GetSnapshotCount());
        await NotifyAsync(ct);
    }

    /// <summary>
    /// Called after a rotation: rescans completed intervals and updates the active one.
    /// </summary>
    public virtual async Task OnIntervalRotatedAsync(CancellationToken ct)
    {
        lock (_lock)
        {
            _currentSet.Clear();
            RebuildFromFilesystem_Locked();
        }

        _logger.LogDebug("IntervalSetTracker updated after rotation: {Count} intervals", GetSnapshotCount());
        await NotifyAsync(ct);
    }

    /// <summary>
    /// Called before a completed interval is deleted. Removes it from the tracked set
    /// and fires <see cref="SetChanged"/> so consumers can release their handles.
    /// </summary>
    public virtual async Task OnIntervalEvictedAsync(IntervalDirectory evicted, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(evicted);

        bool changed;
        lock (_lock)
        {
            var before = _currentSet.Count;
            _currentSet.RemoveAll(r => r.Directory.RootPath == evicted.RootPath);
            changed = _currentSet.Count != before;
        }

        if (changed)
        {
            _logger.LogDebug("IntervalSetTracker: evicted {Interval}", evicted.Timestamp.Value);
            await NotifyAsync(ct);
        }
    }

    public virtual IntervalSetSnapshot CurrentSnapshot()
    {
        lock (_lock)
            return new IntervalSetSnapshot(new List<IntervalReference>(_currentSet).AsReadOnly());
    }

    /// <summary>
    /// Returns all completed intervals (those with <c>_ready</c> sentinel) from the filesystem,
    /// excluding the currently active interval.
    /// </summary>
    protected virtual IEnumerable<IntervalDirectory> ListCompletedIntervals()
    {
        var dataRoot = _rotator.CurrentDirectory?.DataRoot
            ?? throw new InvalidOperationException(
                "IntervalRotator has no current directory; call OpenCurrentAsync first.");

        var intervalsRoot = Path.Combine(dataRoot, "intervals");
        if (!Directory.Exists(intervalsRoot))
            return [];

        var activeTimestamp = _rotator.CurrentDirectory?.Timestamp.Value;

        return Directory.EnumerateDirectories(intervalsRoot)
            .Where(d =>
            {
                var name = Path.GetFileName(d);
                if (name == activeTimestamp) return false;
                if (!IntervalTimestamp.TryParse(name, out _)) return false;
                return File.Exists(Path.Combine(d, "_ready"));
            })
            .Select(d =>
            {
                var name = Path.GetFileName(d)!;
                IntervalTimestamp.TryParse(name, out var ts);
                return new IntervalDirectory(_rotator.CurrentDirectory!.DataRoot, ts);
            });
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private void RebuildFromFilesystem_Locked()
    {
        var completed = ListCompletedIntervals().ToList();

        // Sort ascending (oldest first), then keep only the most recent N
        completed.Sort((a, b) => string.Compare(
            a.Timestamp.Value, b.Timestamp.Value, StringComparison.Ordinal));

        var toKeep = completed.Skip(Math.Max(0, completed.Count - _completedIntervalsToKeep));
        foreach (var dir in toKeep)
            _currentSet.Add(new IntervalReference(dir, IntervalRole.Completed));

        if (_rotator.CurrentDirectory is not null)
            _currentSet.Add(new IntervalReference(_rotator.CurrentDirectory, IntervalRole.Active));
    }

    protected async Task NotifyAsync(CancellationToken ct)
    {
        var h = SetChanged;
        if (h is not null)
            await h(CurrentSnapshot(), ct);
    }

    private int GetSnapshotCount()
    {
        lock (_lock) return _currentSet.Count;
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
