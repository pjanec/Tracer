using System.Text.Json;
using System.Text.Json.Serialization;
using Tracer.Core.Domain;
using Tracer.Core.Identity;
using Tracer.Core.Time;

namespace Tracer.Agent.Storage;

public static class ManifestWriter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters =
        {
            new IntervalTimestampConverter(),
            new WallclockTimeConverter(),
            new AgentIdConverter(),
        },
    };

    public static async Task WriteAsync(string path, IntervalManifest manifest, CancellationToken ct)
    {
        await using var stream = new FileStream(
            path, FileMode.Create, FileAccess.Write, FileShare.None, 65536, useAsync: true);
        await JsonSerializer.SerializeAsync(stream, manifest, Options, ct);
    }

    public static async Task<IntervalManifest?> ReadAsync(string path, CancellationToken ct)
    {
        if (!File.Exists(path)) return null;
        await using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, useAsync: true);
        return await JsonSerializer.DeserializeAsync<IntervalManifest>(stream, Options, ct);
    }

    // ── Converters ──────────────────────────────────────────────────────────

    private sealed class IntervalTimestampConverter : JsonConverter<IntervalTimestamp>
    {
        public override IntervalTimestamp Read(
            ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var s = reader.GetString()
                ?? throw new JsonException("Expected string for IntervalTimestamp.");
            return new IntervalTimestamp(s);
        }

        public override void Write(
            Utf8JsonWriter writer, IntervalTimestamp value, JsonSerializerOptions options)
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

        public override void Write(
            Utf8JsonWriter writer, WallclockTime value, JsonSerializerOptions options)
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

        public override void Write(
            Utf8JsonWriter writer, AgentId value, JsonSerializerOptions options)
            => writer.WriteStringValue(value.Value);
    }
}
