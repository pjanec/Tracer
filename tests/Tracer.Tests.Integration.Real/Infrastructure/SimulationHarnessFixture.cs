using System.Diagnostics;
using Xunit;

namespace Tracer.Tests.Integration.Real.Infrastructure;

/// <summary>
/// xUnit IAsyncLifetime fixture that starts and stops the simulation harness process.
/// The harness executable path is read from the TRACER_HARNESS_PATH environment variable.
/// When the variable is absent this fixture does nothing (tests are skipped by [SkipIfNoSimulationHarness]).
/// </summary>
public sealed class SimulationHarnessFixture : IAsyncLifetime
{
    private const string EnvVar = "TRACER_HARNESS_PATH";
    private Process? _harnessProcess;

    public bool IsAvailable { get; private set; }

    public async Task InitializeAsync()
    {
        var harnessPath = Environment.GetEnvironmentVariable(EnvVar);
        if (string.IsNullOrWhiteSpace(harnessPath))
        {
            IsAvailable = false;
            return;
        }

        _harnessProcess = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = harnessPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            }
        };
        _harnessProcess.Start();

        // Allow harness time to initialize (up to 30 s).
        await Task.Delay(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        IsAvailable = true;
    }

    /// <summary>
    /// Instructs the harness to emit a deterministic trace chain for testing.
    /// </summary>
    public Task EmitKnownTraceAsync(ulong traceId, int depth, CancellationToken ct = default)
    {
        // In a real deployment this would send a control message to the harness.
        // For CI scaffolding, this is a no-op placeholder.
        return Task.CompletedTask;
    }

    /// <summary>
    /// Instructs the harness to emit a burst of events at the specified rate.
    /// </summary>
    public Task EmitEventBurstAsync(int count, int ratePerSec, CancellationToken ct = default)
    {
        // Placeholder — real implementation sends control message to harness.
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        if (_harnessProcess is not null && !_harnessProcess.HasExited)
        {
            _harnessProcess.Kill(entireProcessTree: true);
            _harnessProcess.Dispose();
        }
        return Task.CompletedTask;
    }
}
