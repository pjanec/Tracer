using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Tracer.Adapters.Sync;
using Xunit;

namespace Tracer.Tests.Unit.Adapters.Sync;

public sealed class SyncMasterRestClientTests
{
    // ── Fake HTTP handler ────────────────────────────────────────────────────

    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new();

        public void Enqueue(HttpStatusCode status, object? body = null)
        {
            var response = new HttpResponseMessage(status);
            if (body is not null)
                response.Content = JsonContent.Create(body);
            _responses.Enqueue(response);
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            if (_responses.Count == 0)
                throw new InvalidOperationException("No more fake responses queued.");
            return Task.FromResult(_responses.Dequeue());
        }
    }

    private static (SyncMasterRestClient client, FakeHttpMessageHandler handler) BuildClient()
    {
        var handler = new FakeHttpMessageHandler();
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://fake-sync-master/") };
        var client = new SyncMasterRestClient(httpClient, NullLogger<SyncMasterRestClient>.Instance);
        return (client, handler);
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RegisterUploadIntentAsync_SuccessResponse_ReturnsIntentId()
    {
        var (client, handler) = BuildClient();
        handler.Enqueue(HttpStatusCode.OK, new { intentId = "intent-abc-123" });

        var request = new UploadIntentRequest
        {
            NodeId = "blue-cmd-01",
            IntervalTimestamp = "20260519T140000Z",
            IntervalStartUtc = "2026-05-19T14:00:00Z",
            IntervalEndUtc = "2026-05-19T15:00:00Z",
            Files = Array.Empty<TelemetryFileEntry>(),
        };

        var result = await client.RegisterUploadIntentAsync(request, CancellationToken.None);

        result.Should().Be("intent-abc-123");
    }

    [Fact]
    public async Task GetIntentStatusAsync_SuccessResponse_ReturnsStatus()
    {
        var (client, handler) = BuildClient();
        handler.Enqueue(HttpStatusCode.OK, new { status = "Completed" });

        var result = await client.GetIntentStatusAsync("blue-cmd-01", "20260519T140000Z",
            CancellationToken.None);

        result.Should().Be("Completed");
    }
}
