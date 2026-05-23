using FluentAssertions;
using Microsoft.Extensions.Logging;
using Tracer.Agent.Diagnostics;
using Tracer.Core.Abstractions;
using Tracer.Core.Records;
using Tracer.Core.Time;
using Tracer.Tests.Unit.Adapters.DDS;
using Xunit;

namespace Tracer.Tests.Unit.Agent;

public sealed class TransportMonitorTests
{
    // ── Fakes ────────────────────────────────────────────────────────────────

    private sealed class FakeTransport : IAgentTransport
    {
        private readonly Queue<TransportHealth> _healthQueue = new();

        public void EnqueueHealth(long totalDropped) =>
            _healthQueue.Enqueue(new TransportHealth
            {
                TotalDropped = totalDropped,
                TotalReceived = 0,
                PendingCount = 0,
                Capacity = 1000,
                LastReceivedAt = WallclockTime.Zero,
            });

        public TransportHealth GetHealth() =>
            _healthQueue.Count > 0 ? _healthQueue.Dequeue() :
            new TransportHealth
            {
                TotalDropped = 0,
                TotalReceived = 0,
                PendingCount = 0,
                Capacity = 1000,
                LastReceivedAt = WallclockTime.Zero,
            };

        public IAsyncEnumerable<DiagnosticRecord> ReadAsync(CancellationToken ct) =>
            throw new NotImplementedException();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class ThrowingTransport : IAgentTransport
    {
        public TransportHealth GetHealth() =>
            throw new InvalidOperationException("Simulated transport failure");

        public IAsyncEnumerable<DiagnosticRecord> ReadAsync(CancellationToken ct) =>
            throw new NotImplementedException();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task MonitorAsync_DroppedCountIncreases_LogsWarning()
    {
        var transport = new FakeTransport();
        transport.EnqueueHealth(0);   // first poll: no drops
        transport.EnqueueHealth(5);   // second poll: 5 drops

        var logger = new CapturingLogger<TransportMonitor>();
        var monitor = new TransportMonitor(transport, logger,
            pollInterval: TimeSpan.FromMilliseconds(10));

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await monitor.MonitorAsync(cts.Token);

        logger.Warnings.Should().Contain(w => w.Contains("NewDrops=5"));
    }

    [Fact]
    public async Task MonitorAsync_DroppedCountStable_NoWarningLogged()
    {
        var transport = new FakeTransport();
        // Always 0 drops — no warning should be emitted

        var logger = new CapturingLogger<TransportMonitor>();
        var monitor = new TransportMonitor(transport, logger,
            pollInterval: TimeSpan.FromMilliseconds(10));

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(60));
        await monitor.MonitorAsync(cts.Token);

        logger.Warnings.Should().BeEmpty();
    }

    [Fact]
    public async Task MonitorAsync_ExceptionInPoll_DoesNotThrow()
    {
        var transport = new ThrowingTransport();
        var logger = new CapturingLogger<TransportMonitor>();
        var monitor = new TransportMonitor(transport, logger,
            pollInterval: TimeSpan.FromMilliseconds(10));

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(60));

        Func<Task> act = () => monitor.MonitorAsync(cts.Token);
        await act.Should().NotThrowAsync();
    }
}
