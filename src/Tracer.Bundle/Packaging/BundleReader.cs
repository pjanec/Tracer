using System.IO.Compression;
using System.Text.Json;
using Tracer.Bundle.Format;

namespace Tracer.Bundle.Packaging;

/// <summary>
/// Opens a bundle that is either a directory or a .zip file and returns the manifest.
/// </summary>
public static class BundleReader
{
    /// <summary>
    /// Reads and returns the <see cref="BundleManifest"/> from a bundle.
    /// Supports both directory bundles and .zip bundles.
    /// For .zip bundles, reads manifest.json from the archive without extracting to disk.
    /// </summary>
    public static async Task<BundleManifest> ReadManifestAsync(
        string bundlePath,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(bundlePath);

        if (Directory.Exists(bundlePath))
        {
            var manifestPath = Path.Combine(bundlePath, BundleLayout.ManifestFile);
            await using var stream = File.OpenRead(manifestPath);
            return await DeserializeAsync(stream, ct);
        }

        if (File.Exists(bundlePath))
        {
            using var archive = ZipFile.OpenRead(bundlePath);
            var entry = archive.GetEntry(BundleLayout.ManifestFile)
                ?? throw new InvalidOperationException($"Bundle ZIP does not contain {BundleLayout.ManifestFile}");
            await using var entryStream = entry.Open();
            return await DeserializeAsync(entryStream, ct);
        }

        throw new FileNotFoundException($"Bundle not found at: {bundlePath}", bundlePath);
    }

    private static async Task<BundleManifest> DeserializeAsync(Stream stream, CancellationToken ct)
    {
        var manifest = await JsonSerializer.DeserializeAsync<BundleManifest>(
            stream, BundleManifest.SerializerOptions, ct);
        return manifest ?? throw new InvalidOperationException("Manifest deserialized to null.");
    }
}
