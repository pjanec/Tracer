using FluentAssertions;
using Tracer.Core.Abstractions;
using Tracer.Core.Queries;

namespace Tracer.TestHarness.Assertions;

/// <summary>
/// FluentAssertions-style extension methods for validating
/// <see cref="IDiagnosticStorageReader"/> state.
/// </summary>
public static class StorageAssertions
{
    /// <summary>
    /// Asserts that <see cref="IDiagnosticStorageReader.CountEventsAsync"/> equals
    /// <paramref name="expected"/>.
    /// </summary>
    public static async Task ShouldContainEventCount(
        this IDiagnosticStorageReader reader,
        long expected,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(reader);
        var count = await reader.CountEventsAsync(EventFilter.All, ct).ConfigureAwait(false);
        count.Should().Be(expected, $"storage should contain exactly {expected} events");
    }
}
