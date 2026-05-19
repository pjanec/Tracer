using Tracer.Adapters.Mock.Generation;
using Tracer.Adapters.Mock.Scenarios;
using Tracer.Core.Abstractions;
using Tracer.Core.Records;

namespace Tracer.Adapters.Mock;

/// <summary>
/// A deterministic mock data source driven by a named scenario script.
/// Two instances constructed with the same <c>(scenarioName, config)</c> produce
/// identical record sequences when iterated.
/// </summary>
public sealed class MockDataSource : IDiagnosticDataSource
{
    private readonly IScenarioScript _script;
    private readonly ScenarioContext _context;

    /// <summary>
    /// Constructs a data source that runs <paramref name="scenarioName"/> with
    /// <paramref name="config"/>. The <see cref="Random"/> and
    /// <see cref="TraceIdGenerator"/> share the same seed so all random draws
    /// derive from a single deterministic sequence.
    /// </summary>
    public MockDataSource(string scenarioName, ScenarioConfig config)
    {
        ArgumentNullException.ThrowIfNull(scenarioName);
        ArgumentNullException.ThrowIfNull(config);

        _script = ScenarioRegistry.Get(scenarioName);
        var clock = new SimulatedClock(config.StartTime);
        var random = new Random(config.Seed);
        var traceGen = new TraceIdGenerator(random);   // shares same Random
        _context = new ScenarioContext
        {
            Clock = clock,
            Random = random,
            Config = config,
            TraceIdGen = traceGen,
        };
    }

    /// <summary>The internal simulated clock, exposed for test-side inspection.</summary>
    public SimulatedClock Clock => _context.Clock;

    /// <inheritdoc/>
    public IAsyncEnumerable<DiagnosticRecord> ReadAsync(CancellationToken ct = default)
        => _script.ExecuteAsync(_context, ct);

}
