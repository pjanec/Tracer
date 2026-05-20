using System.IO.Compression;

namespace Tracer.Bundle.Packaging;

/// <summary>
/// Extracts a .tracerbundle.zip to a target directory.
/// </summary>
public static class BundleExtractor
{
    /// <summary>
    /// Extracts all entries of the ZIP archive at <paramref name="zipPath"/>
    /// into <paramref name="targetDirectory"/>.
    /// </summary>
    public static async Task ExtractAsync(
        string zipPath,
        string targetDirectory,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(zipPath);
        ArgumentNullException.ThrowIfNull(targetDirectory);

        await Task.Run(() => ZipFile.ExtractToDirectory(zipPath, targetDirectory), ct);
    }
}
