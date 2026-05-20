using System.Net;
using System.Text.Json;
using FluentAssertions;
using Tracer.Core.Domain;
using Tracer.Core.Identity;
using Tracer.Core.Records;
using Tracer.Core.Time;
using Tracer.TestHarness.Observer;
using Xunit;

namespace Tracer.Tests.Unit.WebApi;

public sealed class EventEndpointTests : IAsyncDisposable
{
    private readonly WebApiFixture _fixture;

    public EventEndpointTests()
    {
        _fixture = WebApiFixture.CreateAsync().GetAwaiter().GetResult();
    }

    [Fact]
    public async Task GetEvent_NonHexId_Returns400()
    {
        var response = await _fixture.Client.GetAsync("/api/events/ZZZZZZZZZZZZZZZZ");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetEvent_WrongLengthHexId_Returns400()
    {
        // Only 8 chars, need 16
        var response = await _fixture.Client.GetAsync("/api/events/ABCDEF01");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetEvent_TooLongHexId_Returns400()
    {
        // 17 chars is too long
        var response = await _fixture.Client.GetAsync("/api/events/ABCDEF0123456789A");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetEvent_ValidHexId_UnknownEvent_Returns404OrFails()
    {
        // The pool is not initialized in WebApiFixture so this may return 500.
        // This test verifies that a valid 16-char hex is accepted by the routing layer
        // (does not get a 400). Actual 404 behavior is tested via ObserverFixture in integration tests.
        var response = await _fixture.Client.GetAsync("/api/events/0000000000000001");

        // Not a 400 — routing accepted the ID, query failed against uninitialized pool
        response.StatusCode.Should().NotBe(HttpStatusCode.BadRequest);
    }

    public async ValueTask DisposeAsync()
    {
        await _fixture.DisposeAsync();
    }
}

/// <summary>
/// Event endpoint tests that require a real DuckDB connection.
/// </summary>
public sealed class EventEndpointDataTests : IAsyncDisposable
{
    private readonly ObserverFixture _fixture;

    private static readonly WallclockTime Now =
        WallclockTime.FromUnixNanoseconds(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1_000_000L);

    public EventEndpointDataTests()
    {
        _fixture = ObserverFixture.CreateAsync().GetAwaiter().GetResult();
    }

    [Fact]
    public async Task GetEvent_ValidHexId_Returns200WithEventDto()
    {
        var eventId = new EventId(0xCAFEBABE00000001UL);
        var ev = new EventRecord
        {
            SequenceNumber = 1,
            PublishWallclock = Now,
            ReceiveWallclock = Now,
            PublisherNode = new AgentId("node-alpha"),
            SubscriberNode = new AgentId("node-beta"),
            Topic = new TopicName("test.lookup"),
            EventId = eventId,
            TraceId = new TraceId(0x1234567890ABCDEFUL),
            PayloadJson = @"{""test"":true}",
        };

        await _fixture.PushAsync(ev);

        var response = await _fixture.Client.GetAsync($"/api/events/{eventId}");

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        json.Should().Contain("CAFEBABE00000001");
    }

    [Fact]
    public async Task GetEvent_UnknownId_Returns404()
    {
        var response = await _fixture.Client.GetAsync("/api/events/DEADBEEFDEADBEEF");

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound);
    }

    public async ValueTask DisposeAsync()
    {
        await _fixture.DisposeAsync();
    }
}
