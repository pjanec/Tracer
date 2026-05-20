using System.Collections.Concurrent;
using Tracer.Core.Records;

namespace Tracer.WebApi.Streaming;

/// <summary>
/// Manages active SSE client connections and broadcasts events to all registered connections.
/// </summary>
public sealed class SseConnectionManager
{
    private readonly ConcurrentDictionary<Guid, SseConnection> _connections = new();
    private readonly SseStreamingOptions _options;

    public SseConnectionManager(SseStreamingOptions options)
    {
        _options = options;
    }

    public int ActiveCount => _connections.Count;

    /// <summary>
    /// Attempts to register a new SSE connection with the given filter.
    /// Returns null if the maximum concurrent client limit is reached.
    /// </summary>
    public SseConnection? TryRegister(SseFilter filter)
    {
        if (_connections.Count >= _options.MaxConcurrentSseClients)
            return null;

        var connection = new SseConnection(filter, _options.PerClientBufferSize);
        _connections[connection.Id] = connection;
        return connection;
    }

    /// <summary>Deregisters and completes the connection with the given ID.</summary>
    public void Deregister(Guid connectionId)
    {
        if (_connections.TryRemove(connectionId, out var conn))
            conn.Complete();
    }

    /// <summary>Broadcasts an event to all registered connections (applying per-connection filters).</summary>
    public ValueTask BroadcastAsync(EventRecord ev, CancellationToken ct)
    {
        foreach (var conn in _connections.Values)
            conn.Enqueue(ev);
        return ValueTask.CompletedTask;
    }
}

