namespace Tracer.TestHarness;

/// <summary>
/// Placeholder — full implementation deferred to integration test phase.
/// </summary>
public sealed class TracerStackFixture : IAsyncDisposable
{
    public async ValueTask DisposeAsync()
    {
        await Task.CompletedTask.ConfigureAwait(false);
    }
}
