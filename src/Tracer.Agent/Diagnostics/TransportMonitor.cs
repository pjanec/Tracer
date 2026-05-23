using Microsoft.Extensions.Logging;
using Tracer.Core.Abstractions;

namespace Tracer.Agent.Diagnostics;

/// <summary>
/// Periodically polls the transport for dropped records and logs a warning when the count increases.
/// </summary>
public sealed class TransportMonitor
{
    private readonly IAgentTransport _transport;
    private readonly ILogger<TransportMonitor> _logger;
    private readonly TimeSpan _pollInterval;
    private long _lastDroppedCount;

    public TransportMonitor(
        IAgentTransport transport,
        ILogger<TransportMonitor> logger,
        TimeSpan? pollInterval = null)
    {
        _transport = transport;
        _logger = logger;
        _pollInterval = pollInterval ?? TimeSpan.FromSeconds(5);
    }

    public async Task MonitorAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_pollInterval, ct).ConfigureAwait(false);
                var health = _transport.GetHealth();
                var newDrops = health.TotalDropped - _lastDroppedCount;
                if (newDrops > 0)
                {
                    _logger.LogWarning(
                        "Transport dropped records since last check: NewDrops={NewDrops}, TotalDropped={TotalDropped}",
                        newDrops, health.TotalDropped);
                    _lastDroppedCount = health.TotalDropped;
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                // Monitor must never throw — log and continue.
                _logger.LogError(ex, "TransportMonitor encountered an unexpected error");
            }
        }
    }
}
