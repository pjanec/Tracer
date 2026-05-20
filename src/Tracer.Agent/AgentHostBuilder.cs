using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Serilog;
using Tracer.Agent.Configuration;
using Tracer.Agent.Diagnostics;
using Tracer.Agent.Ingestion;
using Tracer.Agent.Lifecycle;
using Tracer.Agent.Logging;
using Tracer.Agent.Storage;
using Tracer.Agent.Time;
using Tracer.Agent.Transport;
using Tracer.Agent.Upload;
using Tracer.Core.Abstractions;
using Tracer.Core.Time;

namespace Tracer.Agent;

public static class AgentHostBuilder
{
    public static IHost Build(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        // ── Configuration ────────────────────────────────────────────────────
        builder.Services.Configure<AgentConfig>(builder.Configuration.GetSection("Agent"));
        builder.Services.AddSingleton(sp =>
        {
            var config = sp.GetRequiredService<IOptions<AgentConfig>>().Value;
            ConfigValidation.Validate(config);
            return config;
        });

        // ── Core services ────────────────────────────────────────────────────
        builder.Services.AddSingleton<IClock, SystemClock>();

        // ── Transport & upload ───────────────────────────────────────────────
        builder.Services.AddSingleton<IAgentTransport>(sp =>
            TransportFactory.Create(sp.GetRequiredService<AgentConfig>()));

        builder.Services.AddSingleton<ITelemetryUploadService>(sp =>
            UploadServiceFactory.Create(sp.GetRequiredService<AgentConfig>()));

        // ── Ingestion pipeline ───────────────────────────────────────────────
        builder.Services.AddSingleton<BackpressureMonitor>();
        builder.Services.AddSingleton<DropPolicy>();
        builder.Services.AddSingleton<RecordRouter>();
        builder.Services.AddSingleton<IngestionPipeline>();

        // ── Lifecycle ────────────────────────────────────────────────────────
        builder.Services.AddSingleton<IntervalScheduler>();
        builder.Services.AddSingleton<UploadIntentDispatcher>();
        builder.Services.AddSingleton<IntervalRotator>();
        builder.Services.AddSingleton<IIntervalContext>(sp =>
            sp.GetRequiredService<IntervalRotator>());
        builder.Services.AddSingleton<StartupRecoveryService>();

        // ── Storage ──────────────────────────────────────────────────────────
        builder.Services.AddSingleton<RetentionManager>();

        // ── Diagnostics ──────────────────────────────────────────────────────
        builder.Services.AddSingleton<AgentStateReporter>();

        // ── Hosted service ───────────────────────────────────────────────────
        builder.Services.AddHostedService<AgentHostedService>();

        // ── Serilog ──────────────────────────────────────────────────────────
        builder.Services.AddSerilog((sp, lc) =>
        {
            var config = sp.GetService<AgentConfig>();

            if (config is not null)
            {
                lc.WriteTo.File(
                    new Serilog.Formatting.Compact.CompactJsonFormatter(),
                    LoggingPaths.GetCurrentLogFilePath(config.LogsRoot),
                    rollingInterval: Serilog.RollingInterval.Day,
                    retainedFileCountLimit: 30);

                if (config.LogToConsole)
                    lc.WriteTo.Console();
            }
        });

        return builder.Build();
    }
}
