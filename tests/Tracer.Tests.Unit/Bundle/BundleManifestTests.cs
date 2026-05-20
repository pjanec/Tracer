using System.Text.Json;
using FluentAssertions;
using Tracer.Bundle.Format;
using Xunit;

namespace Tracer.Tests.Unit.Bundle;

public class BundleManifestTests
{
    private static BundleManifest MakeFullManifest() => new()
    {
        BundleId = Ulid.NewUlid().ToString(),
        SchemaVersion = BundleSchemaV1.CurrentVersion,
        CreatedAtUtc = new DateTimeOffset(2026, 5, 19, 14, 0, 0, TimeSpan.Zero),
        TracerVersion = "1.0.0",
        Writer = new BundleWriterInfo { Tool = "tracer-aggregate", Version = "1.0.0", Host = "dev-box-01" },
        TimeRange = new BundleTimeRange
        {
            StartUtc = new DateTimeOffset(2026, 5, 19, 14, 3, 22, TimeSpan.Zero),
            EndUtc = new DateTimeOffset(2026, 5, 19, 14, 38, 51, TimeSpan.Zero),
        },
        SessionContext = new BundleSessionContext
        {
            SessionId = "5b2f0c40-1234-5678-9abc-def012345678",
            ScenarioId = "combat_engagement_v3",
            Label = "Tuesday training run",
        },
        ParticipatingNodes = new[] { "blue-cmd-01", "red-cmd-01" },
        FastStateScope = "none",
        FastStateEntities = Array.Empty<string>(),
        Statistics = new BundleStatistics
        {
            TotalEvents = 1_247_831,
            TotalSlowStateSamples = 8_420,
            TotalFastStateRows = 184_200,
            UncompressedBytes = 247_892_480,
        },
        Files = new[]
        {
            new BundleFileEntry { Path = "events.duckdb", SizeBytes = 41_943_040, Sha256 = "a3f2b4c8" + new string('0', 56) },
            new BundleFileEntry { Path = "slow_state.duckdb", SizeBytes = 524_288, Sha256 = "b4c5d6e7" + new string('0', 56) },
        },
    };

    [Fact]
    public void BundleManifest_RoundTripsViaJsonSerializer()
    {
        var original = MakeFullManifest();
        var json = JsonSerializer.Serialize(original, BundleManifest.SerializerOptions);
        var restored = JsonSerializer.Deserialize<BundleManifest>(json, BundleManifest.SerializerOptions);

        // Compare via JSON re-serialization: records with IReadOnlyList properties use reference equality
        var json2 = JsonSerializer.Serialize(restored, BundleManifest.SerializerOptions);
        json2.Should().Be(json);
    }

    [Fact]
    public void BundleManifest_CamelCaseJson_ContainsBundleIdKey()
    {
        var manifest = MakeFullManifest();
        var json = JsonSerializer.Serialize(manifest, BundleManifest.SerializerOptions);

        json.Should().Contain("\"bundleId\"");
        json.Should().NotContain("\"BundleId\"");
    }

    [Fact]
    public void BundleSchemaV1_CurrentVersionIsOne()
    {
        BundleSchemaV1.CurrentVersion.Should().Be(1);
    }

    [Fact]
    public void BundleSchemaV1_IsRecognized_TrueForOne_FalseForNinetyNine()
    {
        BundleSchemaV1.IsRecognized(1).Should().BeTrue();
        BundleSchemaV1.IsRecognized(99).Should().BeFalse();
    }

    [Fact]
    public void BundleNaming_SafeFileName_ReplacesColons()
    {
        var result = BundleNaming.SafeFileName("a:b");
        result.Should().NotContain(":");
    }

    [Fact]
    public void BundleNaming_SafeFileName_DistinctInputs_ProduceDifferentOutputs()
    {
        var result1 = BundleNaming.SafeFileName("x:y");
        var result2 = BundleNaming.SafeFileName("x_y");

        result1.Should().NotBe(result2);
    }

    [Fact]
    public void BundleLayout_AllPathConstants_AreNonEmpty()
    {
        var fields = typeof(BundleLayout)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => f.FieldType == typeof(string));

        foreach (var field in fields)
        {
            var value = (string?)field.GetValue(null);
            value.Should().NotBeNullOrEmpty(because: $"BundleLayout.{field.Name} must be a non-empty string");
        }
    }
}
