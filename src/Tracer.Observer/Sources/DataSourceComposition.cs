using Tracer.Adapters.Mock;
using Tracer.Adapters.Mock.Scenarios;
using Tracer.Observer.Configuration;

namespace Tracer.Observer.Sources;

public static class DataSourceComposition
{
    public static IReadOnlyList<NamedDataSource> Build(ObserverConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        var ds = config.DataSources;
        if (!string.Equals(ds.Kind, "Mock", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Unsupported data source kind: '{ds.Kind}'.");

        var entries = ds.Mock?.Sources ?? new List<MockSourceEntry>();
        if (entries.Count == 0)
            throw new InvalidOperationException("At least one data source must be configured under DataSources.Mock.Sources.");

        return entries.Select(e => new NamedDataSource(
            e.Name,
            new MockDataSource(e.ScenarioName, new ScenarioConfig())
        )).ToList();
    }
}
