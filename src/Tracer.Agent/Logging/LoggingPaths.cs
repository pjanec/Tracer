namespace Tracer.Agent.Logging;

public static class LoggingPaths
{
    public static string GetCurrentLogFilePath(string logsRoot)
    {
        var date = DateTime.UtcNow.ToString("yyyyMMdd");
        return Path.Combine(logsRoot, $"tracer-agent-{date}.json");
    }
}
