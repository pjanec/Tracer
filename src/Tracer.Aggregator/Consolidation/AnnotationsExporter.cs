using System.Text.Json;
using System.Text.Json.Serialization;
using Tracer.Storage.Annotations;

namespace Tracer.Aggregator.Consolidation;

public static class AnnotationsExporter
{
    public static async Task ExportAsync(
        IAnnotationStore liveStore,
        string sessionId,
        string bundleStagingPath,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(liveStore);
        var annotations = await liveStore.ExportAllForSessionAsync(sessionId, ct);
        if (annotations.Count == 0) return;

        var annotationsDir = Path.Combine(bundleStagingPath, "annotations");
        Directory.CreateDirectory(annotationsDir);
        var path = Path.Combine(annotationsDir, "annotations.json");

        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, annotations,
            new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true,
                Converters = { new JsonStringEnumConverter() },
            }, ct);
    }
}
