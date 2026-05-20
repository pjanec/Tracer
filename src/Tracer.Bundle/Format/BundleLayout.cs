namespace Tracer.Bundle.Format;

public static class BundleLayout
{
    public static readonly string ManifestFile        = "manifest.json";
    public static readonly string ScenarioFile        = "scenario.json";
    public static readonly string TopologyFile        = "topology.json";
    public static readonly string SourceIntervalsFile = "source_intervals.json";
    public static readonly string EventsDb            = "events.duckdb";
    public static readonly string SlowStateDb         = "slow_state.duckdb";
    public static readonly string ChecksumsFile       = "checksums.txt";
    public static readonly string FastStateDirectory  = "fast_state";
    public static readonly string AnnotationsDirectory = "annotations";
    public static readonly string AnnotationsKeepFile  = "annotations/.keep";
}
