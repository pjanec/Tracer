using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tracer.Storage.DuckDB.MultiInterval;

namespace Tracer.OfflineViewer.Lifecycle;

public sealed class OfflineHostedService : IHostedService
{
    private readonly BundleOpenManager _bundleManager;
    private readonly LiveMultiIntervalReader _reader;
    private readonly OfflineViewerConfig _config;
    private readonly ILogger<OfflineHostedService> _logger;

    public OfflineHostedService(
        BundleOpenManager bundleManager,
        LiveMultiIntervalReader reader,
        OfflineViewerConfig config,
        ILogger<OfflineHostedService> logger)
    {
        _bundleManager = bundleManager;
        _reader = reader;
        _config = config;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        // Initialize the reader (subscribes to SetChanged, builds initial pool)
        await _reader.InitializeAsync(ct);

        if (_config.InitialBundlePath is { } path)
        {
            try
            {
                await _bundleManager.OpenAsync(path, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to open initial bundle at {Path}", path);
                // Don't fail startup — the viewer shows the Open Bundle view instead
            }
        }
    }

    public async Task StopAsync(CancellationToken ct)
    {
        await _bundleManager.CloseAsync(ct);
        await _reader.DisposeAsync();
    }
}
