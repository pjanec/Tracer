using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tracer.AdapterSelection;
using Tracer.Adapters.DDS;
using Tracer.Adapters.Mock;
using Tracer.Adapters.Mock.Storage;
using Tracer.Adapters.Mock.Transport;
using Tracer.Adapters.Mock.Upload;
using Tracer.Adapters.Nas;
using Tracer.Adapters.SharedMemory;
using Tracer.Adapters.Sync;
using Tracer.Core.Abstractions;
using Tracer.Core.Time;
using Xunit;

namespace Tracer.Tests.Unit.AdapterSelection;

public sealed class AdapterRegistryTests
{
    // ── Helper ───────────────────────────────────────────────────────────────

    private static IConfiguration BuildConfig(Dictionary<string, string?> values)
        => new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static IConfiguration EmptyConfig()
        => new ConfigurationBuilder().Build();

    // ── Default (mock) config tests ───────────────────────────────────────────

    [Fact]
    public void RegisterAdapters_DefaultConfig_RegistersMockDataSource()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var registry = new AdapterRegistry(EmptyConfig());

        registry.RegisterAdapters(services);

        using var provider = services.BuildServiceProvider();
        var ds = provider.GetRequiredService<IDiagnosticDataSource>();
        ds.Should().BeOfType<MockDataSource>();
    }

    [Fact]
    public async Task RegisterAdapters_DefaultConfig_RegistersInProcessTransport()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var registry = new AdapterRegistry(EmptyConfig());

        registry.RegisterAdapters(services);

        await using var provider = services.BuildServiceProvider();
        var transport = provider.GetRequiredService<IAgentTransport>();
        transport.Should().BeOfType<InProcessChannelTransport>();
    }

    [Fact]
    public void RegisterAdapters_DefaultConfig_RegistersLocalFileSystemUpload()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var registry = new AdapterRegistry(EmptyConfig());

        registry.RegisterAdapters(services);

        using var provider = services.BuildServiceProvider();
        var upload = provider.GetRequiredService<ITelemetryUploadService>();
        upload.Should().BeOfType<LocalFileSystemUploadService>();
    }

    [Fact]
    public void RegisterAdapters_DefaultConfig_RegistersLocalFileSystemStorageReader()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var registry = new AdapterRegistry(EmptyConfig());

        registry.RegisterAdapters(services);

        using var provider = services.BuildServiceProvider();
        var reader = provider.GetRequiredService<ITelemetryStorageReader>();
        reader.Should().BeOfType<LocalFileSystemStorageReader>();
    }

    // ── DDS data source ───────────────────────────────────────────────────────

    [Fact]
    public void RegisterAdapters_DataSource_Dds_RegistersDdsDiagnosticDataSource()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["adapters:dataSource"] = "dds",
            ["dds:publisherNodeId"] = "test-node",
            ["dds:participant:domainId"] = "0",
        });
        var services = new ServiceCollection();
        services.AddLogging();
        var registry = new AdapterRegistry(config);

        registry.RegisterAdapters(services);

        // DdsSubscriberFactory requires CycloneDDS runtime; check the descriptor only.
        services.Any(s =>
            s.ServiceType == typeof(IDiagnosticDataSource) &&
            s.ImplementationType == typeof(DdsDiagnosticDataSource))
            .Should().BeTrue();
    }

    // ── SharedMemory transport ────────────────────────────────────────────────

    [Fact]
    public async Task RegisterAdapters_Transport_SharedMemory_RegistersSharedMemoryTransport()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["adapters:transport"] = "shared-memory",
            ["sharedMemory:sharedMemoryName"] = "TestRing",
            ["sharedMemory:semaphoreName"] = "TestSem",
            ["sharedMemory:capacityBytes"] = "1048576",
        });
        var services = new ServiceCollection();
        services.AddLogging();
        var registry = new AdapterRegistry(config);

        registry.RegisterAdapters(services);

        // SharedMemoryTransport.ReadAsync opens a ring buffer at runtime, but resolving it is safe.
        await using var provider = services.BuildServiceProvider();
        var transport = provider.GetRequiredService<IAgentTransport>();
        transport.Should().BeOfType<SharedMemoryTransport>();
    }

    // ── Sync upload ───────────────────────────────────────────────────────────

    [Fact]
    public void RegisterAdapters_Upload_Sync_RegistersSyncSystemUploadService()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["adapters:upload"] = "sync",
            ["sync:syncMasterBaseUrl"] = "http://fake-sync/",
        });
        var services = new ServiceCollection();
        services.AddLogging();
        var registry = new AdapterRegistry(config);

        registry.RegisterAdapters(services);

        using var provider = services.BuildServiceProvider();
        var upload = provider.GetRequiredService<ITelemetryUploadService>();
        upload.Should().BeOfType<SyncSystemUploadService>();
    }

    // ── NAS storage reader ────────────────────────────────────────────────────

    [Fact]
    public void RegisterAdapters_StorageReader_Nas_RegistersNasStorageReader()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["adapters:storageReader"] = "nas",
            ["nas:nasRoot"] = @"C:\fake\nas",
        });
        var services = new ServiceCollection();
        services.AddLogging();
        var registry = new AdapterRegistry(config);

        registry.RegisterAdapters(services);

        using var provider = services.BuildServiceProvider();
        var reader = provider.GetRequiredService<ITelemetryStorageReader>();
        reader.Should().BeOfType<NasStorageReader>();
    }

    // ── Unknown adapter values ────────────────────────────────────────────────

    [Fact]
    public void RegisterAdapters_UnknownDataSource_ThrowsInvalidOperationException()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["adapters:dataSource"] = "kafka",
        });
        var services = new ServiceCollection();
        var registry = new AdapterRegistry(config);

        var act = () => registry.RegisterAdapters(services);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*dataSource*")
            .WithMessage("*kafka*");
    }

    [Fact]
    public void RegisterAdapters_UnknownTransport_ThrowsInvalidOperationException()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["adapters:transport"] = "rabbitmq",
        });
        var services = new ServiceCollection();
        var registry = new AdapterRegistry(config);

        var act = () => registry.RegisterAdapters(services);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*transport*")
            .WithMessage("*rabbitmq*");
    }

    // ── Mixed config ──────────────────────────────────────────────────────────

    [Fact]
    public void RegisterAdapters_MixedConfig_DdsDataSourcePlusMockUpload()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["adapters:dataSource"] = "dds",
            ["adapters:upload"] = "local-file-system",
            ["dds:publisherNodeId"] = "test-node",
            ["dds:participant:domainId"] = "0",
        });
        var services = new ServiceCollection();
        services.AddLogging();
        var registry = new AdapterRegistry(config);

        registry.RegisterAdapters(services);

        // DDS data source — check descriptor (avoids CycloneDDS runtime).
        services.Any(s =>
            s.ServiceType == typeof(IDiagnosticDataSource) &&
            s.ImplementationType == typeof(DdsDiagnosticDataSource))
            .Should().BeTrue();

        // Mock upload — resolve directly.
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<ITelemetryUploadService>()
            .Should().BeOfType<LocalFileSystemUploadService>();
    }

    // ── Simulated clock ───────────────────────────────────────────────────────

    [Fact]
    public void RegisterAdapters_Clock_Simulated_RegistersSimulatedClock()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["adapters:clock"] = "simulated",
        });
        var services = new ServiceCollection();
        services.AddLogging();
        var registry = new AdapterRegistry(config);

        registry.RegisterAdapters(services);

        using var provider = services.BuildServiceProvider();
        var clock = provider.GetRequiredService<IClock>();
        clock.Should().BeOfType<SimulatedClock>();
    }

    // ── Extension method ──────────────────────────────────────────────────────

    [Fact]
    public void AddTracerAdapters_ExtensionMethod_RegistersServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddTracerAdapters(EmptyConfig());

        using var provider = services.BuildServiceProvider();
        // Verify at least one core adapter interface is registered.
        provider.GetRequiredService<ITelemetryUploadService>()
            .Should().BeOfType<LocalFileSystemUploadService>();
    }
}
