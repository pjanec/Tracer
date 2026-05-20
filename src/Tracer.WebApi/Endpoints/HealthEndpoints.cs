namespace Tracer.WebApi.Endpoints;

public static class HealthEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }))
           .WithName("GetHealth")
           .WithOpenApi();
    }
}
