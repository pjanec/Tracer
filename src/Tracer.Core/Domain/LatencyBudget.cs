namespace Tracer.Core.Domain;

public sealed record LatencyBudget
{
    public required string Topic { get; init; }
    public double? P99BudgetMs { get; init; }
    public double? AbsoluteMaxMs { get; init; }
}
