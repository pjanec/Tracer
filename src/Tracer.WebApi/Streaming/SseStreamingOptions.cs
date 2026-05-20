namespace Tracer.WebApi.Streaming;

public sealed class SseStreamingOptions
{
    public int MaxConcurrentSseClients { get; init; } = 50;
    public int PerClientBufferSize { get; init; } = 1000;
    public TimeSpan HeartbeatInterval { get; init; } = TimeSpan.FromSeconds(15);
}
