using Tracer.Core.Abstractions;
using Tracer.Core.Domain;

namespace Tracer.Agent.Storage;

public sealed class IntervalDirectory
{
    public string DataRoot { get; }
    public IntervalTimestamp Timestamp { get; }

    public string RootPath { get; }
    public string EventsDbPath { get; }
    public string SlowStateDbPath { get; }
    public string FastStateDirectory { get; }
    public string ManifestPath { get; }
    public string ReadySentinelPath { get; }

    public bool IsReady => File.Exists(ReadySentinelPath);
    public bool HasManifest => File.Exists(ManifestPath);

    public IntervalDirectory(string dataRoot, IntervalTimestamp timestamp)
    {
        DataRoot = dataRoot;
        Timestamp = timestamp;
        RootPath = Path.Combine(dataRoot, "intervals", timestamp.Value);
        EventsDbPath = Path.Combine(RootPath, "events.duckdb");
        SlowStateDbPath = Path.Combine(RootPath, "slow_state.duckdb");
        FastStateDirectory = Path.Combine(RootPath, "fast_state");
        ManifestPath = Path.Combine(RootPath, "manifest.json");
        ReadySentinelPath = Path.Combine(RootPath, "_ready");
    }

    public void EnsureCreated()
    {
        Directory.CreateDirectory(RootPath);
        Directory.CreateDirectory(FastStateDirectory);
    }

    public void WriteReadySentinel()
        => File.WriteAllBytes(ReadySentinelPath, Array.Empty<byte>());

    public IReadOnlyList<FileToUpload> EnumerateFiles()
    {
        var files = new List<FileToUpload>();

        AddIfExists(files, EventsDbPath, "events");
        AddIfExists(files, SlowStateDbPath, "slow_state");

        if (Directory.Exists(FastStateDirectory))
        {
            foreach (var f in Directory.GetFiles(FastStateDirectory, "*.parquet"))
                files.Add(new FileToUpload
                {
                    Path = f,
                    SizeBytes = new FileInfo(f).Length,
                    Description = "fast_state",
                });
        }

        AddIfExists(files, ManifestPath, "manifest");

        return files;
    }

    private static void AddIfExists(List<FileToUpload> list, string path, string description)
    {
        if (!File.Exists(path)) return;
        list.Add(new FileToUpload
        {
            Path = path,
            SizeBytes = new FileInfo(path).Length,
            Description = description,
        });
    }
}
