using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using EventId = Tracer.Core.Identity.EventId;
using Tracer.Adapters.Mock.Transport;
using Tracer.Adapters.Mock.Upload;
using Tracer.Agent.Configuration;
using Tracer.Agent.Diagnostics;
using Tracer.Agent.Ingestion;
using Tracer.Agent.Lifecycle;
using Tracer.Agent.Storage;
using Tracer.Agent.Time;
using Tracer.Agent.Upload;
using Tracer.Core.Abstractions;
using Tracer.Core.Domain;
using Tracer.Core.Identity;
using Tracer.Core.Queries;
using Tracer.Core.Records;
using Tracer.Core.Time;
using Tracer.Storage.DuckDB;
using Tracer.Storage.DuckDB.Parquet;
using Tracer.TestHarness;
using Xunit;

namespace Tracer.Tests.Integration;

/// <summary>Validates crash-recovery behavior via <see cref="TracerAgentFixture"/>.</summary>
public sealed class AgentRecoveryTests
{
    private static EventRecord MakeEvent(int seq = 1) => new()
    {
        SequenceNumber = (ulong)seq,
        PublishWallclock = WallclockTime.Zero,
        ReceiveWallclock = WallclockTime.Zero,
        PublisherNode = new AgentId("test-node"),
        SubscriberNode = new AgentId("test-node"),
        Topic = new TopicName("test.event"),
        EventId = new EventId((ulong)seq),
        TraceId = TraceId.None,
        PayloadJson = "{}",
    };

