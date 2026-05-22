using Tracer.Bundle.Format;
using Tracer.Storage.DuckDB.MultiInterval;

namespace Tracer.WebApi.Queries;

/// <summary>
/// Locates fast-state Parquet files for a given topic/entity combination,
/// searching across live intervals and optionally an open bundle.
/// </summary>
public sealed class FastStateFileLocator
{
    private readonly IntervalSetTracker _tracker;
    private readonly Func<string?>? _getBundleWorkingDirectory;

    /// <param name="getBundleWorkingDirectory">
    /// Optional delegate returning the current bundle's working directory (or null when no bundle is open).
    /// Pass <c>() =&gt; bundleOpenManager.Current?.WorkingDirectory</c> from the OfflineViewer layer.
    /// </param>
    public FastStateFileLocator(IntervalSetTracker tracker, Func<string?>? getBundleWorkingDirectory = null)
    {
        _tracker = tracker;
        _getBundleWorkingDirectory = getBundleWorkingDirectory;
    }

    /// <summary>
    /// Returns all existing Parquet file paths for the given topic and entity,
    /// covering live intervals and (if open) the current bundle.
    /// </summary>
    public IReadOnlyList<string> LocateFiles(string topic, string entityId)
    {
        var safeTopic = BundleNaming.SafeFileName(topic);
        var safeEntity = BundleNaming.SafeFileName(entityId);
        var snapshot = _tracker.CurrentSnapshot();
        var paths = new List<string>();

        foreach (var iv in snapshot.Intervals)
        {
            var candidate = Path.Combine(
                iv.Directory.FastStateDirectory, safeTopic, safeEntity, "samples.parquet");
            if (File.Exists(candidate))
                paths.Add(candidate);
        }

        if (_getBundleWorkingDirectory?.Invoke() is { } bundleDir)
        {
            var bundleCandidate = Path.Combine(
                bundleDir, "fast_state", safeTopic, safeEntity, "samples.parquet");
            if (File.Exists(bundleCandidate))
                paths.Add(bundleCandidate);
        }

        return paths;
    }

    /// <summary>
    /// Returns the topic names (as safe filenames) for which this entity has fast-state data,
    /// across live intervals and (if open) the current bundle.
    /// </summary>
    public IReadOnlyList<string> GetAvailableTopicsForEntity(string entityId)
    {
        var safeEntity = BundleNaming.SafeFileName(entityId);
        var topics = new HashSet<string>();
        var snapshot = _tracker.CurrentSnapshot();

        foreach (var iv in snapshot.Intervals)
            AddTopicsFromRoot(iv.Directory.FastStateDirectory, safeEntity, topics);

        if (_getBundleWorkingDirectory?.Invoke() is { } bundleDir)
            AddTopicsFromRoot(Path.Combine(bundleDir, "fast_state"), safeEntity, topics);

        return topics.ToList();
    }

    private static void AddTopicsFromRoot(string fastStateDir, string safeEntity, HashSet<string> topics)
    {
        if (!Directory.Exists(fastStateDir)) return;
        foreach (var topicDir in Directory.EnumerateDirectories(fastStateDir))
        {
            if (Directory.Exists(Path.Combine(topicDir, safeEntity)))
                topics.Add(Path.GetFileName(topicDir)!);
        }
    }
}
