using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tracer.Adapters.Mock;
using Tracer.Adapters.Mock.Transport;
using Tracer.FakeNode.Configuration;

namespace Tracer.FakeNode;

/// <summary>
/// Drives the MockDataSource into the InProcessChannelTransport.
/// Completes the transport when the scenario ends.
/// </summary>
public sealed class FakeNodeOrchestrator : BackgroundService
{
    private readonly MockDataSource _dataSource;
    private readonly InProcessChannelTransport _transport;
    private readonly FakeNodeConfig _config;
    private readonly ILogger<FakeNodeOrchestrator> _logger;

    public FakeNodeOrchestrator(
        MockDataSource dataSource,
        InProcessChannelTransport transport,
        FakeNodeConfig config,
        ILogger<FakeNodeOrchestrator> logger)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(logger);
        _dataSource = dataSource;
        _transport = transport;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "FakeNode orchestrator starting scenario '{Scenario}'", _config.ScenarioName);

        try
        {
            await foreach (var record in _dataSource.ReadAsync(stoppingToken))
            {
                await _transport.WriteAsync(record, stoppingToken);
            }

            _logger.LogInformation(
                "Scenario '{Scenario}' completed; signalling transport completion",
                _config.ScenarioName);

            _transport.Complete();
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("FakeNode orchestrator stopping (cancelled)");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FakeNode orchestrator failed");
            throw;
        }
    }
}
