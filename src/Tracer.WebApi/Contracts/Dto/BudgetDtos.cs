namespace Tracer.WebApi.Contracts.Dto;

public sealed record BudgetDto
{
    public required string Topic { get; init; }
    public double? P99BudgetMs { get; init; }
    public double? AbsoluteMaxMs { get; init; }
}

public sealed record BudgetListDto
{
    public required IReadOnlyList<BudgetDto> Budgets { get; init; }
}
