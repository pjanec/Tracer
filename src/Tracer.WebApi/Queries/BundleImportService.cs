using System.IO.Compression;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Tracer.Bundle.Format;
using Tracer.Bundle.Packaging;
using Tracer.Bundle.Validation;

namespace Tracer.WebApi.Queries;

/// <summary>
/// Extracts a bundle zip to a temp directory, validates it, then atomically renames to the final location.
/// Implements zip-slip defense and extension allow-listing.
/// </summary>
public sealed class BundleImportService
{
    private readonly string _bundlesRoot;
    private readonly ILogger<BundleImportService> _logger;
    private const long MaxImportBytes = 10L * 1024 * 1024 * 1024; // 10 GB

    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".parquet", ".json", ".db",
    };

    public BundleImportService(string bundlesRoot, ILogger<BundleImportService> logger)
    {
        _bundlesRoot = bundlesRoot;
        _logger = logger;
    }

    public async Task<BundleImportResult> ImportAsync(Stream zipStream, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(zipStream);
        Directory.CreateDirectory(_bundlesRoot);

        // Read into memory (bounded by MaxImportBytes)
        using var ms = new MemoryStream();
        try
        {
            await zipStream.CopyToAsync(ms, ct);
        }
        catch (Exception ex)
        {
            return BundleImportResult.InvalidFormat($"Failed to read upload: {ex.Message}");
        }

        if (ms.Length > MaxImportBytes)
            return BundleImportResult.InvalidFormat($"Upload exceeds maximum size of {MaxImportBytes} bytes");

        ms.Position = 0;

        ZipArchive archive;
        try
        {
            archive = new ZipArchive(ms, ZipArchiveMode.Read, leaveOpen: true);
        }
        catch (Exception ex)
        {
            return BundleImportResult.InvalidFormat($"Not a valid zip archive: {ex.Message}");
        }

        using (archive)
        {
            // Validate all entries before extracting
            foreach (var entry in archive.Entries)
            {
                if (entry.FullName.Contains("..") || Path.IsPathRooted(entry.FullName)
                    || entry.FullName.StartsWith('/') || entry.FullName.StartsWith('\\'))
                    return BundleImportResult.InvalidFormat($"Zip-slip detected: {entry.FullName}");

                var ext = Path.GetExtension(entry.FullName);
                if (!string.IsNullOrEmpty(ext) && !AllowedExtensions.Contains(ext))
                    return BundleImportResult.InvalidFormat($"Unexpected file extension: {entry.FullName}");
            }

            // Determine bundle ID from metadata.json
            var metaEntry = archive.GetEntry("metadata.json");
            if (metaEntry is null)
                return BundleImportResult.InvalidFormat("Archive does not contain metadata.json");

            string bundleId;
            BundleManifest? manifest;
            try
            {
                await using var metaStream = metaEntry.Open();
                manifest = await JsonSerializer.DeserializeAsync<BundleManifest>(metaStream,
                    BundleManifest.SerializerOptions,
                    cancellationToken: ct);
                if (manifest is null)
                    return BundleImportResult.InvalidFormat("metadata.json could not be parsed");
                bundleId = manifest.BundleId ?? "";
                if (string.IsNullOrWhiteSpace(bundleId))
                    return BundleImportResult.InvalidFormat("metadata.json missing bundleId");
            }
            catch (Exception ex)
            {
                return BundleImportResult.InvalidFormat($"metadata.json parse error: {ex.Message}");
            }

            // Duplicate check
            var finalDir = Path.Combine(_bundlesRoot, bundleId);
            if (Directory.Exists(finalDir))
                return BundleImportResult.AlreadyExistsResult(bundleId);

            // Extract to temp directory
            var tempDir = Path.Combine(_bundlesRoot, $".import-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);
            try
            {
                foreach (var entry in archive.Entries)
                {
                    if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
                        continue; // directory entry

                    var destPath = Path.Combine(tempDir, entry.FullName.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                    await using var entryStream = entry.Open();
                    await using var fs = File.Create(destPath);
                    await entryStream.CopyToAsync(fs, ct);
                }

                // Validate via BundleValidator
                var validationResult = await BundleValidator.ValidateAsync(tempDir, manifest, strict: false, ct);
                if (!validationResult.IsValid)
                {
                    return BundleImportResult.InvalidFormat(
                        $"Bundle validation failed: {string.Join("; ", validationResult.Errors.Select(e => e.Message))}");
                }

                // Atomic rename to final location
                Directory.Move(tempDir, finalDir);
                return BundleImportResult.Succeeded(bundleId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Bundle import failed; cleaning up temp dir {TempDir}", tempDir);
                try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
                return BundleImportResult.InvalidFormat($"Import failed: {ex.Message}");
            }
        }
    }
}

public sealed record BundleImportResult
{
    public bool Success { get; init; }
    public string? BundleId { get; init; }
    public bool AlreadyExists { get; init; }
    public bool IsInvalidFormat { get; init; }
    public string? ErrorMessage { get; init; }

    public static BundleImportResult Succeeded(string bundleId) =>
        new() { Success = true, BundleId = bundleId };

    public static BundleImportResult AlreadyExistsResult(string bundleId) =>
        new() { AlreadyExists = true, BundleId = bundleId };

    public static BundleImportResult InvalidFormat(string message) =>
        new() { IsInvalidFormat = true, ErrorMessage = message };
}
