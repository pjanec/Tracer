namespace Tracer.Adapters.Nas;

/// <summary>
/// A locally accessible interval zip file, possibly staged from NAS.
/// Disposing will remove the temp copy if one was made during staging.
/// </summary>
public sealed class StagedInterval : IDisposable
{
    private Action? _cleanup;

    public string LocalPath { get; init; }

    public StagedInterval(string localPath, Action? cleanup = null)
    {
        ArgumentNullException.ThrowIfNull(localPath);
        LocalPath = localPath;
        _cleanup = cleanup;
    }

    public void Dispose()
    {
        var cleanup = Interlocked.Exchange(ref _cleanup, null);
        cleanup?.Invoke();
    }
}
