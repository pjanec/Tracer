using System.Text.Json;

namespace Tracer.Storage.SavedQueries.BuiltIn;

public static class BuiltInLoader
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public static async Task EnsureLoadedAsync(ISavedQueryStore store, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(store);

        var existing = await store.ListAsync(new SavedQueryFilter { IsBuiltIn = true }, ct);
        var existingIds = existing.Select(q => q.SavedQueryId).ToHashSet(StringComparer.Ordinal);

        var resourceStream = typeof(BuiltInLoader).Assembly.GetManifestResourceStream(
            "Tracer.Storage.SavedQueries.BuiltIn.builtin-queries.json");
        if (resourceStream is null) return;

        await using (resourceStream)
        {
            var dtos = await JsonSerializer.DeserializeAsync<List<BuiltInQueryDto>>(
                resourceStream, JsonOpts, ct);
            if (dtos is null) return;

            foreach (var dto in dtos)
            {
                if (existingIds.Contains(dto.Id)) continue;
                await store.CreateAsync(new SavedQueryRecord
                {
                    SavedQueryId = dto.Id,
                    Label        = dto.Label,
                    Description  = dto.Description,
                    Sql          = dto.Sql,
                    Parameters   = dto.Parameters ?? Array.Empty<SavedQueryParameter>(),
                    Tags         = dto.Tags ?? Array.Empty<string>(),
                    IsBuiltIn    = true,
                    IsFavorite   = false,
                    Author       = "tracer",
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    RunCount     = 0,
                }, ct);
            }
        }
    }

    private sealed class BuiltInQueryDto
    {
        public string Id { get; init; } = "";
        public string Label { get; init; } = "";
        public string? Description { get; init; }
        public string Sql { get; init; } = "";
        public IReadOnlyList<SavedQueryParameter>? Parameters { get; init; }
        public IReadOnlyList<string>? Tags { get; init; }
    }
}
