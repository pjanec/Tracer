using Tracer.Core.Domain;

namespace Tracer.WebApi.Queries;

/// <summary>
/// In-memory registry for latency budgets, used in live mode or test overrides.
/// </summary>
public sealed class InMemoryBudgetRegistry
{
    private readonly List<LatencyBudget> _budgets = new();

    public void Register(LatencyBudget budget) => _budgets.Add(budget);

    public IReadOnlyList<LatencyBudget> GetAll() => _budgets.AsReadOnly();
}
