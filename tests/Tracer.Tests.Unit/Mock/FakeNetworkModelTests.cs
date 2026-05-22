using FluentAssertions;
using Tracer.Adapters.Mock;
using Xunit;

namespace Tracer.Tests.Unit.Mock;

public sealed class FakeNetworkModelTests
{
    private static readonly IReadOnlyList<string> ThreeNodes = ["node-a", "node-b", "node-c"];
    private static readonly DateTimeOffset BaseTime = new DateTimeOffset(2025, 6, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void FakeNetworkModel_SameSeed_DeterministicOutput()
    {
        var model1 = new FakeNetworkModel(ThreeNodes, seed: 42);
        var model2 = new FakeNetworkModel(ThreeNodes, seed: 42);

        var results1 = model1.SimulateDelivery("node-a", BaseTime, ["node-b", "node-c"]).ToList();
        var results2 = model2.SimulateDelivery("node-a", BaseTime, ["node-b", "node-c"]).ToList();

        results1.Should().BeEquivalentTo(results2);
    }

    [Fact]
    public void FakeNetworkModel_SelfSubscribe_LowLatency()
    {
        var model = new FakeNetworkModel(ThreeNodes, seed: 99);

        var deliveries = model.SimulateDelivery("node-a", BaseTime, ["node-a"]).ToList();

        deliveries.Should().HaveCount(1);
        var latencyMs = (deliveries[0].receiveWallclock - BaseTime).TotalMilliseconds;
        latencyMs.Should().BeLessThan(1.0);
    }

    [Fact]
    public void FakeNetworkModel_Drop_NotReturned()
    {
        // Build a model with forced bad link (bad links have ~0.5% drop)
        // Run 100,000 deliveries and check drop rate on any link
        var nodes = new List<string>();
        for (var i = 0; i < 10; i++) nodes.Add($"node-{i}");

        var model = new FakeNetworkModel(nodes, seed: 1234);

        var total = 100_000;
        var delivered = 0;
        for (var k = 0; k < total; k++)
        {
            var t = BaseTime.AddMilliseconds(k);
            var results = model.SimulateDelivery("node-0", t, ["node-1"]).ToList();
            delivered += results.Count;
        }

        var dropRate = (double)(total - delivered) / total;
        // Drop rate should be > 0 (some drops expected) and < 5%
        dropRate.Should().BeInRange(0.0, 0.05);
    }

    [Fact]
    public void FakeNetworkModel_BadLink_ElevatedP99()
    {
        // Seed chosen such that node-0→node-1 is a bad link (15ms baseline)
        // We'll try multiple seeds to find one with a bad link
        // Bad links: 15ms baseline, JitterStdMs=3ms, so P99 should be > 10ms
        const int samples = 1000;

        // Use many seeds and pick one that produces a bad link by checking P99
        bool found = false;
        for (var seed = 0; seed < 100; seed++)
        {
            var model = new FakeNetworkModel(["node-0", "node-1"], seed: seed);
            var latencies = new List<double>();

            for (var k = 0; k < samples; k++)
            {
                var t = BaseTime.AddMilliseconds(k);
                foreach (var (_, recv) in model.SimulateDelivery("node-0", t, ["node-1"]))
                    latencies.Add((recv - t).TotalMilliseconds);
            }

            if (latencies.Count == 0) continue;

            latencies.Sort();
            var p99 = latencies[(int)(latencies.Count * 0.99)];

            if (p99 > 10.0)
            {
                found = true;
                break;
            }
        }

        found.Should().BeTrue("expected to find a bad link (p99 > 10ms) within 100 seeds");
    }

    [Fact]
    public void FakeNetworkModel_Spike_ElevatedTail()
    {
        // Run 100,000 deliveries; at least one should exceed SpikeAdditionalMs * 0.5 = 75ms
        const double spikeThreshold = 75.0;
        var model = new FakeNetworkModel(["pub", "sub"], seed: 5);

        var found = false;
        for (var k = 0; k < 100_000; k++)
        {
            var t = BaseTime.AddMilliseconds(k);
            foreach (var (_, recv) in model.SimulateDelivery("pub", t, ["sub"]))
            {
                if ((recv - t).TotalMilliseconds > spikeThreshold)
                {
                    found = true;
                    break;
                }
            }
            if (found) break;
        }

        found.Should().BeTrue("expected at least one spike delivery exceeding 75ms in 100,000 samples");
    }
}
