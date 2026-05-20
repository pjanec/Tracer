namespace Tracer.WebApi.OpenApi;

public static class OpenApiConfiguration
{
    public static void Configure(WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddOpenApiDocument(c =>
        {
            c.Title = "Tracer Observer API";
            c.Version = "v1";
        });
    }
}
