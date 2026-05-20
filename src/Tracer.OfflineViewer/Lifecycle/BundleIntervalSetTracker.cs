using Microsoft.Extensions.Logging;
using Tracer.Agent.Storage;
using Tracer.Core.Domain;
using Tracer.Storage.DuckDB.MultiInterval;

namespace Tracer.OfflineViewer.Lifecycle;

/// <summary>
/// An <see cref="IntervalSetTracker"/> that tracks a single bundle file instead of
/// live Observer intervals.  The bundle is treated as a completed interval so the
/// pool opens it with <c>ATTACH … (READ_ONLY)</c> against an in-memory main connection.
/// </summary>
public sealed class BundleIntervalSetTracker : IntervalSetTracker
{
    private static readonly IntervalSetSnapshot Empty =
        new(Array.Empty<IntervalReference>());

    private volatile IntervalSetSnapshot _snapshot = Empty;

    public BundleIntervalSetTracker(ILogger<IntervalSetTracker> logger)
        : base(null!, 0, logger) { }

    public override Task InitializeAsync(CancellationToken ct) => Task.CompletedTask;
    public override Task OnIntervalRotatedAsync(CancellationToken ct) => Task.CompletedTask;
    public override Task OnIntervalEvictedAsync(IntervalDirectory evicted, CancellationToken ct)
        => Task.CompletedTask;

    public override IntervalSetSnapshot CurrentSnapshot() => _snapshot;

    /// <summary>
    /// Switches the reader pool to the specified bundle events database.
    /// Fires <see cref="IntervalSetTracker.SetChanged"/> so the pool rebuilds.
    /// </summary>
    public async Task SwitchToBundleAsync(string eventsDbPath, CancellationToken ct)
    {
        var dir = IntervalDirectory.ForEventsDb(eventsDbPath);
        var iv = new IntervalReference(dir, IntervalRole.Completed);
        _snapshot = new IntervalSetSnapshot(new[] { iv });
        await NotifyAsync(ct);
    }

    /// <summary>
    /// Clears the current bundle from the pool (e.g., when the viewer is closed).
    /// </summary>
    public async Task ClearAsync(CancellationToken ct)
    {
        _snapshot = Empty;
        await NotifyAsync(ct);
    }
}
