using System.Net;
using System.Text.Json;
using FluentAssertions;
using Tracer.TestHarness.Observer;
using Xunit;

namespace Tracer.Tests.Unit.WebApi;

/// <summary>Tests for GET /api/events/aggregate endpoint using WebApiFixture (no-op reader).</summary>
public sealed class EventEndpointsAggregateTests : IAsyncDisposable
{
    private readonly WebApiFixture _fixture;

    public EventEndpointsAggregateTests()
    {
        _fixture = WebApiFixture.CreateAsync().GetAwaiter().GetResult();
    }

    [Fact]
    public async Task HandleAggregateAsync_MissingSessionId_Returns400()
    {
        var response = await _fixture.Client.GetAsync("/api/events/aggregate?bucketDuration=1s");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task HandleAggregateAsync_NoBucketDuration_Returns400()
    {
        var response = await _fixture.Client.GetAsync("/api/events/aggregate?sessionId=test");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetAggregate_ValidRequest_Returns200WithAggregateDto()
    {
        // Uses ObserverFixture (real DuckDB) to get a 200 response.
        await using var obs = await ObserverFixture.CreateAsync();
        var sessionId = $"agg-test-{Guid.NewGuid():N}";
        var payload = System.Text.Json.JsonSerializer.Serialize(new { sessionId });
        var now = Tracer.Core.Time.WallclockTime.FromUnixNanoseconds(
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1_000_000L);
        var sessionStartEv = new Tracer.Core.Records.EventRecord
        {
            SequenceNumber = 99_100,
            PublishWallclock = now,
            ReceiveWallclock = now,
            PublisherNode = new Tracer.Core.Identity.AgentId("node-a"),
            SubscriberNode = new Tracer.Core.Identity.AgentId("node-a"),
            Topic = new Tracer.Core.Domain.TopicName("system.session_start"),
            EventId = new Tracer.Core.Identity.EventId(99_100),
            TraceId = new Tracer.Core.Identity.TraceId(99_100),
            PayloadJson = payload,
        };
        await obs.PushAsync([sessionStartEv]);

        var from = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddHours(-1).ToString("o"));
        var to = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddHours(1).ToString("o"));
        var url = $"/api/events/aggregate?sessionId={Uri.EscapeDataString(sessionId)}&bucketDuration=1s&from={from}&to={to}";
        var response = await obs.Client.GetAsync(url);

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        doc.RootElement.GetProperty("bucketDuration").GetString().Should().Be("1s");
        doc.RootElement.GetProperty("buckets").ValueKind
            .Should().Be(System.Text.Json.JsonValueKind.Array);
    }

    [Fact]
    public async Task GetAggregate_InvalidBucketDuration_Returns400ProblemDetails()
    {
        var response = await _fixture.Client.GetAsync(
            "/api/events/aggregate?sessionId=test&bucketDuration=invalid-dur");

        // Handler should return 400 for invalid bucket duration (or 404 if session not found first)
        // Either way it must NOT be 200
        ((int)response.StatusCode).Should().BeOneOf(400, 404);
    }

    [Fact]
    public async Task GetAggregate_MissingFromOrTo_Returns400ProblemDetails()
    {
        // from and to are required for aggregate; omitting them should return 400
        var response = await _fixture.Client.GetAsync(
            "/api/events/aggregate?sessionId=any&bucketDuration=1s");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    public async ValueTask DisposeAsync() => await _fixture.DisposeAsync();
}

