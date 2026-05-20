using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Tracer.Agent.Configuration;
using Tracer.Agent.Storage;
using Tracer.Agent.Upload;
using Tracer.Core.Domain;
using Tracer.Core.Identity;
using Tracer.Core.Queries;
using Tracer.Core.Time;
using Tracer.Storage.DuckDB;

namespace Tracer.Agent.Lifecycle;

public sealed class StartupRecoveryService
{
    private readonly AgentConfig _config;
    private readonly UploadIntentDispatcher _uploadDispatcher;
    private readonly IClock _clock;
    private readonly ILogger<StartupRecoveryService> _logger;

    public StartupRecoveryService(
        AgentConfig config,
        UploadIntentDispatcher uploadDispatcher,
        IClock clock,
        ILogger<StartupRecoveryService> logger)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(uploadDispatcher);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);
        _config = config;
        _uploadDispatcher = uploadDispatcher;
        _clock = clock;
        _logger = logger;
    }

    public async Task RecoverAsync(CancellationToken ct)
    {
        var intervalsRoot = Path.Combine(_config.DataRoot, "intervals");
        if (!Directory.Exists(intervalsRoot))
        {
            Directory.CreateDirectory(intervalsRoot);
            return;
        }

        var orphans = new List<IntervalDirectory>();
        foreach (var folder in Directory.EnumerateDirectories(intervalsRoot))
        {
            var name = Path.GetFileName(folder);
            if (!IntervalTimestamp.TryParse(name, out var ts)) continue;
            var dir = new IntervalDirectory(_config.DataRoot, ts);
            if (!dir.IsReady)
                orphans.Add(dir);
        }

        if (orphans.Count == 0)
        {
            _logger.LogInformation("Startup recovery: no orphaned intervals found");
            return;
        }

        _logger.LogWarning("Startup recovery: found {Count} orphaned interval(s)", orphans.Count);

        foreach (var orphan in orphans.OrderBy(o => o.Timestamp.Value))
        {
            try
            {
                await TryFinalizeAsync(orphan, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to finalize orphaned interval {Interval}; skipping",
                    orphan.Timestamp.Value);
            }
        }
    }

    private async Task TryFinalizeAsync(IntervalDirectory orphan, CancellationToken ct)
    {
        _logger.LogWarning("Finalizing orphaned interval {Interval}", orphan.Timestamp.Value);

        long eventCount = 0;
        long slowStateCount = 0;

        // Try to read events.duckdb (best-effort) — contains both events and slow_state tables
        if (File.Exists(orphan.EventsDbPath))
        {
            try
            {
                await using var reader = await DuckDbStorageReader.OpenAsync(
                    orphan.EventsDbPath,
                    NullLogger<DuckDbStorageReader>.Instance,
                    ct);
                eventCount = await reader.CountEventsAsync(EventFilter.All, ct);
                slowStateCount = await reader.CountSlowStateAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Could not read events.duckdb from orphan {Interval}; using count=0",
                    orphan.Timestamp.Value);
            }
        }

        // Enumerate Parquet fast-state topics
        var fastStateTopics = new List<string>();
        if (Directory.Exists(orphan.FastStateDirectory))
        {
            foreach (var f in Directory.EnumerateFiles(orphan.FastStateDirectory, "*.parquet"))
                fastStateTopics.Add(Path.GetFileNameWithoutExtension(f));
        }

        var startDto = orphan.Timestamp.ToDateTimeOffset();
        var endDto = startDto + _config.IntervalDuration;

        var gap = new CaptureGap
        {
            StartUtc = WallclockTime.FromDateTimeOffset(startDto),
            EndUtc = WallclockTime.FromDateTimeOffset(endDto),
            Reason = CaptureGapReason.UnrecoveredCrashGap,
            DroppedRecordCount = 0,
            Detail = "Interval finalized during startup recovery; some data may be lost",
        };

        var manifest = new IntervalManifest
        {
            IntervalStart = orphan.Timestamp,
            IntervalEnd = IntervalTimestamp.FromUtc(endDto),
            NodeId = new AgentId(_config.NodeId),
            TracerVersion = TracerAgentVersion.Current,
            SchemaVersion = 1,
            EventCount = eventCount,
            SlowStateCount = slowStateCount,
            FastStateTopics = fastStateTopics,
            CaptureGaps = new[] { gap },
            SessionMarkers = Array.Empty<SessionMarker>(),
            FinalizedAt = _clock.Now,
            FinalizationReason = ManifestFinalizationReason.RecoveryAfterCrash,
        };

        await ManifestWriter.WriteAsync(orphan.ManifestPath, manifest, ct);
        orphan.WriteReadySentinel();

        _logger.LogInformation(
            "Finalized recovered interval {Interval}: events={Events}",
            orphan.Timestamp.Value, eventCount);

        await _uploadDispatcher.DispatchAsync(orphan, manifest, ct);
    }
}

