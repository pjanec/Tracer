using Tracer.Core.Records;

namespace Tracer.Adapters.Mock.Scenarios;

/// <summary>
/// Contract for scenario scripts that produce diagnostic records deterministically.
/// </summary>
public interface IScenarioScript
{
    /// <summary>The canonical name of this scenario (matches the registry key).</summary>
    string Name { get; }

    /// <summary>
    /// Executes the scenario and yields records. Two executions with equal
    /// <see cref="ScenarioContext"/> values (same seed) produce identical sequences.
    /// </summary>
    IAsyncEnumerable<DiagnosticRecord> ExecuteAsync(
        ScenarioContext context,
        CancellationToken ct);
}
