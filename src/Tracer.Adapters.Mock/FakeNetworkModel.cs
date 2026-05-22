namespace Tracer.Adapters.Mock;

/// <summary>
/// Simulates per-link network delivery with configurable latency, jitter, drops, and spikes.
/// Deterministic given the same seed.
/// </summary>
public sealed class FakeNetworkModel
{
    private readonly IReadOnlyList<string> _allNodes;
    private readonly Dictionary<(string publisher, string subscriber), LinkProfile> _links;

    private sealed record LinkProfile(
        double BaseLatencyMs,
        double JitterStdMs,
        double DropProbability,
        double SpikeProbability,
        double SpikeAdditionalMs);

    public FakeNetworkModel(IReadOnlyList<string> allNodes, int seed)
    {
        ArgumentNullException.ThrowIfNull(allNodes);
        _allNodes = allNodes;
        _links = BuildLinks(allNodes, seed);
    }

    private static Dictionary<(string, string), LinkProfile> BuildLinks(IReadOnlyList<string> nodes, int seed)
    {
        var links = new Dictionary<(string, string), LinkProfile>();
        var masterRng = new Random(seed);

        for (var i = 0; i < nodes.Count; i++)
        {
            for (var j = 0; j < nodes.Count; j++)
            {
                var pub = nodes[i];
                var sub = nodes[j];

                if (pub == sub)
                {
                    links[(pub, sub)] = new LinkProfile(
                        BaseLatencyMs: 0.1,
                        JitterStdMs: 0.05,
                        DropProbability: 0.0,
                        SpikeProbability: 0.0,
                        SpikeAdditionalMs: 0.0);
                    continue;
                }

                // Deterministic per-link seed
                var linkSeed = masterRng.Next();
                var linkRng = new Random(linkSeed);

                var isBad = linkRng.NextDouble() < 0.15; // 15% bad links
                links[(pub, sub)] = isBad
                    ? new LinkProfile(
                        BaseLatencyMs: 15.0,
                        JitterStdMs: 3.0,
                        DropProbability: 0.005,
                        SpikeProbability: 0.001,
                        SpikeAdditionalMs: 150.0)
                    : new LinkProfile(
                        BaseLatencyMs: 1.5,
                        JitterStdMs: 0.4,
                        DropProbability: 0.001,
                        SpikeProbability: 0.001,
                        SpikeAdditionalMs: 150.0);
            }
        }

        return links;
    }

    /// <summary>
    /// Simulates delivery of an event from <paramref name="publisherNode"/> to each subscriber.
    /// Omits entries for subscribers that "dropped" the message.
    /// </summary>
    public IEnumerable<(string subscriberNode, DateTimeOffset receiveWallclock)> SimulateDelivery(
        string publisherNode,
        DateTimeOffset publishWallclock,
        IReadOnlyList<string> subscriberNodes)
    {
        ArgumentNullException.ThrowIfNull(subscriberNodes);
        foreach (var sub in subscriberNodes)
        {
            if (!_links.TryGetValue((publisherNode, sub), out var profile))
                continue;

            // Each delivery gets its own deterministic RNG seeded from profile hash
            var rng = new Random(HashCode.Combine(publisherNode, sub, publishWallclock.Ticks));

            // Drop check
            if (rng.NextDouble() < profile.DropProbability)
                continue;

            // Box-Muller for Gaussian jitter
            var u1 = rng.NextDouble();
            var u2 = rng.NextDouble();
            // Avoid log(0)
            u1 = Math.Max(u1, 1e-10);
            var jitter = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2) * profile.JitterStdMs;

            var latencyMs = profile.BaseLatencyMs + jitter;

            // Spike check
            if (rng.NextDouble() < profile.SpikeProbability)
                latencyMs += profile.SpikeAdditionalMs;

            var receiveWallclock = publishWallclock.AddMilliseconds(latencyMs);
            yield return (sub, receiveWallclock);
        }
    }
}
