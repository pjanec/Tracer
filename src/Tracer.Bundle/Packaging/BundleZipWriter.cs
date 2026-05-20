using System.IO.Compression;
using Tracer.Bundle.Format;

namespace Tracer.Bundle.Packaging;

/// <summary>
/// Writes a bundle to a staging directory via <see cref="BundleDirectoryWriter"/>,
/// then compresses the result into a ZIP archive.
/// </summary>
public static class BundleZipWriter
{
    /// <summary>
    /// Finalizes the staging directory (via <see cref="BundleDirectoryWriter.WriteAsync"/>)
    /// and creates a ZIP archive at <paramref name="outputZipPath"/>.
    /// </summary>
    public static async Task WriteAsync(
        string stagingPath,
        BundleManifest manifest,
        string outputZipPath,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(stagingPath);
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(outputZipPath);

        await BundleDirectoryWriter.WriteAsync(stagingPath, manifest, ct);
        await Task.Run(() => ZipFile.CreateFromDirectory(stagingPath, outputZipPath), ct);
    }
}
