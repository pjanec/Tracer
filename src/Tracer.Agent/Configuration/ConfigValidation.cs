namespace Tracer.Agent.Configuration;

public static class ConfigValidation
{
    public static void Validate(AgentConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (string.IsNullOrWhiteSpace(config.NodeId))
            throw new InvalidOperationException("AgentConfig.NodeId must not be null or whitespace.");

        if (!Path.IsPathFullyQualified(config.DataRoot))
            throw new InvalidOperationException("AgentConfig.DataRoot must be a fully-qualified path.");

        if (!Path.IsPathFullyQualified(config.LogsRoot))
            throw new InvalidOperationException("AgentConfig.LogsRoot must be a fully-qualified path.");

        if (config.IntervalDuration < TimeSpan.FromMinutes(1))
            throw new InvalidOperationException("AgentConfig.IntervalDuration must be at least 1 minute.");

        if (config.IntervalDuration > TimeSpan.FromHours(24))
            throw new InvalidOperationException("AgentConfig.IntervalDuration must not exceed 24 hours.");

        if (TimeSpan.FromDays(1).Ticks % config.IntervalDuration.Ticks != 0)
            throw new InvalidOperationException(
                "AgentConfig.IntervalDuration must evenly divide 24 hours.");
    }
}
