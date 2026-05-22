namespace Tracer.Storage.SavedViews;

public interface ISavedViewStore
{
    Task<IReadOnlyList<SavedViewRecord>> ListAsync(SavedViewFilter filter, CancellationToken ct);
    Task<SavedViewRecord?> GetAsync(string savedViewId, CancellationToken ct);
    Task<SavedViewRecord> CreateAsync(SavedViewRecord record, CancellationToken ct);
    Task<SavedViewRecord?> UpdateAsync(SavedViewRecord record, CancellationToken ct);
    Task<bool> DeleteAsync(string savedViewId, CancellationToken ct);
    Task RecordOpenedAsync(string savedViewId, CancellationToken ct);
}
