using Microsoft.AspNetCore.Diagnostics;

namespace Tracer.WebApi.Errors;

public static class ApiExceptionMiddleware
{
    public static async Task HandleAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var ex = context.Features.Get<IExceptionHandlerFeature>()?.Error;
        if (ex is null) return;

        var (status, detail) = ex switch
        {
            ArgumentException ae => (400, ae.Message),
            _ => (500, "An unexpected error occurred")
        };

        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(new
        {
            status,
            detail,
            title = status == 400 ? "Bad Request" : "Internal Server Error"
        });
    }
}
