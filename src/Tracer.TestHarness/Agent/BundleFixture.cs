using Tracer.Bundle.Format;
using Tracer.Bundle.Packaging;

namespace Tracer.TestHarness;

/// <summary>
/// Wraps <see cref="AggregationFixture"/> to produce a fully validated bundle directory.
/// Exposes <see cref="BundlePath"/> (the output directory) and <see cref="Manifest"/>.
/// </summary>
public sealed class BundleFixture : IAsyncDisposable
{
    private AggregationFixture? _inner;
    private bool _disposed;

    private BundleFixture() { }

    /// <summary>Absolute path to the bundle directory produced by this fixture.</summary>
    public string BundlePath { get; private set; } = null!;

    /// <summary>The manifest read from <see cref="BundlePath"/> after a successful build.</summary>
    public BundleManifest Manifest { get; private set; } = null!;

    /// <summary>
    /// Creates an <see cref="AggregationFixture"/>, runs a default build, and
    /// reads the resulting manifest into <see cref="Manifest"/>.
    /// </summary>
    public static async Task<BundleFixture> InitializeAsync(CancellationToken ct = default)
    {
        var inner = await AggregationFixture.InitializeAsync(ct);
        var outputPath = Path.Combine(Path.GetTempPath(), $"bundle-fix-{Guid.NewGuid():N}");

        var bf = new BundleFixture { _inner = inner };

        await inner.RunDefaultBuildAsync(outputPath, ct);
        bf.BundlePath = outputPath;
        bf.Manifest = await BundleReader.ReadManifestAsync(outputPath, ct);

        return bf;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        // Delete bundle directory first
        try
        {
            if (Directory.Exists(BundlePath))
                Directory.Delete(BundlePath, recursive: true);
        }
        catch { }

        if (_inner is not null)
            await _inner.DisposeAsync();
    }
}
