using Tracer.Adapters.Mock.Transport;
using Tracer.Agent.Configuration;
using Tracer.Core.Abstractions;

namespace Tracer.Agent.Transport;

public static class TransportFactory
{
    public static IAgentTransport Create(AgentConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        return config.Transport.Kind switch
        {
            "InProcessChannel" => new InProcessChannelTransport(config.Transport.CapacityRecords),
            _ => throw new InvalidOperationException(
                $"Unknown transport kind: '{config.Transport.Kind}'.")
        };
    }
}
