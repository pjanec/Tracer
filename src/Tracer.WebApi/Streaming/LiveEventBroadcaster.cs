using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tracer.Core.Records;

namespace Tracer.WebApi.Streaming;

/// <summary>
/// Broadcasts live events to SSE subscribers.
/// Virtual methods allow test subclasses to record published events.
/// </summary>
public class LiveEventBroadcaster : BackgroundService
{
    private readonly Channel<EventRecord>? _inbox;
    private readonly SseConnectionManager? _connectionManager;
    private readonly ILogger<LiveEventBroadcaster>? _logger;

    /// <summary>No-arg constructor for test subclasses that override <see cref="Publish"/>.</summary>
    public LiveEventBroadcaster() { }

    public LiveEventBroadcaster(SseConnectionManager connectionManager, ILogger<LiveEventBroadcaster> logger)
    {
        _connectionManager = connectionManager;
        _logger = logger;
        _inbox = Channel.CreateUnbounded<EventRecord>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });
    }

    /// <summary>
    /// Publishes an event to be broadcast to all SSE connections.
    /// Virtual to allow test subclasses to intercept.
    /// </summary>
    public virtual void Publish(EventRecord ev)
    {
        _inbox?.Writer.TryWrite(ev);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_inbox is null || _connectionManager is null) return;

        await foreach (var ev in _inbox.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await _connectionManager.BroadcastAsync(ev, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger?.LogError(ex, "Error broadcasting event {EventId}", ev.EventId);
            }
        }
    }
}

