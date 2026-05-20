using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Tracer.Agent.Configuration;
using Tracer.Agent.Lifecycle;
using Tracer.Agent.Storage;
using Tracer.Agent.Time;
using Tracer.Agent.Upload;
using Tracer.Core.Abstractions;
using Tracer.Core.Domain;
using Tracer.Core.Time;
using Xunit;

namespace Tracer.Tests.Integration;

/// <summary>
/// Verifies that <see cref="RetentionManager"/> fires the pre-deletion callback
/// and honours the grace-period delay before physically deleting intervals.
/// </summary>
public sealed class RetentionCoordinationTests : IAsyncDisposable
{
    private readonly string _tempDir;

    public RetentionCoordinationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"ret-coord-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    /// <summary>
    /// Callback fires before the interval directory is deleted,
    /// and the configurable delay is honoured.
    /// </summary>
    [Fact]
    public async Task Retention_WaitsBeforeDeletion()
    {
        const int keepLast = 1;
        var config = new AgentConfig
        {
            NodeId = "test",
            DataRoot = _tempDir,
            LogsRoot = _tempDir,
            IntervalDuration = TimeSpan.FromHours(1),
            KeepLastNIntervals = keepLast,
            DiskWatermarkPercent = 10,
        };

        // Create 2 completed intervals (manager keeps 1, so will delete the older one)
        var ts1 = new IntervalTimestamp("20260101T000000Z");
        var ts2 = new IntervalTimestamp("20260101T010000Z");
        var dir1 = new IntervalDirectory(_tempDir, ts1);
        var dir2 = new IntervalDirectory(_tempDir, ts2);
        dir1.EnsureCreated();
        dir1.WriteReadySentinel();
        dir2.EnsureCreated();
        dir2.WriteReadySentinel();

        var manager = new RetentionManager(config, NullLogger<RetentionManager>.Instance);
        manager.SetPreDeletionDelay(TimeSpan.FromMilliseconds(100));

        // Track whether callback fired before deletion
        bool callbackFiredBeforeDeletion = false;
        IntervalDirectory? callbackDir = null;

        manager.SetPreDeletionCallback((dir, ct) =>
        {
            callbackDir = dir;
            // At callback time, the directory should still exist on disk
            callbackFiredBeforeDeletion = Directory.Exists(dir.RootPath);
            return Task.CompletedTask;
        });

        // No open interval (pass null so both completed intervals are candidates)
        await manager.ApplyAsync(openIntervalTimestamp: null, default);

        // Callback must have fired
        callbackFiredBeforeDeletion.Should().BeTrue(
            "the pre-deletion callback must be invoked while the directory still exists");
        callbackDir.Should().NotBeNull();
        callbackDir!.Timestamp.Value.Should().Be(ts1.Value,
            "the older interval (ts1) should have been the eviction target");

        // After apply, the older interval should be deleted
        Directory.Exists(dir1.RootPath).Should().BeFalse(
            "RetentionManager must delete the interval after the grace period");

        // The kept interval should still exist
        Directory.Exists(dir2.RootPath).Should().BeTrue(
            "the newer interval that is within the keep window must not be deleted");
    }

    public ValueTask DisposeAsync()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
        return ValueTask.CompletedTask;
    }
}
