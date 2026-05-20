namespace Tracer.Aggregator.Staging;

/// <summary>
/// Manages the temporary staging workspace used during a bundle build.
/// Contains two sub-directories:
/// <list type="bullet">
///   <item><see cref="BundleStagingPath"/> — where bundle output files are assembled</item>
///   <item><see cref="SourcesPath"/> — where source interval archives are extracted</item>
/// </list>
/// Deletes the entire workspace on <see cref="DisposeAsync"/>.
/// </summary>
public sealed class StagingDirectory : IAsyncDisposable
{
    private readonly string _stagingRoot;
    private bool _disposed;

    /// <summary>The directory where bundle output files are assembled.</summary>
    public string BundleStagingPath { get; }

    /// <summary>The directory under which extracted source interval archives are placed.</summary>
    public string SourcesPath { get; }

    private StagingDirectory(string root)
    {
        _stagingRoot = root;
        BundleStagingPath = Path.Combine(root, "bundle");
        SourcesPath = Path.Combine(root, "sources");
        Directory.CreateDirectory(BundleStagingPath);
        Directory.CreateDirectory(SourcesPath);
    }

    /// <summary>Creates a new staging workspace in the system temporary directory.</summary>
    public static Task<StagingDirectory> CreateAsync(
        string finalOutputPath,
        CancellationToken ct = default)
    {
        _ = finalOutputPath; // used by callers for context; not needed here
        var root = Path.Combine(Path.GetTempPath(), $"tracer-staging-{Guid.NewGuid():N}");
        return Task.FromResult(new StagingDirectory(root));
    }

    /// <summary>Deletes the entire staging workspace.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        if (Directory.Exists(_stagingRoot))
            await Task.Run(() => Directory.Delete(_stagingRoot, recursive: true));
    }
}
