using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Tracer.WebApi.Queries;

/// <summary>
/// File-system-backed metadata service for the bundle library.
/// Reads immutable aggregator-written <c>metadata.json</c> and user-editable <c>bundle-metadata.json</c>.
/// </summary>
public sealed class BundleLibraryService
{
    private readonly string _bundlesRoot;
    private readonly ILogger<BundleLibraryService>? _logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
    };

    public BundleLibraryService(string bundlesRoot, ILogger<BundleLibraryService>? logger = null)
    {
        _bundlesRoot = bundlesRoot;
        _logger = logger;
    }

    public Task<IReadOnlyList<BundleLibraryEntry>> ListAsync(CancellationToken ct = default)
    {
        if (!Directory.Exists(_bundlesRoot))
            return Task.FromResult<IReadOnlyList<BundleLibraryEntry>>(Array.Empty<BundleLibraryEntry>());

        var entries = new List<BundleLibraryEntry>();
        foreach (var dir in Directory.EnumerateDirectories(_bundlesRoot))
        {
            ct.ThrowIfCancellationRequested();
            var metaPath = Path.Combine(dir, "metadata.json");
            if (!File.Exists(metaPath)) continue;

            try
            {
                var entry = BuildEntry(dir);
                if (entry is not null)
                    entries.Add(entry);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to read bundle metadata from {Dir}", dir);
            }
        }
        return Task.FromResult<IReadOnlyList<BundleLibraryEntry>>(entries);
    }

    public async Task<bool> UpdateMetadataAsync(
        string bundleId, BundleMetadataUpdate update, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        var bundleDir = Path.Combine(_bundlesRoot, bundleId);
        if (!Directory.Exists(bundleDir)) return false;

        var userMetaPath = Path.Combine(bundleDir, "bundle-metadata.json");
        BundleUserMetadata existing = ReadUserMetadata(userMetaPath) ?? new BundleUserMetadata();

        var updated = existing with
        {
            Label           = update.Label            ?? existing.Label,
            Description     = update.Description      ?? existing.Description,
            Tags            = update.Tags             ?? existing.Tags,
            IsArchived      = update.IsArchived        ?? existing.IsArchived,
            LastOpenedAtUtc = update.LastOpenedAtUtc   ?? existing.LastOpenedAtUtc,
        };

        await WriteUserMetadataAsync(userMetaPath, updated, ct);
        return true;
    }

    public Task<bool> RecordOpenedAsync(string bundleId, CancellationToken ct = default)
        => UpdateMetadataAsync(bundleId, new BundleMetadataUpdate { LastOpenedAtUtc = DateTimeOffset.UtcNow }, ct);

    public Task<bool> DeleteAsync(string bundleId, CancellationToken ct = default)
    {
        var bundleDir = Path.Combine(_bundlesRoot, bundleId);
        if (!Directory.Exists(bundleDir))
            return Task.FromResult(false);
        Directory.Delete(bundleDir, recursive: true);
        return Task.FromResult(true);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private BundleLibraryEntry? BuildEntry(string bundleDir)
    {
        var metaPath = Path.Combine(bundleDir, "metadata.json");
        var aggMeta = ReadAggregatorMetadata(metaPath);
        if (aggMeta is null) return null;

        var userMetaPath = Path.Combine(bundleDir, "bundle-metadata.json");
        var userMeta = ReadUserMetadata(userMetaPath) ?? new BundleUserMetadata();

        var bundleId = Path.GetFileName(bundleDir);
        var sizeBytes = ComputeDirectorySize(bundleDir);

        return new BundleLibraryEntry
        {
            BundleId        = aggMeta.BundleId ?? bundleId,
            SessionId       = aggMeta.SessionContext?.SessionId ?? bundleId,
            Label           = userMeta.Label,
            Description     = userMeta.Description,
            Tags            = userMeta.Tags,
            IsArchived      = userMeta.IsArchived,
            BuiltAtUtc      = aggMeta.CreatedAtUtc,
            SizeBytes       = sizeBytes,
            LastOpenedAtUtc = userMeta.LastOpenedAtUtc,
            SessionStartUtc = aggMeta.TimeRange?.StartUtc,
            SessionEndUtc   = aggMeta.TimeRange?.EndUtc,
        };
    }

    private static AggregatorMetadata? ReadAggregatorMetadata(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AggregatorMetadata>(json, JsonOpts);
        }
        catch { return null; }
    }

    private static BundleUserMetadata? ReadUserMetadata(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<BundleUserMetadata>(json, JsonOpts);
        }
        catch { return null; }
    }

    private static async Task WriteUserMetadataAsync(
        string path, BundleUserMetadata meta, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(meta, JsonOpts);
        var tmp = path + ".tmp";
        await File.WriteAllTextAsync(tmp, json, ct);
        File.Move(tmp, path, overwrite: true);
    }

    internal static long ComputeDirectorySize(string dir)
    {
        if (!Directory.Exists(dir)) return 0;
        return Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
            .Sum(f => new FileInfo(f).Length);
    }

    private sealed class AggregatorMetadata
    {
        public string? BundleId { get; init; }
        public DateTimeOffset CreatedAtUtc { get; init; }
        public AggregatorTimeRange? TimeRange { get; init; }
        public AggregatorSessionContext? SessionContext { get; init; }
    }

    private sealed class AggregatorTimeRange
    {
        public DateTimeOffset StartUtc { get; init; }
        public DateTimeOffset? EndUtc { get; init; }
    }

    private sealed class AggregatorSessionContext
    {
        public string? SessionId { get; init; }
    }
}

public sealed record BundleLibraryEntry
{
    public required string BundleId { get; init; }
    public required string SessionId { get; init; }
    public string? Label { get; init; }
    public string? Description { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();
    public bool IsArchived { get; init; }
    public DateTimeOffset BuiltAtUtc { get; init; }
    public long SizeBytes { get; init; }
    public DateTimeOffset? LastOpenedAtUtc { get; init; }
    public DateTimeOffset? SessionStartUtc { get; init; }
    public DateTimeOffset? SessionEndUtc { get; init; }
}

public sealed record BundleUserMetadata
{
    public string? Label { get; init; }
    public string? Description { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();
    public bool IsArchived { get; init; }
    public DateTimeOffset? LastOpenedAtUtc { get; init; }
}

public sealed record BundleMetadataUpdate
{
    public string? Label { get; init; }
    public string? Description { get; init; }
    public IReadOnlyList<string>? Tags { get; init; }
    public bool? IsArchived { get; init; }
    public DateTimeOffset? LastOpenedAtUtc { get; init; }
}
