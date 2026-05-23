using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Tracer.Agent;
using Tracer.Agent.Configuration;
using Tracer.Agent.Logging;

var host = AgentHostBuilder.Build(args);

// LOG_FILE must be the first stdout line (convention A6.3.1)
var agentConfig = host.Services.GetRequiredService<AgentConfig>();
var logFilePath = LoggingPaths.GetCurrentLogFilePath(agentConfig.LogsRoot);
Console.WriteLine($"LOG_FILE={logFilePath}");

await host.RunAsync();
