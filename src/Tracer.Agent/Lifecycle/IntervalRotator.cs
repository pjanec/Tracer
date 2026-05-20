using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using Tracer.Agent.Configuration;
using Tracer.Agent.Ingestion;
using Tracer.Agent.Storage;
using Tracer.Agent.Upload;
using Tracer.Core.Abstractions;
using Tracer.Core.Domain;
using Tracer.Core.Identity;
using Tracer.Core.Records;
using Tracer.Core.Time;
using Tracer.Storage.DuckDB;
using Tracer.Storage.DuckDB.Parquet;

namespace Tracer.Agent.Lifecycle;

public sealed class IntervalRotator : IIntervalContext, IAsyncDisposable
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly IntervalScheduler _scheduler;
    private readonly AgentConfig _config;
    private readonly UploadIntentDispatcher _uploader;
    private readonly IClock _clock;
    private readonly ILogger<IntervalRotator> _logger;

    private IDiagnosticStorageWriter? _currentWriter;
    private IntervalDirectory? _currentDirectory;

    private long _eventCountInCurrent;
    private long _slowStateCountInCurrent;

    private readonly object _topicsLock = new();
    private readonly HashSet<string> _fastStateTopicsInCurrent = new();

    private readonly object _gapsLock = new();
    private readonly List<CaptureGap> _captureGapsInCurrent = new();

    private readonly object _markersLock = new();
    private readonly List<SessionMarker> _sessionMarkersInCurrent = new();

    public IDiagnosticStorageWriter? CurrentWriter => _currentWriter;
    public IntervalDirectory? CurrentDirectory => _currentDirectory;

    public IntervalRotator(
        IntervalScheduler scheduler,
        AgentConfig config,
        UploadIntentDispatcher uploader,
        IClock clock,
        ILogger<IntervalRotator> logger)
    {
        _scheduler = scheduler;
        _config = config;
        _uploader = uploader;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>Opens the current interval. Throws if an interval is already open.</summary>
    public async Task OpenCurrentAsync(CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (_currentWriter is not null)
                throw new InvalidOperationException("An interval is already open.");

            var start = _scheduler.CurrentIntervalStart();
            var ts = IntervalTimestamp.FromUtc(start);
            await OpenInternalAsync(ts, ct);
        }
        finally { _lock.Release(); }
    }

    private async Task OpenInternalAsync(IntervalTimestamp ts, CancellationToken ct)
    {
        var dir = new IntervalDirectory(_config.DataRoot, ts);
        dir.EnsureCreated();

        _currentWriter = await DuckDbStorageWriter.CreateAsync(
            dir.RootPath,
            WellKnownTopicSchemas.ToDictionary(),
            NullLogger<DuckDbStorageWriter>.Instance,
            ct);

        _currentDirectory = dir;
        _eventCountInCurrent = 0;
        _slowStateCountInCurrent = 0;

        lock (_topicsLock) _fastStateTopicsInCurrent.Clear();
        lock (_gapsLock) _captureGapsInCurrent.Clear();
        lock (_markersLock) _sessionMarkersInCurrent.Clear();

        _logger.LogInformation("Opened interval {Interval}", ts.Value);
    }

    /// <summary>
    /// Flushes and closes the current interval, writes the manifest and sentinel,
    /// dispatches an upload intent, and opens the next interval (unless shutting down).
    /// </summary>
    public async Task RotateAsync(ManifestFinalizationReason reason, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (_currentWriter is null) return;

            await _currentWriter.FlushAsync(ct);
            await _currentWriter.DisposeAsync();
            _currentWriter = null;

            var prevDir = _currentDirectory!;
            _currentDirectory = null;

            List<CaptureGap> gaps;
            List<SessionMarker> markers;
            List<string> topics;

            lock (_gapsLock) gaps = new List<CaptureGap>(_captureGapsInCurrent);
            lock (_markersLock) markers = new List<SessionMarker>(_sessionMarkersInCurrent);
            lock (_topicsLock) topics = new List<string>(_fastStateTopicsInCurrent);

            var endDto = prevDir.Timestamp.ToDateTimeOffset() + _config.IntervalDuration;
            var manifest = new IntervalManifest
            {
                IntervalStart = prevDir.Timestamp,
                IntervalEnd = IntervalTimestamp.FromUtc(endDto),
                NodeId = new AgentId(_config.NodeId),
                TracerVersion = TracerAgentVersion.Current,
                SchemaVersion = 1,
                EventCount = Interlocked.Read(ref _eventCountInCurrent),
                SlowStateCount = Interlocked.Read(ref _slowStateCountInCurrent),
                FastStateTopics = topics,
                CaptureGaps = gaps,
                SessionMarkers = markers,
                FinalizedAt = _clock.Now,
                FinalizationReason = reason,
            };

            await ManifestWriter.WriteAsync(prevDir.ManifestPath, manifest, ct);
            prevDir.WriteReadySentinel();

            await _uploader.DispatchAsync(prevDir, manifest, ct);

            _logger.LogInformation("Rotated interval {Interval}, reason={Reason}",
                prevDir.Timestamp.Value, reason);

            if (reason != ManifestFinalizationReason.GracefulShutdown)
            {
                // Use prevTimestamp + duration to avoid reopening the same interval
                // when force-rotating before the wall-clock boundary
                var nextStart = prevDir.Timestamp.ToDateTimeOffset() + _config.IntervalDuration;
                await OpenInternalAsync(IntervalTimestamp.FromUtc(nextStart), ct);
            }
        }
        finally { _lock.Release(); }
    }

    // ── IIntervalContext ─────────────────────────────────────────────────────

    public void NotifyRecordWritten(DiagnosticRecord record)
    {
        switch (record)
        {
            case EventRecord evt:
                Interlocked.Increment(ref _eventCountInCurrent);
                TryExtractSessionMarker(evt);
                break;
            case StateSampleRecord { Rate: StateSampleRate.Slow }:
                Interlocked.Increment(ref _slowStateCountInCurrent);
                break;
            case StateSampleRecord { Rate: StateSampleRate.Fast }:
                lock (_topicsLock)
                    _fastStateTopicsInCurrent.Add(record.Topic.Value);
                break;
        }
    }

    public void NotifyCaptureGap(CaptureGap gap)
    {
        lock (_gapsLock)
            _captureGapsInCurrent.Add(gap);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private void TryExtractSessionMarker(EventRecord evt)
    {
        if (evt.Topic.Value != "system.session_start") return;

        try
        {
            using var doc = JsonDocument.Parse(evt.PayloadJson);
            if (!doc.RootElement.TryGetProperty("sessionId", out var prop)) return;

            var sessionId = prop.GetString();
            if (string.IsNullOrEmpty(sessionId)) return;

            lock (_markersLock)
                _sessionMarkersInCurrent.Add(new SessionMarker
                {
                    SessionId = sessionId,
                    Type = SessionMarkerType.Start,
                    Wallclock = evt.PublishWallclock,
                });
        }
        catch { /* ignore malformed payloads */ }
    }

    private int _disposed;

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
        await RotateAsync(ManifestFinalizationReason.GracefulShutdown, CancellationToken.None);
        _lock.Dispose();
    }
}
