namespace Tracer.Storage.SavedQueries;

public interface ISavedQueryStore
{
    Task<IReadOnlyList<SavedQueryRecord>> ListAsync(SavedQueryFilter filter, CancellationToken ct = default);
    Task<SavedQueryRecord?> GetAsync(string savedQueryId, CancellationToken ct = default);
    Task<SavedQueryRecord> CreateAsync(SavedQueryRecord record, CancellationToken ct = default);
    Task<SavedQueryRecord?> UpdateAsync(SavedQueryRecord record, CancellationToken ct = default);
    Task<bool> DeleteAsync(string savedQueryId, CancellationToken ct = default);
    Task IncrementRunCountAsync(string savedQueryId, CancellationToken ct = default);
    /// <summary>Toggles the IsFavorite flag; works for both user and built-in queries.</summary>
    Task<SavedQueryRecord?> ToggleFavoriteAsync(string savedQueryId, CancellationToken ct = default);
}
