using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Tracer.Adapters.Nas.Configuration;
using Tracer.Core.Abstractions;
using Tracer.Core.Domain;
using Tracer.Core.Identity;
using Tracer.Core.Time;

namespace Tracer.Adapters.Nas;

/// <summary>
/// Production <see cref="ITelemetryStorageReader"/> that reads from a NAS (UNC or local) share.
/// Layout: <c>{NasRoot}\telemetry\{nodeId}\{intervalTimestamp}.zip</c>.
/// Only returns intervals whose zip contains the <c>_ready</c> sentinel entry
/// (per <c>sync_addendum_telemetry.md §A3.3</c>).
/// </summary>
public sealed class NasStorageReader : ITelemetryStorageReader
{
    private readonly NasAdapterConfig _config;
    private readonly SmbPathResolver _pathResolver;
    private readonly ILogger<NasStorageReader> _logger;
    private readonly Func<string, ZipArchive> _openZip;
    private readonly Func<DateTimeOffset> _now;

    // Circuit breaker state — per-instance, not static.
    private int _consecutiveFailures;
    private DateTimeOffset? _circuitOpenedAt;
    private readonly object _circuitLock = new();

    private static readonly JsonSerializerOptions _jsonOptions = BuildJsonOptions();

    private const string ReadySentinelEntry = "_ready";
    private const string ManifestEntry = "manifest.json";

