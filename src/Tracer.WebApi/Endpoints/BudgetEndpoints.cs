using Microsoft.AspNetCore.Mvc;
using Tracer.WebApi.Contracts.Dto;
using Tracer.WebApi.Queries;

namespace Tracer.WebApi.Endpoints;

public static class BudgetEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/api/scenario/budgets", HandleAsync)
            .WithName("GetLatencyBudgets")
            .WithOpenApi();
    }

    public static async Task<IResult> HandleAsync(
        [FromServices] BudgetService svc,
        [FromQuery] string sessionId = "",
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(svc);
        var budgets = await svc.GetBudgetsAsync(sessionId, ct);
        return Results.Ok(new BudgetListDto
        {
            Budgets = budgets.Select(BudgetDtoMapper.Map).ToList(),
        });
    }
}
