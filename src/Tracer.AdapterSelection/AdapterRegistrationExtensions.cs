using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Tracer.AdapterSelection;

/// <summary>
/// Extension methods for registering adapter implementations via the "adapters" config section.
/// </summary>
public static class AdapterRegistrationExtensions
{
    /// <summary>
    /// Registers adapter implementations chosen by the "adapters" configuration section.
    /// Call this from the host builder after all services are configured.
    /// </summary>
    public static IServiceCollection AddTracerAdapters(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var registry = new AdapterRegistry(configuration);
        registry.RegisterAdapters(services);
        return services;
    }
}