    public NasStorageReader(NasAdapterConfig config, ILogger<NasStorageReader> logger,
        Func<string, ZipArchive>? openZip = null, Func<DateTimeOffset>? now = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(logger);
        _config = config;
        _logger = logger;
        _pathResolver = new SmbPathResolver(config.NasRoot);
        _openZip = openZip ?? ZipFile.OpenRead;
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<string>> ListNodesAsync(CancellationToken ct = default)
    {
        var telemetryRoot = _pathResolver.ResolveTelemetryRoot();
        if (!Directory.Exists(telemetryRoot))
            return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

        var nodes = Directory.GetDirectories(telemetryRoot)
            .Select(Path.GetFileName)
            .Where(n => n is not null)
            .Cast<string>()
            .ToArray();

        return Task.FromResult<IReadOnlyList<string>>(nodes);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<IntervalDescriptor>> ListIntervalsAsync(
        string nodeId,
        CancellationToken ct = default)
    {
        var nodeDir = _pathResolver.ResolveNodeDir(nodeId);
        if (!Directory.Exists(nodeDir))
        {
            _logger.LogWarning("NAS node directory not found: {Path}", nodeDir);
            return Array.Empty<IntervalDescriptor>();
        }

        var descriptors = new List<IntervalDescriptor>();
        foreach (var zipPath in Directory.GetFiles(nodeDir, "*.zip").Order())
        {
            var name = Path.GetFileNameWithoutExtension(zipPath);
            if (!IntervalTimestamp.TryParse(name, out var ts))
            {
                _logger.LogWarning("Skipping unrecognized zip file: {Path}", zipPath);
                continue;
            }

            if (!IsReady(zipPath))
            {
                _logger.LogWarning(
                    "Skipping incomplete interval (missing _ready sentinel): {Path}", zipPath);
                continue;
            }

            var manifest = await ReadManifestFromZipAsync(zipPath, ct).ConfigureAwait(false);
            if (manifest is not null)
            {
                descriptors.Add(new IntervalDescriptor(
                    ts,
                    manifest.IntervalStart.ToDateTimeOffset(),
                    manifest.IntervalEnd.ToDateTimeOffset()));
            }
            else
            {
                var start = ts.ToDateTimeOffset();
                descriptors.Add(new IntervalDescriptor(ts, start, start.AddMinutes(5)));
            }
        }

        return descriptors;
    }

    /// <inheritdoc/>
    public async Task<IntervalManifest?> ReadIntervalManifestAsync(
        string nodeId,
        IntervalDescriptor descriptor,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        var zipPath = GetIntervalZipPath(nodeId, descriptor);
        if (!File.Exists(zipPath)) return null;
        return await ReadManifestFromZipAsync(zipPath, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public string GetIntervalZipPath(string nodeId, IntervalDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        return _pathResolver.Resolve(nodeId, descriptor.Timestamp.Value);
    }

    /// <summary>
    /// Returns a <see cref="StagedInterval"/> for the specified interval.
    /// If <see cref="NasAdapterConfig.PreferLocalStaging"/> is <c>false</c>, the source
    /// zip path is returned directly (Windows SMB access is transparent via UNC).
    /// If <c>true</c>, the zip is copied to a temp directory; the temp copy is deleted
    /// when <see cref="StagedInterval.Dispose"/> is called.
    /// </summary>
    public async Task<StagedInterval> StageAsync(
        string nodeId,
        IntervalDescriptor descriptor,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        var sourcePath = GetIntervalZipPath(nodeId, descriptor);

        if (!_config.PreferLocalStaging)
            return new StagedInterval(sourcePath, cleanup: null);

        var tempDir = Path.Combine(Path.GetTempPath(), $"tracer-stage-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var localPath = Path.Combine(tempDir, Path.GetFileName(sourcePath));

        await Task.Run(() => File.Copy(sourcePath, localPath, overwrite: true), ct)
            .ConfigureAwait(false);

        return new StagedInterval(localPath, cleanup: () =>
        {
            try { Directory.Delete(tempDir, recursive: true); }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete temp staging directory: {Dir}", tempDir);
            }
        });
    }

    private T ExecuteFileOp<T>(string zipPath, Func<ZipArchive, T> op)
    {
        lock (_circuitLock)
        {
            if (_circuitOpenedAt is DateTimeOffset openedAt)
            {
                var resetInterval = TimeSpan.FromSeconds(_config.CircuitBreakerResetIntervalSeconds);
                if (_now() - openedAt < resetInterval)
                    throw new CircuitBreakerOpenException(
                        $"NAS circuit breaker is open (tripped at {openedAt:O}). " +
                        $"Will reset after {resetInterval.TotalSeconds}s.");

                // Reset window has passed — allow a probe attempt.
                _circuitOpenedAt = null;
                _consecutiveFailures = 0;
            }
        }

        var lastEx = (IOException?)null;
        for (var attempt = 0; attempt <= _config.RetryOnTransientError; attempt++)
        {
            try
            {
                using var archive = _openZip(zipPath);
                var result = op(archive);
                lock (_circuitLock) { _consecutiveFailures = 0; }
                return result;
            }
            catch (IOException ex)
            {
                lastEx = ex;
                _logger.LogWarning(ex,
                    "NAS transient I/O error (attempt {Attempt}/{Max}) for {Path}",
                    attempt + 1, _config.RetryOnTransientError + 1, zipPath);
                // Small sleep between retries to avoid hammering; skipped on last attempt.
                if (attempt < _config.RetryOnTransientError)
                    Thread.Sleep(_config.RetryBaseDelaySeconds * 100); // 10 % of base delay in ms
            }
        }

        // All retries exhausted.
        lock (_circuitLock)
        {
            _consecutiveFailures++;
            if (_consecutiveFailures >= _config.CircuitBreakerThreshold)
            {
                _circuitOpenedAt = _now();
                _logger.LogError(lastEx,
                    "NAS circuit breaker OPENED after {Failures} consecutive failures",
                    _consecutiveFailures);
            }
        }

        throw lastEx!;
    }

    private bool IsReady(string zipPath)
    {
        try
        {
            return ExecuteFileOp(zipPath, a => a.GetEntry(ReadySentinelEntry) is not null);
        }
        catch (CircuitBreakerOpenException) { throw; }
        catch (InvalidDataException ex)
        {
            _logger.LogWarning(ex, "Skipping incomplete interval archive at {Path}: _ready sentinel missing or zip corrupt", zipPath);
            return false;
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Skipping incomplete interval archive at {Path}: _ready sentinel missing or zip corrupt", zipPath);
            return false;
        }
    }

    private async Task<IntervalManifest?> ReadManifestFromZipAsync(
        string zipPath, CancellationToken ct)
    {
        ZipArchiveEntry? entry;
        try
        {
            entry = ExecuteFileOp(zipPath, a => a.GetEntry(ManifestEntry));
        }
        catch (CircuitBreakerOpenException) { return null; }
        catch (IOException) { return null; }

        if (entry is null) return null;

        // Re-open outside ExecuteFileOp so the archive stays alive for async read.
        using var archive = _openZip(zipPath);
        var liveEntry = archive.GetEntry(ManifestEntry);
        if (liveEntry is null) return null;

        await using var stream = liveEntry.Open();
        try
        {
            return await JsonSerializer.DeserializeAsync<IntervalManifest>(stream, _jsonOptions, ct)
                .ConfigureAwait(false);
        }
        catch (JsonException) { return null; }
    }

    private static JsonSerializerOptions BuildJsonOptions() => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters =
        {
            new IntervalTimestampConverter(),
            new WallclockTimeConverter(),
            new AgentIdConverter(),
        },
    };

    // ── Private JSON converters (mirrors Tracer.Agent.Storage.ManifestWriter) ──

    private sealed class IntervalTimestampConverter : JsonConverter<IntervalTimestamp>
    {
        public override IntervalTimestamp Read(
            ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var s = reader.GetString()
                ?? throw new JsonException("Expected string for IntervalTimestamp.");
            return new IntervalTimestamp(s);
        }

        public override void Write(Utf8JsonWriter writer, IntervalTimestamp value, JsonSerializerOptions options)
            => writer.WriteStringValue(value.Value);
    }

    private sealed class WallclockTimeConverter : JsonConverter<WallclockTime>
    {
        public override WallclockTime Read(
            ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var s = reader.GetString()
                ?? throw new JsonException("Expected string for WallclockTime.");
            return WallclockTime.FromDateTimeOffset(DateTimeOffset.Parse(s));
        }

        public override void Write(Utf8JsonWriter writer, WallclockTime value, JsonSerializerOptions options)
            => writer.WriteStringValue(value.ToDateTimeOffset().ToString("O"));
    }

    private sealed class AgentIdConverter : JsonConverter<AgentId>
    {
        public override AgentId Read(
            ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var s = reader.GetString()
                ?? throw new JsonException("Expected string for AgentId.");
            return new AgentId(s);
        }

        public override void Write(Utf8JsonWriter writer, AgentId value, JsonSerializerOptions options)
            => writer.WriteStringValue(value.Value);
    }
}
