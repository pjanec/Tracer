using Tracer.Bundle.Format;
using Tracer.Bundle.Packaging;
using Tracer.Bundle.Validation;
using Tracer.WebApi.Lifecycle;
using Microsoft.Extensions.Logging;

namespace Tracer.OfflineViewer.Lifecycle;

public sealed class BundleOpenManager : IAsyncDisposable
{
    private readonly ReadOnlyConnectionPool _pool;
    private readonly ILogger<BundleOpenManager> _logger;
    private readonly SemaphoreSlim _switchLock = new(1, 1);

    private OpenedBundle? _current;

    public BundleOpenManager(ReadOnlyConnectionPool pool, ILogger<BundleOpenManager> logger)
    {
        _pool = pool;
        _logger = logger;
    }

    public OpenedBundle? Current => _current;
    public bool IsOpen => _current is not null;

    public async Task OpenAsync(string bundlePath, CancellationToken ct)
    {
        await _switchLock.WaitAsync(ct);
        try
        {
            // 1. Resolve path to a directory (extract if zipped)
            var workingDirectory = await ResolveBundleDirectoryAsync(bundlePath, ct);

            // 2. Read and validate manifest
            BundleManifest manifest;
            try
            {
                manifest = await BundleReader.ReadManifestAsync(workingDirectory, ct);
            }
            catch (Exception ex)
            {
                // Clean up extracted temp dir if we created one
                if (workingDirectory != bundlePath && Directory.Exists(workingDirectory))
                {
                    try { Directory.Delete(workingDirectory, recursive: true); } catch { }
                }
                throw new InvalidOperationException(
                    $"Failed to read bundle manifest: {ex.Message}", ex);
            }

            var validation = await BundleValidator.ValidateAsync(
                workingDirectory, manifest, strict: false, ct);
            if (!validation.IsValid)
            {
                if (workingDirectory != bundlePath && Directory.Exists(workingDirectory))
                {
                    try { Directory.Delete(workingDirectory, recursive: true); } catch { }
                }
                throw new InvalidOperationException(
                    $"Bundle validation failed: {string.Join("; ", validation.Errors.Select(e => e.Message))}");
            }

            // 3. Open events.duckdb via the connection pool
            var eventsDb = Path.Combine(workingDirectory, "events.duckdb");
            if (_current is not null)
            {
                await _pool.OnIntervalRotatedAsync(eventsDb, ct);
                await CleanUpPreviousAsync(_current);
            }
            else
            {
                await _pool.InitializeAsync(eventsDb, ct);
            }

            _current = new OpenedBundle
            {
                Manifest = manifest,
                WorkingDirectory = workingDirectory,
                OriginalPath = bundlePath
            };
            _logger.LogInformation("Opened bundle {BundleId} from {Path}",
                manifest.BundleId, bundlePath);
        }
        finally { _switchLock.Release(); }
    }

    public async Task CloseAsync(CancellationToken ct)
    {
        await _switchLock.WaitAsync(ct);
        try
        {
            if (_current is null) return;
            await CleanUpPreviousAsync(_current);
            _current = null;
        }
        finally { _switchLock.Release(); }
    }

    private static async Task<string> ResolveBundleDirectoryAsync(string bundlePath, CancellationToken ct)
    {
        if (Directory.Exists(bundlePath)) return bundlePath;
        if (!File.Exists(bundlePath))
            throw new FileNotFoundException($"Bundle not found: {bundlePath}");

        // Treat as zip; extract to temp
        var tempDir = Path.Combine(Path.GetTempPath(), $"tracer-bundle-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        await BundleExtractor.ExtractAsync(bundlePath, tempDir, ct);
        return tempDir;
    }

    private Task CleanUpPreviousAsync(OpenedBundle bundle)
    {
        // Only delete the working directory if it was an extracted temp (not the original directory)
        if (bundle.WorkingDirectory != bundle.OriginalPath)
        {
            try { Directory.Delete(bundle.WorkingDirectory, recursive: true); }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to clean up temp directory"); }
        }
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _switchLock.Dispose();
        return ValueTask.CompletedTask;
    }
}

public sealed record OpenedBundle
{
    public required BundleManifest Manifest { get; init; }
    public required string WorkingDirectory { get; init; }
    public required string OriginalPath { get; init; }
}
