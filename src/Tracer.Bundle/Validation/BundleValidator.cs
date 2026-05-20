using System.Security.Cryptography;
using Tracer.Bundle.Format;
using Tracer.Bundle.Packaging;

namespace Tracer.Bundle.Validation;

/// <summary>
/// Validates a bundle directory against its manifest.
/// All errors are collected; validation does not short-circuit on the first failure.
/// </summary>
public static class BundleValidator
{
    /// <summary>
    /// Validates the bundle at <paramref name="bundleDirectory"/> against <paramref name="manifest"/>.
    /// </summary>
    /// <param name="bundleDirectory">Path to the bundle directory (already extracted if needed).</param>
    /// <param name="manifest">The manifest to validate against.</param>
    /// <param name="strict">If true, also verifies SHA-256 checksums of each file.</param>
    public static async Task<ValidationResult> ValidateAsync(
        string bundleDirectory,
        BundleManifest manifest,
        bool strict = false,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(bundleDirectory);
        ArgumentNullException.ThrowIfNull(manifest);

        var errors = new List<ValidationError>();

        // Step 1: Schema version
        if (!BundleSchemaV1.IsRecognized(manifest.SchemaVersion))
        {
            errors.Add(new ValidationError(
                "SCHEMA_VERSION",
                $"Schema version {manifest.SchemaVersion} is not recognized. Expected: {BundleSchemaV1.CurrentVersion}."));
        }

        // Steps 2–4: Per-file checks
        foreach (var entry in manifest.Files)
        {
            var filePath = Path.Combine(bundleDirectory, entry.Path);

            // Step 2: File exists
            if (!File.Exists(filePath))
            {
                errors.Add(new ValidationError(
                    "FILE_MISSING",
                    $"File listed in manifest is missing: {entry.Path}"));
                continue; // Can't check size/checksum for a missing file
            }

            // Step 3: File size
            var actualSize = new FileInfo(filePath).Length;
            if (actualSize != entry.SizeBytes)
            {
                errors.Add(new ValidationError(
                    "SIZE_MISMATCH",
                    $"File size mismatch for {entry.Path}: expected {entry.SizeBytes} bytes, found {actualSize} bytes."));
            }

            // Step 4: Checksum (strict mode only, and only if manifest has a hash)
            if (strict && !string.IsNullOrEmpty(entry.Sha256))
            {
                var actualHash = await BundleDirectoryWriter.ComputeSha256Async(filePath, ct);
                if (!string.Equals(actualHash, entry.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add(new ValidationError(
                        "CHECKSUM_MISMATCH",
                        $"SHA-256 mismatch for {entry.Path}: expected {entry.Sha256}, found {actualHash}."));
                }
            }
        }

        return new ValidationResult(errors);
    }
}
