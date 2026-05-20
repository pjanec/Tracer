using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tracer.Agent.Lifecycle;
using Tracer.Agent.Storage;
using Tracer.Core.Domain;
using Tracer.Core.Time;
using Tracer.WebApi.Lifecycle;

namespace Tracer.Observer.Lifecycle;

/// <summary>
/// Abstraction over StartupRecoveryService to allow test doubles.
/// </summary>
public interface IStartupRecovery
{
    Task RecoverAsync(CancellationToken ct);
}

public sealed class ObserverHostedService : BackgroundService
{
    private readonly IStartupRecovery _recovery;
    private readonly IntervalRotator _rotator;
    private readonly IntervalScheduler _scheduler;
    private readonly ObserverIngestionPipeline _ingestion;
    private readonly ReadOnlyConnectionPool _pool;
    private readonly RetentionManager _retention;
    private readonly IClock _clock;
    private readonly ILogger<ObserverHostedService> _logger;

    public ObserverHostedService(
        IStartupRecovery recovery,
        IntervalRotator rotator,
        IntervalScheduler scheduler,
        ObserverIngestionPipeline ingestion,
        ReadOnlyConnectionPool pool,
        RetentionManager retention,
        IClock clock,
        ILogger<ObserverHostedService> logger)
    {
        _recovery = recovery;
        _rotator = rotator;
        _scheduler = scheduler;
        _ingestion = ingestion;
        _pool = pool;
        _retention = retention;
        _clock = clock;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("TracerObserver starting");

        // 1. Recovery — finalize any orphaned intervals from previous run
        await _recovery.RecoverAsync(stoppingToken);

        // 2. Open the current interval
        await _rotator.OpenCurrentAsync(stoppingToken);

        // 3. Initialize the read-only connection pool against the active interval
        var activeDb = _rotator.CurrentDirectory!.EventsDbPath;
        await _pool.InitializeAsync(activeDb, stoppingToken);

        // 4. Start ingestion and retention in background
        var ingestionTask = _ingestion.RunAsync(stoppingToken);
        var retentionTask = RetentionLoopAsync(stoppingToken);

        // 5. Rotation loop runs on this task
        await RotationLoopAsync(stoppingToken);

        // 6. Shutdown propagates to background tasks
        await Task.WhenAll(ingestionTask, retentionTask);

        // 7. Final rotation to close the current interval cleanly
        await _rotator.RotateAsync(ManifestFinalizationReason.GracefulShutdown, CancellationToken.None);

        _logger.LogInformation("TracerObserver stopped");
    }

    private async Task RotationLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var timeUntilBoundary = _scheduler.TimeUntilNextBoundary();
            if (timeUntilBoundary > TimeSpan.Zero)
            {
                try { await Task.Delay(timeUntilBoundary, ct); }
                catch (OperationCanceledException) { return; }
            }
            await _rotator.RotateAsync(ManifestFinalizationReason.ScheduledRotation, ct);

            var newActiveDb = _rotator.CurrentDirectory!.EventsDbPath;
            try
            {
                await _pool.OnIntervalRotatedAsync(newActiveDb, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Connection pool refresh failed after rotation");
            }
        }
    }

    private async Task RetentionLoopAsync(CancellationToken ct)
    {
        var interval = TimeSpan.FromMinutes(5);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await _retention.ApplyAsync(_rotator.CurrentDirectory?.Timestamp, ct);
                await Task.Delay(interval, ct);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Retention pass failed; continuing");
                try { await Task.Delay(interval, ct); } catch (OperationCanceledException) { return; }
            }
        }
    }
}
