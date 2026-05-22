using System.IO.Compression;

namespace Tracer.WebApi.Queries;

/// <summary>
/// Streams a bundle directory as a zip archive to the given destination stream.
/// No temp file is created — the zip is written directly to the stream.
/// </summary>
public sealed class BundleExportService
{
    private readonly string _bundlesRoot;

    public BundleExportService(string bundlesRoot)
    {
        _bundlesRoot = bundlesRoot;
    }

    /// <summary>
    /// Exports the bundle with <paramref name="bundleId"/> as a zip to <paramref name="destination"/>.
    /// Returns <c>false</c> if the bundle directory does not exist.
    /// </summary>
    public async Task<bool> ExportAsync(string bundleId, Stream destination, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(destination);

        var bundleDir = Path.Combine(_bundlesRoot, bundleId);
        if (!Directory.Exists(bundleDir)) return false;

        using var archive = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true);

        foreach (var filePath in Directory.EnumerateFiles(bundleDir, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();

            // Compute relative path (forward slashes, no leading separator)
            var relativePath = Path.GetRelativePath(bundleDir, filePath)
                .Replace('\\', '/');

            // Sanity: never write absolute or path-traversal entries
            if (Path.IsPathRooted(relativePath) || relativePath.Contains(".."))
                continue;

            var entry = archive.CreateEntry(relativePath, CompressionLevel.Fastest);
            entry.LastWriteTime = new DateTimeOffset(File.GetLastWriteTimeUtc(filePath), TimeSpan.Zero);

            await using var entryStream = entry.Open();
            await using var fs = File.OpenRead(filePath);
            await fs.CopyToAsync(entryStream, ct);
        }

        return true;
    }
}
