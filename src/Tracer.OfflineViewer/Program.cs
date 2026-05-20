using Microsoft.Extensions.DependencyInjection;
using Tracer.OfflineViewer.Browser;

namespace Tracer.OfflineViewer;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        try
        {
            var bundlePath = args.Length > 0 ? args[0] : null;

            var host = OfflineViewerHostBuilder.Build(bundlePath);

            var config = host.Services.GetRequiredService<OfflineViewerConfig>();
            Console.WriteLine($"LOG_FILE={config.LogFilePath}");

            await host.StartAsync();
            BrowserLauncher.Open($"http://localhost:{config.HttpPort}/");

            await host.WaitForShutdownAsync();
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"FATAL: {ex}");
            return 1;
        }
    }
}
