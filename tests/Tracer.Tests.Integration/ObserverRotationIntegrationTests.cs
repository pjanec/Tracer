using Xunit;

namespace Tracer.Tests.Integration;

public sealed class ObserverRotationIntegrationTests
{
    [Fact(Skip = "Deferred to TRC-P3-009")]
    public Task Observer_RotatesInterval_WritesManifest()
        => Task.CompletedTask;

    [Fact(Skip = "Deferred to TRC-P3-009")]
    public Task Observer_ConnectionPool_RefreshesOnRotation()
        => Task.CompletedTask;

    [Fact(Skip = "Deferred to TRC-P3-009")]
    public Task Observer_RetentionDeletesOldIntervals()
        => Task.CompletedTask;
}
