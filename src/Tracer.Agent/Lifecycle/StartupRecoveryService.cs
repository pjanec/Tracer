namespace Tracer.Agent.Lifecycle;

public sealed class StartupRecoveryService
{
    public Task RecoverAsync(CancellationToken ct) => Task.CompletedTask;
}
