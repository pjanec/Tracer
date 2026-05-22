using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Tracer.Adapters.Sync;
using Tracer.Adapters.Sync.Configuration;
using Tracer.Core.Abstractions;
using Tracer.Core.Domain;
using Tracer.Core.Identity;
using Tracer.Core.Time;
using Xunit;

namespace Tracer.Tests.Unit.Adapters.Sync;

public sealed class SyncSystemUploadServiceTests
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
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new { intentId = "default-intent" }),
                });
            return Task.FromResult(_responses.Dequeue());
        }
    }

    private static (SyncSystemUploadService svc, FakeHttpMessageHandler handler) Build(
        int retryAttempts = 1)
    {
        var handler = new FakeHttpMessageHandler();
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://fake-sync/") };
        var restClient = new SyncMasterRestClient(httpClient, NullLogger<SyncMasterRestClient>.Instance);
        var config = new SyncAdapterConfig
        {
            SyncMasterBaseUrl = "http://fake-sync/",
            RetryAttempts = retryAttempts,
            RetryBaseDelaySeconds = 0,
            RetryMaxDelaySeconds = 1,
        };
        var svc = new SyncSystemUploadService(
            restClient, config, NullLogger<SyncSystemUploadService>.Instance);
        return (svc, handler);
    }

    private static UploadRequest MakeRequest(
        string nodeId = "blue-cmd-01",
        string intervalTs = "20260519T140000Z") => new()
    {
        NodeId = new AgentId(nodeId),
        Interval = new IntervalTimestamp(intervalTs),
        IntervalStartUtc = WallclockTime.Zero,
        IntervalEndUtc = WallclockTime.Zero,
        Files = Array.Empty<FileToUpload>(),
    };

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RequestUploadAsync_Success_ReturnsIntentIdContainingNodeIdAndTimestamp()
    {
        var (svc, handler) = Build();
        handler.Enqueue(HttpStatusCode.OK, new { intentId = "abc" });

        var result = await svc.RequestUploadAsync(MakeRequest(), CancellationToken.None);

        result.Value.Should().Contain("blue-cmd-01");
        result.Value.Should().Contain("20260519T140000Z");
    }

    [Fact]
    public async Task RequestUploadAsync_NullRequest_ThrowsArgumentNullException()
    {
        var (svc, _) = Build();

        Func<Task> act = () => svc.RequestUploadAsync(null!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task GetStatusAsync_CompletedStatus_ReturnsMappedComplete()
    {
        var (svc, handler) = Build();
        handler.Enqueue(HttpStatusCode.OK, new { intentId = "i" });
        var intentId = await svc.RequestUploadAsync(MakeRequest(), CancellationToken.None);

        handler.Enqueue(HttpStatusCode.OK, new { status = "Completed" });
        var status = await svc.GetStatusAsync(intentId, CancellationToken.None);

        status.Should().Be(UploadStatus.Complete);
    }

    [Fact]
    public async Task GetStatusAsync_FailedStatus_ReturnsMappedFailed()
    {
        var (svc, handler) = Build();
        handler.Enqueue(HttpStatusCode.OK, new { intentId = "i" });
        var intentId = await svc.RequestUploadAsync(MakeRequest(), CancellationToken.None);

        handler.Enqueue(HttpStatusCode.OK, new { status = "Failed" });
        var status = await svc.GetStatusAsync(intentId, CancellationToken.None);

        status.Should().Be(UploadStatus.Failed);
    }

    [Fact]
    public async Task GetStatusAsync_InProgressStatus_ReturnsMappedInProgress()
    {
        var (svc, handler) = Build();
        handler.Enqueue(HttpStatusCode.OK, new { intentId = "i" });
        var intentId = await svc.RequestUploadAsync(MakeRequest(), CancellationToken.None);

        handler.Enqueue(HttpStatusCode.OK, new { status = "InProgress" });
        var status = await svc.GetStatusAsync(intentId, CancellationToken.None);

        status.Should().Be(UploadStatus.InProgress);
    }

    [Fact]
    public async Task GetStatusAsync_MalformedIntentId_ReturnsUnknown()
    {
        var (svc, _) = Build();
        var badId = new UploadIntentId("no-pipe-separator");

        var status = await svc.GetStatusAsync(badId, CancellationToken.None);

        status.Should().Be(UploadStatus.Unknown);
    }

    [Fact]
    public async Task RequestUploadAsync_ServerReturns500_ThrowsHttpRequestException()
    {
        var (svc, handler) = Build(retryAttempts: 1);
        handler.Enqueue(HttpStatusCode.InternalServerError);

        Func<Task> act = () => svc.RequestUploadAsync(MakeRequest(), CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task GetStatusAsync_UnknownStatusString_ReturnsUnknown()
    {
        var (svc, handler) = Build();
        handler.Enqueue(HttpStatusCode.OK, new { intentId = "i" });
        var intentId = await svc.RequestUploadAsync(MakeRequest(), CancellationToken.None);

        handler.Enqueue(HttpStatusCode.OK, new { status = "SomeFutureStatus" });
        var status = await svc.GetStatusAsync(intentId, CancellationToken.None);

        status.Should().Be(UploadStatus.Unknown);
    }

    [Fact]
    public async Task WaitForCompletionAsync_AlreadyComplete_ReturnsComplete()
    {
        var (svc, handler) = Build();
        handler.Enqueue(HttpStatusCode.OK, new { intentId = "i" });
        var intentId = await svc.RequestUploadAsync(MakeRequest(), CancellationToken.None);

        handler.Enqueue(HttpStatusCode.OK, new { status = "Completed" });

        var result = await svc.WaitForCompletionAsync(intentId, CancellationToken.None);

        result.Should().Be(UploadStatus.Complete);
    }
}
