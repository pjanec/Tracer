using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tracer.Agent.Configuration;
using Tracer.Agent.Ingestion;
using Tracer.Agent.Storage;
using Tracer.Core.Domain;

namespace Tracer.Agent.Lifecycle;

public sealed class AgentHostedService : BackgroundService
{
    private readonly StartupRecoveryService _recovery;
    private readonly IntervalRotator _rotator;
    private readonly IngestionPipeline _ingestion;
    private readonly RetentionManager _retention;
    private readonly IntervalScheduler _scheduler;
    private readonly ILogger<AgentHostedService> _logger;

    public AgentHostedService(
        StartupRecoveryService recovery,
        IntervalRotator rotator,
        IngestionPipeline ingestion,
        RetentionManager retention,
        IntervalScheduler scheduler,
        ILogger<AgentHostedService> logger)
    {
        _recovery = recovery;
        _rotator = rotator;
        _ingestion = ingestion;
        _retention = retention;
        _scheduler = scheduler;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("TracerAgent starting");

        await _recovery.RecoverAsync(stoppingToken);
        await _rotator.OpenCurrentAsync(stoppingToken);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);

        var ingestionTask = Task.Run(() => _ingestion.RunAsync(cts.Token), cts.Token);
        var retentionTask = RunRetentionLoopAsync(cts.Token);
        var rotationTask = RunRotationLoopAsync(cts.Token);

        try
        {
            await Task.WhenAll(ingestionTask, retentionTask, rotationTask);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error in agent task loop");
        }

        await _rotator.RotateAsync(ManifestFinalizationReason.GracefulShutdown, CancellationToken.None);
        _logger.LogInformation("TracerAgent stopped");
    }

    private async Task RunRotationLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var delay = _scheduler.TimeUntilNextBoundary();
            await Task.Delay(delay, ct);
            if (ct.IsCancellationRequested) break;
            await _rotator.RotateAsync(ManifestFinalizationReason.ScheduledRotation, ct);
        }
    }

    private async Task RunRetentionLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await _retention.ApplyAsync(_rotator.CurrentDirectory?.Timestamp, ct);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Retention apply failed");
            }

            await Task.Delay(TimeSpan.FromMinutes(5), ct);
        }
    }
}
