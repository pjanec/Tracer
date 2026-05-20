using Xunit;

namespace Tracer.Tests.Integration;

public class LiveStreamingTests
{
    [Fact(Skip = "Deferred to TRC-P3-011")]
    public Task PushNotableEvents_AppearOnStreamInOrder() => Task.CompletedTask;

    [Fact(Skip = "Deferred to TRC-P3-011")]
    public Task ClientReconnect_ReceivesNewEventsAfterReconnect() => Task.CompletedTask;

    [Fact(Skip = "Deferred to TRC-P3-011")]
    public Task SlowClient_DropsCountedButStreamRemainsAlive() => Task.CompletedTask;
}