    [Fact]
    public async Task OrphanedInterval_FinalizedOnRestart()
    {
        // Create a temp DataRoot with a pre-existing orphaned interval directory
        var dataRoot = Path.Combine(Path.GetTempPath(), $"tracer-recovery-{Guid.NewGuid():N}");
        var logsRoot = Path.Combine(Path.GetTempPath(), $"tracer-recovery-logs-{Guid.NewGuid():N}");
        var uploadRoot = Path.Combine(Path.GetTempPath(), $"tracer-recovery-upload-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dataRoot);
        Directory.CreateDirectory(logsRoot);
        Directory.CreateDirectory(uploadRoot);

        // Create an orphan: an interval directory without _ready
        var orphanTs = "20260519T120000Z";
        var orphanDir = Path.Combine(dataRoot, "intervals", orphanTs);
        Directory.CreateDirectory(orphanDir);
        // No _ready file → orphan

        try
        {
            var agentConfig = new AgentConfig
            {
                NodeId = "recovery-node",
                DataRoot = dataRoot,
                LogsRoot = logsRoot,
                IntervalDuration = TimeSpan.FromHours(1),
                KeepLastNIntervals = 24,
                Transport = new TransportConfig { CapacityRecords = 10_000 },
                UploadService = new UploadServiceConfig { LocalFileSystemRoot = uploadRoot },
            };

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var transport = new InProcessChannelTransport(10_000);
            var uploadService = new LocalFileSystemUploadService(uploadRoot);

            var builder = Host.CreateApplicationBuilder();
            builder.Services.AddSingleton(agentConfig);
            builder.Services.AddSingleton<IAgentTransport>(transport);
            builder.Services.AddSingleton<ITelemetryUploadService>(uploadService);
            builder.Services.AddSingleton(TimeProvider.System);
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
            builder.Services.AddSingleton<IIntervalContext>(sp => sp.GetRequiredService<IntervalRotator>());
            builder.Services.AddSingleton<StartupRecoveryService>();
            builder.Services.AddSingleton<RetentionManager>();
            builder.Services.AddSingleton<AgentStateReporter>();
            builder.Services.AddSingleton<TransportMonitor>();
            builder.Services.AddHostedService<AgentHostedService>();
            builder.Logging.ClearProviders();

            var host = builder.Build();
            await host.StartAsync(cts.Token);

            // Give startup recovery time to process the orphan
            await Task.Delay(500, cts.Token);

            var orphanDirectory = new IntervalDirectory(dataRoot, new IntervalTimestamp(orphanTs));
            orphanDirectory.IsReady.Should().BeTrue("startup recovery should have finalized the orphan");
            orphanDirectory.HasManifest.Should().BeTrue();

            transport.Complete();
            await host.StopAsync(CancellationToken.None);
            host.Dispose();
        }
        finally
        {
            TryDelete(dataRoot);
            TryDelete(logsRoot);
            TryDelete(uploadRoot);
        }
    }

    [Fact]
    public async Task RecoveredManifest_HasCrashReason()
    {
        var dataRoot = Path.Combine(Path.GetTempPath(), $"tracer-recovery2-{Guid.NewGuid():N}");
        var logsRoot = Path.Combine(Path.GetTempPath(), $"tracer-recovery2-logs-{Guid.NewGuid():N}");
        var uploadRoot = Path.Combine(Path.GetTempPath(), $"tracer-recovery2-upload-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dataRoot);
        Directory.CreateDirectory(logsRoot);
        Directory.CreateDirectory(uploadRoot);

        var orphanTs = "20260519T120000Z";
        var orphanDir = Path.Combine(dataRoot, "intervals", orphanTs);
        Directory.CreateDirectory(orphanDir);

        try
        {
            var agentConfig = new AgentConfig
            {
                NodeId = "recovery-node-2",
                DataRoot = dataRoot,
                LogsRoot = logsRoot,
                IntervalDuration = TimeSpan.FromHours(1),
                KeepLastNIntervals = 24,
                Transport = new TransportConfig { CapacityRecords = 10_000 },
                UploadService = new UploadServiceConfig { LocalFileSystemRoot = uploadRoot },
            };

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var transport = new InProcessChannelTransport(10_000);
            var uploadService = new LocalFileSystemUploadService(uploadRoot);

            var builder = Host.CreateApplicationBuilder();
            builder.Services.AddSingleton(agentConfig);
            builder.Services.AddSingleton<IAgentTransport>(transport);
            builder.Services.AddSingleton<ITelemetryUploadService>(uploadService);
            builder.Services.AddSingleton(TimeProvider.System);
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
            builder.Services.AddSingleton<IIntervalContext>(sp => sp.GetRequiredService<IntervalRotator>());
            builder.Services.AddSingleton<StartupRecoveryService>();
            builder.Services.AddSingleton<RetentionManager>();
            builder.Services.AddSingleton<AgentStateReporter>();
            builder.Services.AddSingleton<TransportMonitor>();
            builder.Services.AddHostedService<AgentHostedService>();
            builder.Logging.ClearProviders();

            var host = builder.Build();
            await host.StartAsync(cts.Token);
            await Task.Delay(500, cts.Token);

            var manifestPath = Path.Combine(orphanDir, "manifest.json");
            File.Exists(manifestPath).Should().BeTrue();

            var manifest = await ManifestWriter.ReadAsync(manifestPath, CancellationToken.None);
            manifest.Should().NotBeNull();
            manifest!.FinalizationReason.Should().Be(ManifestFinalizationReason.RecoveryAfterCrash);
            manifest.CaptureGaps.Should().HaveCountGreaterThanOrEqualTo(1);
            manifest.CaptureGaps.Should().Contain(g => g.Reason == CaptureGapReason.UnrecoveredCrashGap);

            transport.Complete();
            await host.StopAsync(CancellationToken.None);
            host.Dispose();
        }
        finally
        {
            TryDelete(dataRoot);
            TryDelete(logsRoot);
            TryDelete(uploadRoot);
        }
    }

    [Fact]
    public async Task AfterRecovery_NewIntervalAcceptsRecords()
    {
        await using var fixture = await TracerAgentFixture.CreateAsync();

        // Push 50 events and force rotation to get a completed interval
        for (int i = 1; i <= 50; i++)
            await fixture.PushAsync(MakeEvent(i));

        await Task.Delay(300);
        await fixture.ForceRotationAsync();

        // Verify the rotated interval has 50 events
        var intervalsDir = Path.Combine(fixture.DataRoot, "intervals");
        var readyDirs = Directory.GetDirectories(intervalsDir)
            .Where(d => File.Exists(Path.Combine(d, "_ready")))
            .ToList();

        readyDirs.Should().HaveCountGreaterThanOrEqualTo(1);

        var dbPath = Path.Combine(readyDirs.Last(), "events.duckdb");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var reader = await DuckDbStorageReader.OpenAsync(
            dbPath,
            NullLogger<DuckDbStorageReader>.Instance,
            cts.Token);

        var count = await reader.CountEventsAsync(EventFilter.All, cts.Token);
        count.Should().Be(50);
    }

    private static void TryDelete(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); } catch { /* best-effort */ }
    }
}
