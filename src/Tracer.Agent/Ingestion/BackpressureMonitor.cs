using Tracer.Core.Abstractions;
using Tracer.Agent.Configuration;

namespace Tracer.Agent.Ingestion;

public enum BackpressureLevel
{
    Healthy,
    FastStateAtRisk,
    SlowStateAtRisk,
    EventsAtRisk,
    Saturated
}

public sealed class BackpressureMonitor
{
    private readonly IAgentTransport _transport;
    private readonly BackpressureConfig _config;

    public BackpressureMonitor(IAgentTransport transport, AgentConfig config)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(config);
        _transport = transport;
        _config = config.Backpressure;
    }

    public BackpressureLevel Evaluate()
    {
        var pending = _transport.GetHealth().PendingCount;

        if (pending >= _config.EventsThreshold) return BackpressureLevel.Saturated;
        if (pending >= _config.SlowStateThreshold) return BackpressureLevel.EventsAtRisk;
        if (pending >= _config.FastStateThreshold) return BackpressureLevel.SlowStateAtRisk;
        if (pending >= _config.InflightThreshold) return BackpressureLevel.FastStateAtRisk;
        return BackpressureLevel.Healthy;
    }
}
