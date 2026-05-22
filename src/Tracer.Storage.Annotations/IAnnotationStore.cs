namespace Tracer.Storage.Annotations;

public interface IAnnotationStore
{
    Task<IReadOnlyList<AnnotationRecord>> ListAsync(AnnotationFilter filter, CancellationToken ct);
    Task<AnnotationRecord?> GetAsync(string annotationId, CancellationToken ct);
    Task<AnnotationRecord> CreateAsync(AnnotationRecord record, CancellationToken ct);
    Task<AnnotationRecord?> UpdateAsync(AnnotationRecord record, CancellationToken ct);
    Task<bool> DeleteAsync(string annotationId, CancellationToken ct);
    Task<IReadOnlyList<AnnotationRecord>> ExportAllForSessionAsync(string sessionId, CancellationToken ct);
}
