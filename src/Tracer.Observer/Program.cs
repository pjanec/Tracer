using Tracer.Observer;

try
{
    var app = ObserverHostBuilder.Build(args);
    var config = app.Services.GetRequiredService<Tracer.Observer.Configuration.ObserverConfig>();
    var logFilePath = Path.Combine(config.LogsRoot,
        $"tracer-observer-{DateTime.UtcNow:yyyy-MM-dd}.json");
    Console.WriteLine($"LOG_FILE={logFilePath}");
    await app.RunAsync();
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"FATAL: {ex}");
    return 1;
}
