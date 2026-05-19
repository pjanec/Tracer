using Microsoft.Extensions.Logging;

namespace Tracer.TestHarness.Diagnostics;

/// <summary>
/// An in-memory <see cref="ILogger"/> that captures log messages for
/// later inspection in tests.
/// </summary>
public sealed class TestLogSink : ILogger
{
    private readonly List<string> _messages = new();

    /// <inheritdoc/>
    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);
        _messages.Add(formatter(state, exception));
    }

    /// <inheritdoc/>
    public bool IsEnabled(LogLevel logLevel) => true;

    /// <inheritdoc/>
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    /// <summary>Returns a snapshot of all captured log messages.</summary>
    public IReadOnlyList<string> GetMessages() => _messages.AsReadOnly();
}
