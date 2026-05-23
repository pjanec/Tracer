using System.Text.Json;
using System.Text.Json.Serialization;
using Tracer.Storage.SavedViews;

namespace Tracer.Aggregator.Consolidation;

public static class SavedViewsExporter
{
    public static async Task ExportAsync(
        ISavedViewStore liveStore,
        string sessionId,
        string bundleStagingPath,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(liveStore);
        var views = await liveStore.ListAsync(
            new SavedViewFilter { SessionId = sessionId, Limit = int.MaxValue }, ct);
        if (views.Count == 0) return;

        var savedViewsDir = Path.Combine(bundleStagingPath, "saved_views");
        Directory.CreateDirectory(savedViewsDir);
        var path = Path.Combine(savedViewsDir, "saved_views.json");

        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, views,
            new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true,
                Converters = { new JsonStringEnumConverter() },
            }, ct);
    }
}
