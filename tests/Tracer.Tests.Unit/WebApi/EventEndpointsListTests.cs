using System.Net;
using System.Text.Json;
using FluentAssertions;
using Tracer.TestHarness.Observer;
using Xunit;

namespace Tracer.Tests.Unit.WebApi;

/// <summary>Tests for GET /api/events list endpoint using WebApiFixture (no-op reader).</summary>
public sealed class EventEndpointsListTests : IAsyncDisposable
{
    private readonly WebApiFixture _fixture;

    public EventEndpointsListTests()
    {
        _fixture = WebApiFixture.CreateAsync().GetAwaiter().GetResult();
    }

    [Fact]
    public async Task HandleListAsync_NoSessionId_Returns400()
    {
        var response = await _fixture.Client.GetAsync("/api/events");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task HandleListAsync_LimitOverMax_Returns400ProblemDetails()
    {
        var response = await _fixture.Client.GetAsync("/api/events?sessionId=test&limit=9999");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task HandleListAsync_LimitZero_Returns400ProblemDetails()
    {
        var response = await _fixture.Client.GetAsync("/api/events?sessionId=test&limit=0");

        // limit=0 is less than 1, so should be rejected
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task HandleListAsync_ValidLimitMaximum_NotBadRequest()
    {
        // limit=5000 is the maximum allowed — should not be rejected as 400
        var response = await _fixture.Client.GetAsync("/api/events?sessionId=test&limit=5000");

        // May be 404 (no session in no-op fixture), but NOT 400
        response.StatusCode.Should().NotBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task HandleListAsync_MultipleTopicParams_PassedAsListToService()
    {
        // Multiple query-string params for the same key should parse as array
        var response = await _fixture.Client.GetAsync(
            "/api/events?sessionId=test&topic=a.b&topic=c.d");

        // Not a 400 — routing accepted the multiple params
        response.StatusCode.Should().NotBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task HandleListAsync_NoFilter_Returns200WithEventList()
    {
        // Uses ObserverFixture (real DuckDB) to get a real 200 response.
        // Push a session-start event so GetSessionTimeRangeAsync finds the session.
        await using var obs = await ObserverFixture.CreateAsync();
        var sessionId = $"list-test-{Guid.NewGuid():N}";
        var payload = System.Text.Json.JsonSerializer.Serialize(new { sessionId });
        var now = Tracer.Core.Time.WallclockTime.FromUnixNanoseconds(
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1_000_000L);
        var sessionStartEv = new Tracer.Core.Records.EventRecord
        {
            SequenceNumber = 99_001,
            PublishWallclock = now,
            ReceiveWallclock = now,
            PublisherNode = new Tracer.Core.Identity.AgentId("node-a"),
            SubscriberNode = new Tracer.Core.Identity.AgentId("node-a"),
            Topic = new Tracer.Core.Domain.TopicName("system.session_start"),
            EventId = new Tracer.Core.Identity.EventId(99_001),
            TraceId = new Tracer.Core.Identity.TraceId(99_001),
            PayloadJson = payload,
        };
        await obs.PushAsync([sessionStartEv]);

        var response = await obs.Client.GetAsync(
            $"/api/events?sessionId={Uri.EscapeDataString(sessionId)}");

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        doc.RootElement.GetProperty("events").ValueKind
            .Should().Be(System.Text.Json.JsonValueKind.Array);
        doc.RootElement.GetProperty("totalMatching").GetInt64()
            .Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task HandleListAsync_UnknownSessionId_Returns404ProblemDetails()
    {
        // ObserverFixture (real DuckDB) with a non-existent session → 404
        await using var obs = await ObserverFixture.CreateAsync();
        var response = await obs.Client.GetAsync(
            $"/api/events?sessionId=nonexistent-{Guid.NewGuid():N}");

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound);
    }

    public async ValueTask DisposeAsync() => await _fixture.DisposeAsync();
}
