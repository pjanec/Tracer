using System.ComponentModel.DataAnnotations;

namespace Tracer.Observer.Configuration;

public sealed class ObserverConfig
{
    [Required]
    public required string DataRoot { get; set; }

    [Required]
    public required string LogsRoot { get; set; }

    [Range(1024, 65535)]
    public int HttpPort { get; set; } = 5300;

    public TimeSpan IntervalDuration { get; set; } = TimeSpan.FromHours(1);

    public int KeepLastNIntervals { get; set; } = 4;

    public int DiskWatermarkPercent { get; set; } = 10;

    public bool LogToConsole { get; set; } = false;

    [Required]
    public required DataSourcesConfig DataSources { get; set; }

    public LiveStreamingConfig LiveStreaming { get; set; } = new();

    /// <summary>Where built bundles are stored on the observer's disk.</summary>
    public string BundlesRoot { get; set; } = "";

    /// <summary>Where the mock-NAS data lives (read source).</summary>
    public string NasMockRoot { get; set; } = "";
}

public sealed class DataSourcesConfig
{
    public string Kind { get; set; } = "Mock";
    public MockSourcesConfig? Mock { get; set; }
}

public sealed class MockSourcesConfig
{
    public IList<MockSourceEntry> Sources { get; set; } = new List<MockSourceEntry>();
}

public sealed class MockSourceEntry
{
    public required string Name { get; set; }
    public required string ScenarioName { get; set; }
}

public sealed class LiveStreamingConfig
{
    public int MaxConcurrentSseClients { get; set; } = 50;
    public int PerClientBufferSize { get; set; } = 1000;
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(15);
}

public static class ConfigValidation
{
    public static void Validate(ObserverConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (string.IsNullOrWhiteSpace(config.DataRoot))
            throw new InvalidOperationException("ObserverConfig.DataRoot must not be null or whitespace.");
        if (!Path.IsPathFullyQualified(config.DataRoot))
            throw new InvalidOperationException($"ObserverConfig.DataRoot must be an absolute path: '{config.DataRoot}'.");

        if (string.IsNullOrWhiteSpace(config.LogsRoot))
            throw new InvalidOperationException("ObserverConfig.LogsRoot must not be null or whitespace.");
        if (!Path.IsPathFullyQualified(config.LogsRoot))
            throw new InvalidOperationException($"ObserverConfig.LogsRoot must be an absolute path: '{config.LogsRoot}'.");

        if (config.HttpPort is < 1024 or > 65535)
            throw new InvalidOperationException($"ObserverConfig.HttpPort must be in range [1024, 65535], was: {config.HttpPort}.");
    }
}
