using Microsoft.Extensions.Hosting;
using Tracer.Agent;

var host = AgentHostBuilder.Build(args);
await host.RunAsync();
