using Microsoft.Extensions.Logging;
using Tracer.Agent.Configuration;

namespace Tracer.Agent.Storage;

public sealed class RetentionManager
{
    private readonly AgentConfig _config;
    private readonly ILogger<RetentionManager> _logger;

    public RetentionManager(AgentConfig config, ILogger<RetentionManager> logger)
    {
        _config = config;
        _logger = logger;
    }

    public Task ApplyAsync(CancellationToken ct) => Task.CompletedTask;
}
