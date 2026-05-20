using Microsoft.AspNetCore.Mvc;
using Tracer.Core.Errors;

namespace Tracer.WebApi.Errors;

public static class ProblemDetailsFactory
{
    public static ProblemDetails From(Exception? ex) => ex switch
    {
        ArgumentException ae => new ProblemDetails { Status = 400, Detail = ae.Message },
        TracerStorageException tse => new ProblemDetails { Status = 500, Detail = tse.Message },
        _ => new ProblemDetails { Status = 500, Detail = "An unexpected error occurred" }
    };
}
