using System.Collections.Concurrent;
using System.IO.Compression;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Tracer.Core.Abstractions;

namespace Tracer.Adapters.Mock.Upload;

/// <summary>
/// Local-filesystem upload service for development and testing.
/// Zips interval files into a single archive in a "fake NAS" directory tree.
/// </summary>
public sealed class LocalFileSystemUploadService : ITelemetryUploadService
{
    private readonly string _root;
    private readonly ILogger<LocalFileSystemUploadService> _logger;
    private readonly ConcurrentDictionary<string, UploadStatus> _statuses = new();

    public LocalFileSystemUploadService(string root)
        : this(root, NullLogger<LocalFileSystemUploadService>.Instance) { }

    public LocalFileSystemUploadService(string root, ILogger<LocalFileSystemUploadService> logger)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(logger);
        if (!Path.IsPathFullyQualified(root))
            throw new ArgumentException("root must be an absolute path", nameof(root));
        _root = root;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<UploadIntentId> RequestUploadAsync(UploadRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var id = new UploadIntentId(Guid.NewGuid().ToString("N"));

        try
        {
            var destDir = Path.Combine(_root, request.NodeId.Value);
            Directory.CreateDirectory(destDir);
            var zipPath = Path.Combine(destDir, $"{request.Interval.Value}.zip");

            if (File.Exists(zipPath))
                File.Delete(zipPath);

            using var zipFs = File.Create(zipPath);
            using var archive = new ZipArchive(zipFs, ZipArchiveMode.Create, leaveOpen: false);

            foreach (var file in request.Files)
            {
                if (!File.Exists(file.Path)) continue;

                var entryName = Path.GetFileName(file.Path);
                if (file.Path.Contains("fast_state"))
                    entryName = "fast_state/" + entryName;

                var compression = file.Path.EndsWith(".parquet", StringComparison.OrdinalIgnoreCase)
                    ? CompressionLevel.NoCompression
                    : CompressionLevel.Optimal;

                var entry = archive.CreateEntry(entryName, compression);
                using var entryStream = entry.Open();
                using var fileStream = File.OpenRead(file.Path);
                fileStream.CopyTo(entryStream);
            }

            _statuses[id.Value] = UploadStatus.Complete;
            _logger.LogInformation(
                "Mock upload complete: {NodeId}/{Interval} -> {Target}",
                request.NodeId.Value, request.Interval.Value, zipPath);
        }
        catch (Exception ex)
        {
            _statuses[id.Value] = UploadStatus.Failed;
            _logger.LogError(ex, "Mock upload failed: {NodeId}/{Interval}",
                request.NodeId.Value, request.Interval.Value);
        }

        return Task.FromResult(id);
    }

    /// <inheritdoc />
    public Task<UploadStatus> GetStatusAsync(UploadIntentId intentId, CancellationToken ct)
    {
        var status = _statuses.TryGetValue(intentId.Value, out var s) ? s : UploadStatus.Unknown;
        return Task.FromResult(status);
    }
}
