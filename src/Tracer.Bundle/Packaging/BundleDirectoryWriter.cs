using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Tracer.Bundle.Format;

namespace Tracer.Bundle.Packaging;

/// <summary>
/// Finalizes a staging directory into a valid bundle by writing manifest.json,
/// computing SHA-256 for every file in manifest.Files, producing checksums.txt,
/// and creating annotations/.keep.
/// </summary>
public static class BundleDirectoryWriter
{
    /// <summary>
    /// Writes manifest.json and checksums.txt into <paramref name="stagingPath"/>,
    /// and creates annotations/.keep. All files listed in <paramref name="manifest"/>.Files
    /// must already exist in the staging directory.
    /// </summary>
    public static async Task WriteAsync(
        string stagingPath,
        BundleManifest manifest,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(stagingPath);
        ArgumentNullException.ThrowIfNull(manifest);

        // 1. Write manifest.json
        var manifestPath = Path.Combine(stagingPath, BundleLayout.ManifestFile);
        await using (var manifestStream = File.Create(manifestPath))
        {
            await JsonSerializer.SerializeAsync(manifestStream, manifest, BundleManifest.SerializerOptions, ct);
        }

        // 2. Compute SHA-256 for each listed file and build checksums.txt
        var sb = new StringBuilder();
        foreach (var entry in manifest.Files)
        {
            var filePath = Path.Combine(stagingPath, entry.Path);
            var hash = await ComputeSha256Async(filePath, ct);
            // sha256sum format: 64-hex + two spaces + relative path
            sb.Append(hash);
            sb.Append("  ");
            sb.Append(entry.Path);
            sb.Append('\n');
        }

        var checksumsPath = Path.Combine(stagingPath, BundleLayout.ChecksumsFile);
        await File.WriteAllTextAsync(checksumsPath, sb.ToString(), Encoding.UTF8, ct);

        // 3. Create annotations/.keep
        var annotationsDir = Path.Combine(stagingPath, BundleLayout.AnnotationsDirectory);
        Directory.CreateDirectory(annotationsDir);
        var keepPath = Path.Combine(stagingPath, BundleLayout.AnnotationsKeepFile);
        if (!File.Exists(keepPath))
            await File.WriteAllBytesAsync(keepPath, Array.Empty<byte>(), ct);
    }

    internal static async Task<string> ComputeSha256Async(string filePath, CancellationToken ct = default)
    {
        await using var stream = File.OpenRead(filePath);
        var hashBytes = await SHA256.HashDataAsync(stream, ct);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}
