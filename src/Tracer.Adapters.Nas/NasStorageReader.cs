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

    private static readonly JsonSerializerOptions _jsonOptions = BuildJsonOptions();

    private const string ReadySentinelEntry = "_ready";
    private const string ManifestEntry = "manifest.json";

    public NasStorageReader(NasAdapterConfig config, ILogger<NasStorageReader> logger)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(logger);
        _config = config;
        _logger = logger;
        _pathResolver = new SmbPathResolver(config.NasRoot);
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

    private static bool IsReady(string zipPath)
    {
        try
        {
            using var archive = ZipFile.OpenRead(zipPath);
            return archive.GetEntry(ReadySentinelEntry) is not null;
        }
        catch (InvalidDataException) { return false; }
        catch (IOException) { return false; }
    }

    private static async Task<IntervalManifest?> ReadManifestFromZipAsync(
        string zipPath, CancellationToken ct)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        var entry = archive.GetEntry(ManifestEntry);
        if (entry is null) return null;

        await using var stream = entry.Open();
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
