namespace Tracer.Adapters.SharedMemory.Configuration;

/// <summary>Configuration for the shared memory ring buffer transport.</summary>
public sealed class SharedMemoryConfig
{
    public string SharedMemoryName { get; init; } = "TracerRingBuffer";
    public string SemaphoreName { get; init; } = "TracerSyncSem";
    public long CapacityBytes { get; init; } = 64 * 1024 * 1024;   // 64 MB
}
