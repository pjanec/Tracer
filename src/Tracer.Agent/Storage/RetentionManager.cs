using Microsoft.Extensions.Logging;
using Tracer.Agent.Configuration;
using Tracer.Core.Domain;

namespace Tracer.Agent.Storage;

public sealed class RetentionManager
{
    private readonly AgentConfig _config;
    private readonly ILogger<RetentionManager> _logger;

    public RetentionManager(AgentConfig config, ILogger<RetentionManager> logger)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(logger);
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// Applies retention policy. The caller passes the currently-open interval timestamp
    /// to prevent it from being deleted even if it meets eviction criteria.
    /// </summary>
    public Task ApplyAsync(IntervalTimestamp? openIntervalTimestamp, CancellationToken ct)
    {
        var intervalsRoot = Path.Combine(_config.DataRoot, "intervals");
        if (!Directory.Exists(intervalsRoot))
            return Task.CompletedTask;

        var readyDirs = Directory.EnumerateDirectories(intervalsRoot)
            .Where(d =>
            {
                var name = Path.GetFileName(d);
                if (!IntervalTimestamp.TryParse(name, out _)) return false;
                return File.Exists(Path.Combine(d, "_ready"));
            })
            .OrderBy(d => Path.GetFileName(d))
            .ToList();

        var openValue = openIntervalTimestamp?.Value;

        var toDelete = new List<string>();
        var keep = _config.KeepLastNIntervals;
        if (readyDirs.Count > keep)
            toDelete.AddRange(readyDirs.Take(readyDirs.Count - keep));

        if (openValue is not null)
            toDelete.RemoveAll(d => Path.GetFileName(d) == openValue);

        foreach (var dir in toDelete)
        {
            ct.ThrowIfCancellationRequested();
            TryDeleteInterval(dir);
        }

        EnforceDiskWatermark(intervalsRoot, openValue, ct);
        return Task.CompletedTask;
    }

    private void TryDeleteInterval(string dirPath)
    {
        try
        {
            Directory.Delete(dirPath, recursive: true);
            _logger.LogInformation("Retention: deleted interval {Interval}", Path.GetFileName(dirPath));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Retention: failed to delete interval {Interval}", Path.GetFileName(dirPath));
        }
    }

    private void EnforceDiskWatermark(string intervalsRoot, string? openValue, CancellationToken ct)
    {
        try
        {
            var drive = new DriveInfo(intervalsRoot);
            var freePercent = (double)drive.AvailableFreeSpace / drive.TotalSize * 100.0;
            if (freePercent >= _config.DiskWatermarkPercent) return;

            _logger.LogWarning(
                "Disk watermark triggered: {Free:F1}% free (threshold {Threshold}%)",
                freePercent, _config.DiskWatermarkPercent);

            var candidates = Directory.EnumerateDirectories(intervalsRoot)
                .Where(d =>
                {
                    var name = Path.GetFileName(d);
                    if (!IntervalTimestamp.TryParse(name, out _)) return false;
                    if (name == openValue) return false;
                    return File.Exists(Path.Combine(d, "_ready"));
                })
                .OrderBy(d => Path.GetFileName(d))
                .ToList();

            while (candidates.Count > 1)
            {
                ct.ThrowIfCancellationRequested();
                var oldest = candidates[0];
                candidates.RemoveAt(0);
                TryDeleteInterval(oldest);

                drive = new DriveInfo(intervalsRoot);
                freePercent = (double)drive.AvailableFreeSpace / drive.TotalSize * 100.0;
                if (freePercent >= _config.DiskWatermarkPercent)
                {
                    _logger.LogInformation("Disk watermark cleared: {Free:F1}% free", freePercent);
                    return;
                }
            }

            _logger.LogWarning("Disk watermark: only 1 interval remains, cannot evict further");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Disk watermark check failed");
        }
    }
}
