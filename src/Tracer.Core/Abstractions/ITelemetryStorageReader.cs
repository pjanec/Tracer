using Tracer.Core.Domain;

namespace Tracer.Core.Abstractions;

/// <summary>
/// Reads completed interval data from the storage location (NAS or local filesystem mock).
/// </summary>
public interface ITelemetryStorageReader
{
    /// <summary>Returns the IDs of all nodes that have at least one uploaded interval.</summary>
    Task<IReadOnlyList<string>> ListNodesAsync(CancellationToken ct = default);

    /// <summary>Returns all uploaded intervals for the given node, ordered by start time.</summary>
    Task<IReadOnlyList<IntervalDescriptor>> ListIntervalsAsync(string nodeId, CancellationToken ct = default);

    /// <summary>
    /// Reads and returns the <see cref="IntervalManifest"/> for the specified interval.
    /// Returns <c>null</c> if the interval archive does not contain a manifest.
    /// </summary>
    Task<IntervalManifest?> ReadIntervalManifestAsync(
        string nodeId,
        IntervalDescriptor descriptor,
        CancellationToken ct = default);

    /// <summary>
    /// Returns the absolute path to the ZIP archive for the specified interval.
    /// Does not check whether the file exists.
    /// </summary>
    string GetIntervalZipPath(string nodeId, IntervalDescriptor descriptor);
}
