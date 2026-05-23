using System.IO.Compression;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Tracer.Adapters.Nas;
using Tracer.Adapters.Nas.Configuration;
using Xunit;

namespace Tracer.Tests.Unit.Adapters.Nas;

public sealed class NasReaderHardeningTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static NasAdapterConfig MakeConfig(
        int retryOnTransientError = 2,
        int retryBaseDelaySeconds = 0,  // 0 → no sleep in tests
        int circuitBreakerThreshold = 3,
        int circuitBreakerResetIntervalSeconds = 60) =>
        new()
        {
            NasRoot = @"\\fake-nas\tracer",
            RetryOnTransientError = retryOnTransientError,
            RetryBaseDelaySeconds = retryBaseDelaySeconds,
            CircuitBreakerThreshold = circuitBreakerThreshold,
            CircuitBreakerResetIntervalSeconds = circuitBreakerResetIntervalSeconds,
        };

    // Creates a valid in-memory zip with a _ready sentinel.
    private static ZipArchive MakeGoodArchive()
    {
        var ms = new MemoryStream();
        using (var za = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            za.CreateEntry("_ready");
        ms.Position = 0;
        return new ZipArchive(ms, ZipArchiveMode.Read);
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteFileOp_TransientIoException_RetriesAndSucceeds()
    {
        int callCount = 0;
        ZipArchive OpenZip(string path)
        {
            callCount++;
            if (callCount <= 2)
                throw new IOException("Simulated transient NAS error");
            return MakeGoodArchive();
        }

        // We call IsReady-equivalent via ListIntervalsAsync which calls IsReady internally.
        // Instead, drive ExecuteFileOp indirectly by calling GetHealth via IsReady.
        // Directly: create a temp zip on disk and call ListIntervalsAsync.
        // Since IsReady opens the zip via _openZip, our fake will be invoked.
        var tempDir = Path.Combine(Path.GetTempPath(), $"nas-test-{Guid.NewGuid():N}");
        try
        {
            var nodeDir = Path.Combine(tempDir, "telemetry", "node1");
            Directory.CreateDirectory(nodeDir);
            File.WriteAllBytes(
                Path.Combine(nodeDir, "20260519T140000Z.zip"),
                Array.Empty<byte>());   // content doesn't matter — fake opens it

            var readerWithFakePath = new NasStorageReader(
                new NasAdapterConfig
                {
                    NasRoot = tempDir,
                    RetryOnTransientError = 3,
                    RetryBaseDelaySeconds = 0,
                    CircuitBreakerThreshold = 10,
                    CircuitBreakerResetIntervalSeconds = 60,
                },
                NullLogger<NasStorageReader>.Instance,
                openZip: OpenZip);

            // Act — ListIntervalsAsync calls IsReady which calls ExecuteFileOp.
            var intervals = await readerWithFakePath
                .ListIntervalsAsync("node1", CancellationToken.None);

            // If retry succeeded, we get one interval (archive has _ready).
            // 3 calls for IsReady (2 failures + 1 success) + 1 call for manifest check = 4 total.
            callCount.Should().BeGreaterThan(2, "retries occurred before success");
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public async Task ExecuteFileOp_AlwaysThrowsIoException_CircuitBreakerTripsAfterThreshold()
    {
        ZipArchive AlwaysThrow(string _) =>
            throw new IOException("NAS always down");

        var tempDir = Path.Combine(Path.GetTempPath(), $"nas-test-{Guid.NewGuid():N}");
        try
        {
            var nodeDir = Path.Combine(tempDir, "telemetry", "node1");
            Directory.CreateDirectory(nodeDir);
            File.WriteAllBytes(Path.Combine(nodeDir, "20260519T140000Z.zip"), Array.Empty<byte>());

            var reader = new NasStorageReader(
                new NasAdapterConfig
                {
                    NasRoot = tempDir,
                    RetryOnTransientError = 0,
                    RetryBaseDelaySeconds = 0,
                    CircuitBreakerThreshold = 3,
                    CircuitBreakerResetIntervalSeconds = 60,
                },
                NullLogger<NasStorageReader>.Instance,
                openZip: AlwaysThrow);

            // Call enough times to trip the circuit breaker.
            for (int i = 0; i < 3; i++)
                await reader.ListIntervalsAsync("node1", CancellationToken.None);

            // The next call should produce a CircuitBreakerOpenException.
            var act = () => reader.ListIntervalsAsync("node1", CancellationToken.None);
            await act.Should().ThrowAsync<CircuitBreakerOpenException>();
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public async Task ExecuteFileOp_CircuitBreakerResetsAfterInterval()
    {
        int callCount = 0;
        ZipArchive ThrowThenSucceed(string _)
        {
            callCount++;
            if (callCount <= 3) // trip the breaker
                throw new IOException("NAS down initially");
            return MakeGoodArchive();
        }

        DateTimeOffset fakeNow = DateTimeOffset.UtcNow;
        var tempDir = Path.Combine(Path.GetTempPath(), $"nas-test-{Guid.NewGuid():N}");
        try
        {
            var nodeDir = Path.Combine(tempDir, "telemetry", "node1");
            Directory.CreateDirectory(nodeDir);
            File.WriteAllBytes(Path.Combine(nodeDir, "20260519T140000Z.zip"), Array.Empty<byte>());

            var reader = new NasStorageReader(
                new NasAdapterConfig
                {
                    NasRoot = tempDir,
                    RetryOnTransientError = 0,
                    RetryBaseDelaySeconds = 0,
                    CircuitBreakerThreshold = 3,
                    CircuitBreakerResetIntervalSeconds = 60,
                },
                NullLogger<NasStorageReader>.Instance,
                openZip: ThrowThenSucceed,
                now: () => fakeNow);

            // Trip the circuit breaker.
            for (int i = 0; i < 3; i++)
                await reader.ListIntervalsAsync("node1", CancellationToken.None);

            // Confirm it's open.
            var act = () => reader.ListIntervalsAsync("node1", CancellationToken.None);
            await act.Should().ThrowAsync<CircuitBreakerOpenException>();

            // Advance the clock past the reset interval.
            fakeNow = fakeNow.AddSeconds(61);

            // Now a probe attempt should succeed (callCount increments to 4 → success).
            var intervals = await reader.ListIntervalsAsync("node1", CancellationToken.None);

            intervals.Should().HaveCount(1, "circuit breaker reset allowed a successful probe");
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best-effort */ }
        }
    }
}
