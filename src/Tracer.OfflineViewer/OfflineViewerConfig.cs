using Tracer.WebApi.Lifecycle;

namespace Tracer.OfflineViewer;

public sealed class OfflineViewerConfig
{
    public int HttpPort { get; set; }
    public string LogFilePath { get; set; } = "";
    public string? InitialBundlePath { get; set; }

    /// <summary>Configures lifecycle topic classification (spawn/ownership/destruction).</summary>
    public LifecycleClassificationConfig LifecycleClassification { get; set; } = new();
}
