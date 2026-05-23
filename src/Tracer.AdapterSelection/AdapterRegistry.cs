using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tracer.Adapters.DDS;
using Tracer.Adapters.DDS.Configuration;
using Tracer.Adapters.Mock;
using Tracer.Adapters.Mock.Scenarios;
using Tracer.Adapters.Mock.Storage;
using Tracer.Adapters.Mock.Transport;
using Tracer.Adapters.Mock.Upload;
using Tracer.Adapters.Nas;
using Tracer.Adapters.Nas.Configuration;
using Tracer.Adapters.SharedMemory;
using Tracer.Adapters.SharedMemory.Configuration;
using Tracer.Adapters.Sync;
using Tracer.Adapters.Sync.Configuration;
using Tracer.Core.Abstractions;
using Tracer.Core.Time;

namespace Tracer.AdapterSelection;

/// <summary>
/// Reads adapter slot values from the "adapters" configuration section and registers
/// the chosen implementation types into an <see cref="IServiceCollection"/>.
/// </summary>
public sealed class AdapterRegistry
{
    private readonly IConfiguration _config;

    public AdapterRegistry(IConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(config);
        _config = config;
    }

    /// <summary>Registers all five adapter slots: dataSource, transport, upload, storageReader, clock.</summary>
    public void RegisterAdapters(IServiceCollection services)
    {
        RegisterDataSource(services);
        RegisterTransport(services);
        RegisterUpload(services);
        RegisterStorageReader(services);
        RegisterClock(services);
    }

    // ── dataSource ───────────────────────────────────────────────────────────

    private void RegisterDataSource(IServiceCollection services)
    {
        var choice = _config["adapters:dataSource"] ?? "mock";
        switch (choice)
        {
            case "mock":
                services.AddSingleton<IDiagnosticDataSource>(
                    _ => new MockDataSource("Calm", new ScenarioConfig()));
                break;

            case "dds":
                services.Configure<DdsAdapterConfig>(_config.GetSection("dds"));
                services.AddSingleton(sp =>
                    sp.GetRequiredService<IOptions<DdsAdapterConfig>>().Value);
                services.AddSingleton<DdsTraceContextExtractor>();
                services.AddSingleton<DdsTopicRegistry>(sp => BuildDdsTopicRegistry(sp));
                services.AddSingleton<IDdsSubscriberFactory, DdsSubscriberFactory>();
                services.AddSingleton<DdsSampleTranslator>();
                services.AddSingleton<IDiagnosticDataSource, DdsDiagnosticDataSource>();
                break;

            default:
                throw new InvalidOperationException(
                    $"Unknown dataSource adapter value: '{choice}'. Supported values: mock, dds");
        }
    }

    // ── transport ────────────────────────────────────────────────────────────

    private void RegisterTransport(IServiceCollection services)
    {
        var choice = _config["adapters:transport"] ?? "in-process";
        switch (choice)
        {
            case "in-process":
                services.AddSingleton<IAgentTransport>(
                    _ => new InProcessChannelTransport(50_000));
                break;

            case "shared-memory":
                if (!OperatingSystem.IsWindows())
                    throw new PlatformNotSupportedException("shared-memory transport is only supported on Windows.");
                RegisterSharedMemoryTransport(services);
                break;

            default:
                throw new InvalidOperationException(
                    $"Unknown transport adapter value: '{choice}'. Supported values: in-process, shared-memory");
        }
    }

    // ── upload ───────────────────────────────────────────────────────────────

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private void RegisterSharedMemoryTransport(IServiceCollection services)
    {
        services.Configure<SharedMemoryConfig>(_config.GetSection("sharedMemory"));
        services.AddSingleton(sp =>
            sp.GetRequiredService<IOptions<SharedMemoryConfig>>().Value);
        services.AddSingleton<IAgentTransport, SharedMemoryTransport>();
    }

    // ── upload ───────────────────────────────────────────────────────────────

    private void RegisterUpload(IServiceCollection services)
    {
        var choice = _config["adapters:upload"] ?? "local-file-system";
        switch (choice)
        {
            case "local-file-system":
                services.AddSingleton<ITelemetryUploadService>(
                    _ => new LocalFileSystemUploadService(Path.GetTempPath()));
                break;

            case "sync":
                services.Configure<SyncAdapterConfig>(_config.GetSection("sync"));
                services.AddSingleton(sp =>
                    sp.GetRequiredService<IOptions<SyncAdapterConfig>>().Value);
                services.AddHttpClient<SyncMasterRestClient>((sp, client) =>
                {
                    var cfg = sp.GetRequiredService<IOptions<SyncAdapterConfig>>().Value;
                    client.BaseAddress = new Uri(cfg.SyncMasterBaseUrl);
                    client.Timeout = TimeSpan.FromSeconds(cfg.RequestTimeoutSeconds);
                });
                services.AddSingleton<ITelemetryUploadService, SyncSystemUploadService>();
                break;

            default:
                throw new InvalidOperationException(
                    $"Unknown upload adapter value: '{choice}'. Supported values: local-file-system, sync");
        }
    }

    // ── storageReader ────────────────────────────────────────────────────────

    private void RegisterStorageReader(IServiceCollection services)
    {
        var choice = _config["adapters:storageReader"] ?? "local-file-system";
        switch (choice)
        {
            case "local-file-system":
                services.AddSingleton<ITelemetryStorageReader>(
                    _ => new LocalFileSystemStorageReader(Path.GetTempPath()));
                break;

            case "nas":
                services.Configure<NasAdapterConfig>(_config.GetSection("nas"));
                services.AddSingleton(sp =>
                    sp.GetRequiredService<IOptions<NasAdapterConfig>>().Value);
                services.AddSingleton<ITelemetryStorageReader, NasStorageReader>();
                break;

            default:
                throw new InvalidOperationException(
                    $"Unknown storageReader adapter value: '{choice}'. Supported values: local-file-system, nas");
        }
    }

    // ── clock ────────────────────────────────────────────────────────────────

    private void RegisterClock(IServiceCollection services)
    {
        var choice = _config["adapters:clock"] ?? "system";
        switch (choice)
        {
            case "system":
                services.AddSingleton<IClock, SystemClock>();
                break;

            case "simulated":
                services.AddSingleton<IClock>(_ => new SimulatedClock());
                break;

            default:
                throw new InvalidOperationException(
                    $"Unknown clock adapter value: '{choice}'. Supported values: system, simulated");
        }
    }

    // ── DDS helpers ──────────────────────────────────────────────────────────

    private static DdsTopicRegistry BuildDdsTopicRegistry(IServiceProvider sp)
    {
        var config = sp.GetRequiredService<DdsAdapterConfig>();
        var logger = sp.GetRequiredService<ILogger<AdapterRegistry>>();

        var metas = new List<DdsTopicMetadata>();
        foreach (var sub in config.Topics)
        {
            var type = Type.GetType(sub.SampleTypeName);
            if (type is null)
            {
                logger.LogWarning(
                    "DDS topic type '{TypeName}' could not be resolved; topic '{Topic}' will be skipped",
                    sub.SampleTypeName, sub.TopicName);
                continue;
            }

            metas.Add(new DdsTopicMetadata
            {
                TopicName = sub.TopicName,
                SampleType = type,
                Kind = DdsTopicKind.Event,
                EntityIdField = "entityId",
            });
        }

        return new DdsTopicRegistry(metas);
    }
}
