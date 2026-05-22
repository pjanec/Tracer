using Tracer.OfflineViewer.Lifecycle;
using Tracer.Storage.Annotations;

namespace Tracer.OfflineViewer.WebApi;

public sealed class LazyBundleAnnotationStore : IAnnotationStore
{
    private readonly BundleOpenManager _mgr;

    public LazyBundleAnnotationStore(BundleOpenManager mgr) { _mgr = mgr; }

    private IAnnotationStore? Resolve() =>
        _mgr.Current is { } c ? new BundleAnnotationStore(c.WorkingDirectory) : null;

    public Task<IReadOnlyList<AnnotationRecord>> ListAsync(AnnotationFilter f, CancellationToken ct) =>
        Resolve() is { } s ? s.ListAsync(f, ct)
            : Task.FromResult<IReadOnlyList<AnnotationRecord>>(Array.Empty<AnnotationRecord>());

    public Task<AnnotationRecord?> GetAsync(string id, CancellationToken ct) =>
        Resolve() is { } s ? s.GetAsync(id, ct) : Task.FromResult<AnnotationRecord?>(null);

    public Task<AnnotationRecord> CreateAsync(AnnotationRecord r, CancellationToken ct) =>
        throw new InvalidOperationException("Bundle annotations are read-only");

    public Task<AnnotationRecord?> UpdateAsync(AnnotationRecord r, CancellationToken ct) =>
        throw new InvalidOperationException("Bundle annotations are read-only");

    public Task<bool> DeleteAsync(string id, CancellationToken ct) =>
        throw new InvalidOperationException("Bundle annotations are read-only");

    public Task<IReadOnlyList<AnnotationRecord>> ExportAllForSessionAsync(string sessionId, CancellationToken ct) =>
        Resolve() is { } s ? s.ExportAllForSessionAsync(sessionId, ct)
            : Task.FromResult<IReadOnlyList<AnnotationRecord>>(Array.Empty<AnnotationRecord>());
}
