using Tracer.Adapters.Mock.Scenarios.Scripts;

namespace Tracer.Adapters.Mock.Scenarios;

/// <summary>
/// Central catalogue of available scenario scripts.
/// Each registered name maps to a factory that produces a fresh (stateless) script instance.
/// </summary>
public static class ScenarioRegistry
{
    private static readonly Dictionary<string, Func<IScenarioScript>> s_scenarios = new()
    {
        ["Calm"] = () => new CalmScenario(),
        ["CombatEngagement"] = () => new CombatEngagementScenario(),
    };

    /// <summary>
    /// Returns a fresh <see cref="IScenarioScript"/> for <paramref name="name"/>.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when <paramref name="name"/> is not registered.</exception>
    public static IScenarioScript Get(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (!s_scenarios.TryGetValue(name, out var factory))
            throw new ArgumentException($"Unknown scenario: '{name}'", nameof(name));
        return factory();
    }

    /// <summary>All registered scenario names.</summary>
    public static IReadOnlyCollection<string> AvailableScenarios => s_scenarios.Keys;
}
