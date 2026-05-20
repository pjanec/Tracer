using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Tracer.Core.Domain;
using Tracer.Core.Identity;
using Tracer.Core.Queries;
using Tracer.Core.Records;
using Tracer.Core.Time;
using Tracer.Storage.DuckDB;
using Tracer.TestHarness;
using Xunit;

namespace Tracer.Tests.Integration;

/// <summary>Validates the full agent interval lifecycle using <see cref="TracerAgentFixture"/>.</summary>
public sealed class AgentIntervalLifecycleTests
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
    public async Task ThreeIntervals_ThreeReadyDirectories()
    {
        await using var fixture = await TracerAgentFixture.CreateAsync();

        await fixture.PushAsync(MakeEvent(1));
        await fixture.ForceRotationAsync();

        await fixture.PushAsync(MakeEvent(2));
        await fixture.ForceRotationAsync();

        await fixture.PushAsync(MakeEvent(3));
        await fixture.ForceRotationAsync();

        var intervalsDir = Path.Combine(fixture.DataRoot, "intervals");
        var readyDirs = Directory.GetDirectories(intervalsDir)
            .Where(d => File.Exists(Path.Combine(d, "_ready")))
            .ToList();

        readyDirs.Should().HaveCountGreaterThanOrEqualTo(3);
    }

    [Fact]
    public async Task RecordCounts_MatchPushed()
    {
        await using var fixture = await TracerAgentFixture.CreateAsync();

        // Push 200 events
        for (int i = 1; i <= 200; i++)
            await fixture.PushAsync(MakeEvent(i));

        // Give ingestion a moment to process
        await Task.Delay(200);

        await fixture.ForceRotationAsync();

        // Find the completed interval's events.duckdb
        var intervalsDir = Path.Combine(fixture.DataRoot, "intervals");
        var readyDir = Directory.GetDirectories(intervalsDir)
            .First(d => File.Exists(Path.Combine(d, "_ready")));

        var dbPath = Path.Combine(readyDir, "events.duckdb");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var reader = await DuckDbStorageReader.OpenAsync(
            dbPath,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<DuckDbStorageReader>.Instance,
            cts.Token);

        var count = await reader.CountEventsAsync(EventFilter.All, cts.Token);
        count.Should().Be(200);
    }

    [Fact]
    public async Task UploadServiceReceivesEachInterval()
    {
        await using var fixture = await TracerAgentFixture.CreateAsync();

        await fixture.PushAsync(MakeEvent(1));
        await fixture.ForceRotationAsync();

        await fixture.PushAsync(MakeEvent(2));
        await fixture.ForceRotationAsync();

        await fixture.PushAsync(MakeEvent(3));
        await fixture.ForceRotationAsync();

        var zips = Directory.GetFiles(fixture.UploadRoot, "*.zip", SearchOption.AllDirectories);
        zips.Should().HaveCountGreaterThanOrEqualTo(3);
    }

    [Fact]
    public async Task NoDataLoss_HealthyConditions()
    {
        await using var fixture = await TracerAgentFixture.CreateAsync();

        for (int i = 1; i <= 500; i++)
            await fixture.PushAsync(MakeEvent(i));

        // Give pipeline time to process
        await Task.Delay(500);

        // Force rotation so we can read the completed interval
        await fixture.ForceRotationAsync();

        var intervalsDir = Path.Combine(fixture.DataRoot, "intervals");
        var readyDir = Directory.GetDirectories(intervalsDir)
            .First(d => File.Exists(Path.Combine(d, "_ready")));

        var dbPath = Path.Combine(readyDir, "events.duckdb");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var reader = await DuckDbStorageReader.OpenAsync(
            dbPath,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<DuckDbStorageReader>.Instance,
            cts.Token);

        var count = await reader.CountEventsAsync(EventFilter.All, cts.Token);
        count.Should().Be(500);
    }
}
