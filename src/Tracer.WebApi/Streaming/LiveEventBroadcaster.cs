using Tracer.Core.Records;

namespace Tracer.WebApi.Streaming;

/// <summary>
/// Broadcasts live events to SSE subscribers.
/// Virtual methods allow test subclasses to record published events.
/// </summary>
public class LiveEventBroadcaster : BackgroundService
{
    public virtual void Publish(EventRecord ev) { }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
        => Task.CompletedTask;
}
