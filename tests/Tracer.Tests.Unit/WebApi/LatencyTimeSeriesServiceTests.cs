using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Tracer.Core.Identity;
using Tracer.Core.Records;
using Tracer.Core.Time;
using Tracer.TestHarness.Observer;
using Tracer.WebApi.Queries;
using Xunit;

namespace Tracer.Tests.Unit.WebApi;

public sealed class LatencyTimeSeriesServiceTests : IAsyncDisposable
{
    private readonly ObserverFixture _fixture;
    private readonly LatencyTimeSeriesService _svc;

    private static readonly DateTimeOffset BaseTime =
        new DateTimeOffset(2026, 1, 10, 10, 0, 0, TimeSpan.Zero);

    private static ulong _nextId = 6_000_000;

    private static WallclockTime At(DateTimeOffset dto) =>
        WallclockTime.FromUnixNanoseconds(dto.ToUnixTimeMilliseconds() * 1_000_000L);

    private static EventRecord MakeEvent(
        DateTimeOffset publishAt, double latencyMs,
        string topic = "ts.topic", string pub = "pa", string sub = "sb")
    {
        var id = _nextId++;
        return new EventRecord
        {
            SequenceNumber = id,
            PublishWallclock = At(publishAt),
            ReceiveWallclock = At(publishAt.AddMilliseconds(latencyMs)),
            PublisherNode = new AgentId(pub),
            SubscriberNode = new AgentId(sub),
            Topic = new Tracer.Core.Domain.TopicName(topic),
            EventId = new EventId(id),
            TraceId = new TraceId(id),
            PayloadJson = "{}",
        };
    }

    public LatencyTimeSeriesServiceTests()
    {
        _fixture = ObserverFixture.CreateAsync().GetAwaiter().GetResult();
        _svc = _fixture.App.Services.GetRequiredService<LatencyTimeSeriesService>();
    }

    private LatencyTimeSeriesQuery MakeQuery(
        DateTimeOffset? from = null, DateTimeOffset? to = null, string? topic = null) =>
        new()
        {
            From = At(from ?? BaseTime.AddSeconds(-1)),
            To = At(to ?? BaseTime.AddHours(6)),
            Topic = topic,
        };

    [Fact]
    public async Task EmptyBundle_ReturnsEmptyPoints()
    {
        var result = await _svc.GetAsync(MakeQuery(), CancellationToken.None);
        result.Points.Should().BeEmpty();
    }

    [Fact]
    public async Task SampleCount_Per_Bucket()
    {
        var topic = $"ts.count.{_nextId}";
        var now = BaseTime;
        // 3 events in first 5-min bucket
        for (var i = 0; i < 3; i++)
            await _fixture.PushAsync(MakeEvent(now.AddMinutes(i), 2.0, topic));
        // 2 events in second 5-min bucket
        for (var i = 0; i < 2; i++)
            await _fixture.PushAsync(MakeEvent(now.AddMinutes(5 + i), 2.0, topic));

        // 4h+ span → 5-min buckets
        var q = MakeQuery(from: now.AddSeconds(-1), to: now.AddHours(6), topic: topic);
        var result = await _svc.GetAsync(q, CancellationToken.None);

        result.BucketSize.Should().Be("5 minutes");
        result.Points.Should().HaveCountGreaterOrEqualTo(2);
    }

    [Fact]
    public void BucketLabel_5min_For4HourSpan()
    {
        var (label, _) = LatencyTimeSeriesService.ChooseBucket(TimeSpan.FromHours(5));
        label.Should().Be("5 minutes");
    }

    [Fact]
    public void BucketLabel_1min_For2HourSpan()
    {
        var (label, _) = LatencyTimeSeriesService.ChooseBucket(TimeSpan.FromHours(2));
        label.Should().Be("1 minute");
    }

    [Fact]
    public void BucketLabel_1sec_For2MinSpan()
    {
        var (label, _) = LatencyTimeSeriesService.ChooseBucket(TimeSpan.FromMinutes(2));
        label.Should().Be("1 second");
    }

    [Fact]
    public async Task P50_P99_Computed()
    {
        var topic = $"ts.perc.{_nextId}";
        var now = BaseTime;
        // 10 events in the SAME 100ms bucket (same timestamp), different latencies: 1ms to 10ms
        // With 1 very high outlier (100ms) to make p99 > p50
        for (var i = 1; i <= 9; i++)
            await _fixture.PushAsync(MakeEvent(now, i * 1.0, topic));
        await _fixture.PushAsync(MakeEvent(now, 100.0, topic)); // spike → p99 ≫ p50

        // 1-min span → 100ms buckets
        var q = MakeQuery(from: now.AddSeconds(-1), to: now.AddSeconds(10), topic: topic);
        var result = await _svc.GetAsync(q, CancellationToken.None);

        result.Points.Should().NotBeEmpty();
        // There should be a bucket where p99 > p50 (due to the spike)
        result.Points.Should().Contain(p => p.P99Ms > p.P50Ms);
    }

    [Fact]
    public async Task TimeRange_Filters_Points()
    {
        var topic = $"ts.range.{_nextId}";
        var now = BaseTime;
        // Events over 2h
        for (var i = 0; i < 12; i++)
            await _fixture.PushAsync(MakeEvent(now.AddMinutes(i * 10), 1.0, topic));

        // Only first 20 minutes (→ 1-min bucket / 10-sec bucket range)
        var q = MakeQuery(from: now.AddSeconds(-1), to: now.AddMinutes(20), topic: topic);
        var result = await _svc.GetAsync(q, CancellationToken.None);

        // All bucket starts should be before now+20min
        result.Points.Should().AllSatisfy(p =>
            p.BucketStartUtc.Should().BeBefore(now.AddMinutes(21)));
    }

    public async ValueTask DisposeAsync() => await _fixture.DisposeAsync();
}
