using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Formatting.Compact;
using Tracer.Adapters.Mock;
using Tracer.Adapters.Mock.Transport;
using Tracer.Adapters.Mock.Upload;
using Tracer.Agent.Configuration;
using Tracer.Agent.Diagnostics;
using Tracer.Agent.Ingestion;
using Tracer.Agent.Lifecycle;
using Tracer.Agent.Logging;
using Tracer.Agent.Storage;
using Tracer.Agent.Time;
using Tracer.Agent.Upload;
using Tracer.Core.Abstractions;
using Tracer.Core.Time;
using Tracer.FakeNode.Configuration;
using Tracer.Storage.DuckDB.Parquet;

namespace Tracer.FakeNode;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            var config = FakeNodeConfigLoader.Load(args);

            // LOG_FILE must be the first stdout line (convention)
            var logFilePath = LoggingPaths.GetCurrentLogFilePath(config.AgentConfig.LogsRoot);
            Console.WriteLine($"LOG_FILE={logFilePath}");

            var builder = Host.CreateApplicationBuilder(args);

            // ── FakeNode-specific services ────────────────────────────────────
            builder.Services.AddSingleton(config);
            builder.Services.AddSingleton(config.AgentConfig);

            // Mock data source
            builder.Services.AddSingleton(sp =>
                new MockDataSource(config.ScenarioName, config.ScenarioConfig));

            // Shared transport (both orchestrator writes and agent reads)
            builder.Services.AddSingleton<InProcessChannelTransport>(sp =>
                new InProcessChannelTransport(config.AgentConfig.Transport.CapacityRecords));
            builder.Services.AddSingleton<IAgentTransport>(sp =>
                sp.GetRequiredService<InProcessChannelTransport>());

            // Mock upload service
            builder.Services.AddSingleton<ITelemetryUploadService>(sp =>
                new LocalFileSystemUploadService(
                    !string.IsNullOrEmpty(config.AgentConfig.UploadService.LocalFileSystemRoot)
                        ? config.AgentConfig.UploadService.LocalFileSystemRoot
                        : Path.Combine(config.AgentConfig.DataRoot, "_upload_staging"),
                    sp.GetRequiredService<ILogger<LocalFileSystemUploadService>>()));

            // ── Core agent services (mirror of AgentHostBuilder) ──────────────
            builder.Services.AddSingleton<IClock, SystemClock>();

            builder.Services.AddSingleton<IReadOnlyDictionary<string, ParquetTopicSchema>>(
                _ => WellKnownTopicSchemas.ToDictionary());

            builder.Services.AddSingleton<BackpressureMonitor>();
            builder.Services.AddSingleton<DropPolicy>();
            builder.Services.AddSingleton<RecordRouter>();
            builder.Services.AddSingleton<IngestionPipeline>();

            builder.Services.AddSingleton<IntervalScheduler>();
            builder.Services.AddSingleton<UploadIntentDispatcher>();
            builder.Services.AddSingleton<IntervalRotator>();
            builder.Services.AddSingleton<IIntervalContext>(sp =>
                sp.GetRequiredService<IntervalRotator>());
            builder.Services.AddSingleton<StartupRecoveryService>();
            builder.Services.AddSingleton<RetentionManager>();
            builder.Services.AddSingleton<AgentStateReporter>();

            // Agent's main loop
            builder.Services.AddHostedService<AgentHostedService>();

            // FakeNode's scenario driver
            builder.Services.AddHostedService<FakeNodeOrchestrator>();

            // ── Serilog ───────────────────────────────────────────────────────
            builder.Services.AddSerilog((sp, lc) =>
            {
                lc.MinimumLevel.Information()
                  .Enrich.FromLogContext()
                  .Enrich.WithProperty("Service", "TracerFakeNode")
                  .Enrich.WithProperty("NodeId", config.AgentConfig.NodeId)
                  .WriteTo.File(
                      new CompactJsonFormatter(),
                      logFilePath,
                      rollingInterval: Serilog.RollingInterval.Day,
                      retainedFileCountLimit: 14);

                if (config.AgentConfig.LogToConsole)
                    lc.WriteTo.Console(new CompactJsonFormatter());
            });

            var host = builder.Build();
            await host.RunAsync();
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"FATAL: {ex}");
            return 1;
        }
    }
}
