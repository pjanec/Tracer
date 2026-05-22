using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Tracer.WebApi.Util;

/// <summary>Marker service registered only in bundle (OfflineViewer) mode.</summary>
public interface IBundleModeMarker { }

/// <summary>
/// Guards Phase 9 endpoints that require per-node receive times,
/// which are only available when a bundle has been opened.
/// </summary>
public static class BundleModeGate
{
    public static IResult? CheckBundleOrLive(IServiceProvider sp)
    {
        if (sp.GetService<IBundleModeMarker>() is null)
            return TypedResults.Problem(new ProblemDetails
            {
                Title = "Bundle mode required",
                Detail = "This analysis requires per-node receive times, which are only available in bundle mode. Build a bundle from your session, then open it in the offline viewer.",
                Status = StatusCodes.Status409Conflict
            });
        return null;
    }
}
