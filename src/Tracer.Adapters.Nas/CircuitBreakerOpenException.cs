namespace Tracer.Adapters.Nas;

/// <summary>
/// Thrown when the NAS circuit breaker is open and requests are being blocked.
/// </summary>
public sealed class CircuitBreakerOpenException : Exception
{
    public CircuitBreakerOpenException(string message) : base(message) { }
}
