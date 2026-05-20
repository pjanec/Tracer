using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Tracer.Bundle.Format;
using Tracer.Bundle.Packaging;
using Tracer.WebApi.Contracts.Dto;

namespace Tracer.WebApi.Bundles;

/// <summary>In-memory registry of bundles built by this observer instance.</summary>
public sealed class BundleCatalog
{
    private readonly ConcurrentDictionary<string, BundleEntry> _entries = new();
    private readonly ILogger<BundleCatalog> _logger;

    public string BundlesRoot { get; }

    public BundleCatalog(string bundlesRoot, ILogger<BundleCatalog> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bundlesRoot);
        BundlesRoot = bundlesRoot;
        _logger = logger;
    }

    public Task RegisterAsync(string bundleId, string outputPath, CancellationToken ct = default)
    {
        _entries[bundleId] = new BundleEntry { BundleId = bundleId, Path = outputPath };
        return Task.CompletedTask;
    }

    public Task<BundleEntry?> GetAsync(string bundleId, CancellationToken ct = default)
    {
        _entries.TryGetValue(bundleId, out var entry);
        return Task.FromResult(entry);
    }

    public async Task<BundleManifestDto?> GetManifestAsync(string bundleId, CancellationToken ct = default)
    {
        var entry = await GetAsync(bundleId, ct);
        if (entry is null) return null;
        try
        {
            var manifest = await BundleReader.ReadManifestAsync(entry.Path, ct);
            return MapToDto(manifest);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read manifest for bundle {BundleId}", bundleId);
            return null;
        }
    }

    public async Task<IReadOnlyList<BundleListEntryDto>> ListAsync(CancellationToken ct = default)
    {
        var results = new List<BundleListEntryDto>();
        foreach (var (id, entry) in _entries)
        {
            try
            {
                var manifest = await BundleReader.ReadManifestAsync(entry.Path, ct);
                results.Add(new BundleListEntryDto
                {
                    BundleId = id,
                    CreatedAtUtc = manifest.CreatedAtUtc,
                    TimeRange = new TimeRangeDto
                    {
                        StartUtc = manifest.TimeRange.StartUtc,
                        EndUtc = manifest.TimeRange.EndUtc,
                    },
                    SizeBytes = GetSize(entry.Path),
                    Label = manifest.SessionContext?.Label,
                    SessionId = manifest.SessionContext?.SessionId,
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Skipping unreadable bundle {BundleId}", id);
            }
        }
        return results;
    }

    public Task<bool> DeleteAsync(string bundleId, CancellationToken ct = default)
    {
        if (!_entries.TryRemove(bundleId, out var entry)) return Task.FromResult(false);
        try
        {
            if (Directory.Exists(entry.Path))
                Directory.Delete(entry.Path, recursive: true);
            else if (File.Exists(entry.Path))
                File.Delete(entry.Path);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete bundle {BundleId} at {Path}", bundleId, entry.Path);
        }
        return Task.FromResult(true);
    }

    private static long GetSize(string path)
    {
        if (Directory.Exists(path))
            return Directory.GetFiles(path, "*", SearchOption.AllDirectories)
                .Sum(f => new FileInfo(f).Length);
        if (File.Exists(path))
            return new FileInfo(path).Length;
        return 0;
    }

    private static BundleManifestDto MapToDto(BundleManifest m) => new()
    {
        BundleId = m.BundleId,
        SchemaVersion = m.SchemaVersion,
        CreatedAtUtc = m.CreatedAtUtc,
        TracerVersion = m.TracerVersion,
        Writer = new BundleWriterInfoDto { Tool = m.Writer.Tool, Version = m.Writer.Version, Host = m.Writer.Host },
        TimeRange = new TimeRangeDto { StartUtc = m.TimeRange.StartUtc, EndUtc = m.TimeRange.EndUtc },
        SessionContext = new BundleSessionContextDto
        {
            SessionId = m.SessionContext?.SessionId,
            ScenarioId = m.SessionContext?.ScenarioId,
            Label = m.SessionContext?.Label,
        },
        ParticipatingNodes = m.ParticipatingNodes,
        FastStateScope = m.FastStateScope,
        Statistics = new BundleStatisticsDto
        {
            TotalEvents = m.Statistics.TotalEvents,
            TotalSlowStateSamples = m.Statistics.TotalSlowStateSamples,
            TotalFastStateRows = m.Statistics.TotalFastStateRows,
            UncompressedBytes = m.Statistics.UncompressedBytes,
        },
    };
}

public sealed record BundleEntry
{
    public required string BundleId { get; init; }
    public required string Path { get; init; }
    public bool IsZipped => Path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
}
