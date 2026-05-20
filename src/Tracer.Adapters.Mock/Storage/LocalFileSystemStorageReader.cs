using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Tracer.Core.Abstractions;
using Tracer.Core.Domain;
using Tracer.Core.Identity;
using Tracer.Core.Time;

namespace Tracer.Adapters.Mock.Storage;

/// <summary>
/// Reads completed interval data from the local filesystem mock-NAS structure
/// produced by <see cref="Upload.LocalFileSystemUploadService"/>.
/// Layout: {root}/{nodeId}/{intervalTimestamp}.zip
/// </summary>
public sealed class LocalFileSystemStorageReader : ITelemetryStorageReader
{
    private readonly string _root;
    private readonly ILogger<LocalFileSystemStorageReader> _logger;

    private static readonly JsonSerializerOptions _options = BuildOptions();

    public LocalFileSystemStorageReader(string root)
        : this(root, NullLogger<LocalFileSystemStorageReader>.Instance) { }

    public LocalFileSystemStorageReader(string root, ILogger<LocalFileSystemStorageReader> logger)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(logger);
        _root = root;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> ListNodesAsync(CancellationToken ct = default)
    {
        if (!Directory.Exists(_root))
            return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

        var nodes = Directory.GetDirectories(_root)
            .Select(Path.GetFileName)
            .Where(n => n is not null)
            .Cast<string>()
            .ToArray();

        return Task.FromResult<IReadOnlyList<string>>(nodes);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<IntervalDescriptor>> ListIntervalsAsync(
        string nodeId,
        CancellationToken ct = default)
    {
        var nodeDir = Path.Combine(_root, nodeId);
        if (!Directory.Exists(nodeDir))
            return Array.Empty<IntervalDescriptor>();

        var descriptors = new List<IntervalDescriptor>();
        foreach (var zipPath in Directory.GetFiles(nodeDir, "*.zip").Order())
        {
            var name = Path.GetFileNameWithoutExtension(zipPath);
            if (!IntervalTimestamp.TryParse(name, out var ts))
            {
                _logger.LogWarning("Skipping unrecognized zip file: {Path}", zipPath);
                continue;
            }

            // Read the manifest to get exact start/end times
            var manifest = await ReadManifestFromZipAsync(zipPath, ct);
            if (manifest is not null)
            {
                descriptors.Add(new IntervalDescriptor(
                    ts,
                    manifest.IntervalStart.ToDateTimeOffset(),
                    manifest.IntervalEnd.ToDateTimeOffset()));
            }
            else
            {
                // Fall back to timestamp-only descriptor (5-minute default window)
                var start = ts.ToDateTimeOffset();
                descriptors.Add(new IntervalDescriptor(ts, start, start.AddMinutes(5)));
            }
        }

        return descriptors;
    }

    /// <inheritdoc />
    public async Task<IntervalManifest?> ReadIntervalManifestAsync(
        string nodeId,
        IntervalDescriptor descriptor,
        CancellationToken ct = default)
    {
        var zipPath = GetIntervalZipPath(nodeId, descriptor);
        if (!File.Exists(zipPath)) return null;
        return await ReadManifestFromZipAsync(zipPath, ct);
    }

    /// <inheritdoc />
    public string GetIntervalZipPath(string nodeId, IntervalDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        return Path.Combine(_root, nodeId, $"{descriptor.Timestamp.Value}.zip");
    }

    private static async Task<IntervalManifest?> ReadManifestFromZipAsync(string zipPath, CancellationToken ct)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        var entry = archive.GetEntry("manifest.json");
        if (entry is null) return null;

        await using var stream = entry.Open();
        try
        {
            return await JsonSerializer.DeserializeAsync<IntervalManifest>(stream, _options, ct);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static JsonSerializerOptions BuildOptions() => new()
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
