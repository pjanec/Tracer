using System.Collections.Concurrent;
using Tracer.Core.Abstractions;

namespace Tracer.Adapters.Mock.Upload;

/// <summary>
/// Local-filesystem upload service for development and testing.
/// Copies interval files into a staging directory tree rooted at <see cref="_root"/>.
/// </summary>
public sealed class LocalFileSystemUploadService : ITelemetryUploadService
{
    private readonly string _root;
    private readonly ConcurrentDictionary<string, UploadStatus> _statuses = new();

    public LocalFileSystemUploadService(string root)
    {
        _root = root;
    }

    /// <inheritdoc />
    public Task<UploadIntentId> RequestUploadAsync(UploadRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var id = new UploadIntentId(Guid.NewGuid().ToString("N"));

        try
        {
            var destDir = Path.Combine(_root, request.NodeId.Value, request.Interval.Value);
            Directory.CreateDirectory(destDir);

            foreach (var file in request.Files)
            {
                if (!File.Exists(file.Path)) continue;
                var destPath = Path.Combine(destDir, Path.GetFileName(file.Path));
                File.Copy(file.Path, destPath, overwrite: true);
            }

            _statuses[id.Value] = UploadStatus.Complete;
        }
        catch
        {
            _statuses[id.Value] = UploadStatus.Failed;
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
