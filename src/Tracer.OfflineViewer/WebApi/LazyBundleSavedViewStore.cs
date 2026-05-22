using Tracer.Storage.SavedViews;

namespace Tracer.OfflineViewer.WebApi;

/// <summary>
/// No-op saved view store for bundle mode.
/// Saved views are not exported into bundles yet (TRC-P8-009 scope covers annotations only).
/// </summary>
public sealed class LazyBundleSavedViewStore : ISavedViewStore
{
    public Task<IReadOnlyList<SavedViewRecord>> ListAsync(SavedViewFilter filter, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<SavedViewRecord>>(Array.Empty<SavedViewRecord>());

    public Task<SavedViewRecord?> GetAsync(string savedViewId, CancellationToken ct) =>
        Task.FromResult<SavedViewRecord?>(null);

    public Task<SavedViewRecord> CreateAsync(SavedViewRecord record, CancellationToken ct) =>
        throw new InvalidOperationException("Bundle saved views are read-only");

    public Task<SavedViewRecord?> UpdateAsync(SavedViewRecord record, CancellationToken ct) =>
        throw new InvalidOperationException("Bundle saved views are read-only");

    public Task<bool> DeleteAsync(string savedViewId, CancellationToken ct) =>
        throw new InvalidOperationException("Bundle saved views are read-only");

    public Task RecordOpenedAsync(string savedViewId, CancellationToken ct) =>
        Task.CompletedTask;
}
