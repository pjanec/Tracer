using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tracer.Storage.Annotations;

public sealed class BundleAnnotationStore : IAnnotationStore
{
    private readonly string _bundleAnnotationsPath;
    private IReadOnlyList<AnnotationRecord>? _cache;

    private static readonly JsonSerializerOptions s_readOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    public BundleAnnotationStore(string bundlePath)
    {
        _bundleAnnotationsPath = Path.Combine(bundlePath, "annotations", "annotations.json");
    }

    private async Task<IReadOnlyList<AnnotationRecord>> LoadAsync(CancellationToken ct)
    {
        if (_cache is not null) return _cache;
        if (!File.Exists(_bundleAnnotationsPath))
        {
            _cache = Array.Empty<AnnotationRecord>();
            return _cache;
        }
        await using var stream = File.OpenRead(_bundleAnnotationsPath);
        var list = await JsonSerializer.DeserializeAsync<List<AnnotationRecord>>(stream, s_readOptions, ct);
        _cache = list ?? new List<AnnotationRecord>();
        return _cache;
    }

    public async Task<IReadOnlyList<AnnotationRecord>> ListAsync(AnnotationFilter filter, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var all = await LoadAsync(ct);
        IEnumerable<AnnotationRecord> q = all;
        if (filter.SessionId is not null)  q = q.Where(a => a.SessionId == filter.SessionId);
        if (filter.Kind is { } k)          q = q.Where(a => a.Kind == k);
        if (filter.EventId is not null)    q = q.Where(a => a.EventId == filter.EventId);
        if (filter.EntityId is not null)   q = q.Where(a => a.EntityId == filter.EntityId);
        if (filter.TraceId is not null)    q = q.Where(a => a.TraceId == filter.TraceId);
        if (filter.FromUtc is { } from)    q = q.Where(a => a.CreatedAtUtc >= from);
        if (filter.ToUtc is { } to)        q = q.Where(a => a.CreatedAtUtc < to);
        return q.OrderByDescending(a => a.CreatedAtUtc).Take(filter.Limit).ToList();
    }

    public async Task<AnnotationRecord?> GetAsync(string annotationId, CancellationToken ct)
    {
        var all = await LoadAsync(ct);
        return all.FirstOrDefault(a => a.AnnotationId == annotationId);
    }

    public Task<AnnotationRecord> CreateAsync(AnnotationRecord record, CancellationToken ct)
        => throw new InvalidOperationException("Bundle annotations are read-only");

    public Task<AnnotationRecord?> UpdateAsync(AnnotationRecord record, CancellationToken ct)
        => throw new InvalidOperationException("Bundle annotations are read-only");

    public Task<bool> DeleteAsync(string annotationId, CancellationToken ct)
        => throw new InvalidOperationException("Bundle annotations are read-only");

    public async Task<IReadOnlyList<AnnotationRecord>> ExportAllForSessionAsync(
        string sessionId, CancellationToken ct)
        => (await LoadAsync(ct)).Where(a => a.SessionId == sessionId).ToList();
}
