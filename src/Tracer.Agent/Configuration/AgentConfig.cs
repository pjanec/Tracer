using System.ComponentModel.DataAnnotations;

namespace Tracer.Agent.Configuration;

public sealed class AgentConfig
{
    [Required]
    public string NodeId { get; set; } = string.Empty;

    [Required]
    public string DataRoot { get; set; } = string.Empty;

    [Required]
    public string LogsRoot { get; set; } = string.Empty;

    public TimeSpan IntervalDuration { get; set; } = TimeSpan.FromHours(1);

    public int KeepLastNIntervals { get; set; } = 24;

    public int DiskWatermarkPercent { get; set; } = 10;

    public bool LogToConsole { get; set; } = false;

    public TransportConfig Transport { get; set; } = new();

    public UploadServiceConfig UploadService { get; set; } = new();

    public BackpressureConfig Backpressure { get; set; } = new();

    public int BacklogWarningThreshold { get; set; } = 3;

    public int ShutdownUploadFlushTimeoutSeconds { get; set; } = 60;
}

public sealed class TransportConfig
{
    public string Kind { get; set; } = "InProcessChannel";
    public int CapacityRecords { get; set; } = 100_000;
}

public sealed class UploadServiceConfig
{
    public string Kind { get; set; } = "LocalFileSystem";
    public string LocalFileSystemRoot { get; set; } = string.Empty;
}

public sealed class BackpressureConfig
{
    public int InflightThreshold { get; set; } = 50_000;
    public int FastStateThreshold { get; set; } = 70_000;
    public int SlowStateThreshold { get; set; } = 90_000;
    public int EventsThreshold { get; set; } = 98_000;
}
