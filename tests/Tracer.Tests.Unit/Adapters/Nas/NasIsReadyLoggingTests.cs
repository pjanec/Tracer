using System.IO.Compression;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Tracer.Adapters.Nas;
using Tracer.Adapters.Nas.Configuration;
using Xunit;

namespace Tracer.Tests.Unit.Adapters.Nas;

/// <summary>Unit tests for warning logging in <see cref="NasStorageReader.IsReady"/> (FIX-B2).</summary>
public sealed class NasIsReadyLoggingTests : IDisposable
{
    private readonly string _nasRoot;

    public NasIsReadyLoggingTests()
    {
        _nasRoot = Path.Combine(Path.GetTempPath(), $"tracer-nas-b2-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_nasRoot);
    }

    public void Dispose()
    {
        try { Directory.Delete(_nasRoot, recursive: true); } catch { /* best-effort */ }
    }

    private static void CreateNodeZip(string dir, string filename)
    {
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, filename), Array.Empty<byte>());
    }

    // ── InvalidDataException → warning logged ─────────────────────────────────

    [Fact]
    public async Task IsReady_InvalidDataException_LogsWarning()
    {
        var nodeDir = Path.Combine(_nasRoot, "telemetry", "node1");
        Directory.CreateDirectory(nodeDir);
        // Write a corrupt zip (non-zip bytes)
        var zipPath = Path.Combine(nodeDir, "20260519T140000Z.zip");
        File.WriteAllBytes(zipPath, new byte[] { 0xFF, 0xFE, 0x00 });

        var capturingLogger = new CapturingNasLogger();
        var reader = new NasStorageReader(
            new NasAdapterConfig { NasRoot = _nasRoot },
            capturingLogger,
            openZip: path => new ZipArchive(new FileStream(path, FileMode.Open)));

        // ListIntervalsAsync calls IsReady which should log a warning for corrupt zip
        var intervals = await reader.ListIntervalsAsync("node1", CancellationToken.None);

        // IsReady logs one warning for the InvalidDataException, and the caller
        // (ListAvailableIntervalsAsync) logs a second warning when IsReady returns false.
        capturingLogger.Warnings.Should().NotBeEmpty(
            because: "corrupt zip (InvalidDataException) must produce at least one warning");
        capturingLogger.Warnings.Should().Contain(
            w => w.Contains("Skipping incomplete interval archive"),
            because: "the FIX-B2 warning must indicate the zip is incomplete or corrupt");
        capturingLogger.Warnings.Should().Contain(
            w => w.Contains(zipPath),
            because: "the warning should include the zip path for diagnosability");
    }

    [Fact]
    public async Task IsReady_InvalidDataException_DoesNotThrow()
    {
        var nodeDir = Path.Combine(_nasRoot, "telemetry", "node2");
        Directory.CreateDirectory(nodeDir);
        var zipPath = Path.Combine(nodeDir, "20260519T140000Z.zip");
        File.WriteAllBytes(zipPath, new byte[] { 0xDE, 0xAD });

        var reader = new NasStorageReader(
            new NasAdapterConfig { NasRoot = _nasRoot },
            new CapturingNasLogger(),
            openZip: path => new ZipArchive(new FileStream(path, FileMode.Open)));

        // Must not throw — just skip the zip
        var intervals = await reader.ListIntervalsAsync("node2", CancellationToken.None);
        intervals.Should().BeEmpty("corrupt zip should be skipped, not throw");
    }

    // ── IOException → warning logged ─────────────────────────────────────────

    [Fact]
    public async Task IsReady_IOException_LogsWarning()
    {
        var nodeDir = Path.Combine(_nasRoot, "telemetry", "node3");
        Directory.CreateDirectory(nodeDir);
        var zipPath = Path.Combine(nodeDir, "20260519T140000Z.zip");
        File.WriteAllBytes(zipPath, Array.Empty<byte>());

        int callCount = 0;
        var capturingLogger = new CapturingNasLogger();
        var reader = new NasStorageReader(
            new NasAdapterConfig
            {
                NasRoot = _nasRoot,
                RetryOnTransientError = 0,
                CircuitBreakerThreshold = 100,
            },
            capturingLogger,
            openZip: _ =>
            {
                callCount++;
                throw new IOException("Simulated IO failure");
            });

        var intervals = await reader.ListIntervalsAsync("node3", CancellationToken.None);

        capturingLogger.Warnings.Should().NotBeEmpty(
            because: "IOException in IsReady must produce a warning");
        capturingLogger.Warnings.Should().Contain(w => w.Contains("Skipping incomplete interval archive"),
            because: "IOException warning must use the standard message template");
    }
}

internal sealed class CapturingNasLogger : ILogger<NasStorageReader>
{
    public List<string> Warnings { get; } = new();
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
        Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (logLevel >= LogLevel.Warning)
            Warnings.Add(formatter(state, exception));
    }
}
