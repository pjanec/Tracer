using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Tracer.WebApi.Contracts.Dto;
using Tracer.WebApi.Lifecycle;

namespace Tracer.WebApi.Endpoints;

public static class ConfigEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/api/config/lifecycle-classification", HandleAsync).WithOpenApi();
    }

    public static Ok<LifecycleConfigDto> HandleAsync(
        [FromServices] LifecycleClassificationConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        return TypedResults.Ok(new LifecycleConfigDto
        {
            SpawnSuffixes = config.SpawnSuffixes,
            OwnershipSuffixes = config.OwnershipSuffixes,
            DestructionSuffixes = config.DestructionSuffixes,
            SpawnRegex = config.Regex?.Spawn,
            OwnershipRegex = config.Regex?.Ownership,
            DestructionRegex = config.Regex?.Destruction,
        });
    }
}
