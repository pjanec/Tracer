using System.Security.Cryptography;
using FluentAssertions;
using Tracer.Bundle.Format;
using Tracer.Bundle.Packaging;
using Tracer.Bundle.Validation;
using Xunit;

namespace Tracer.Tests.Unit.Bundle;

public class BundleValidatorTests
{
    private static async Task<string> ComputeSha256Async(string filePath)
    {
        await using var stream = File.OpenRead(filePath);
        var bytes = await SHA256.HashDataAsync(stream);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static async Task<(string stagingPath, BundleManifest manifest)> CreateBundleWithRealHashesAsync()
    {
        var staging = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(staging);

        var eventsData = new byte[] { 1, 2, 3, 4, 5 };
        var slowData = new byte[] { 6, 7, 8 };
        await File.WriteAllBytesAsync(Path.Combine(staging, "events.duckdb"), eventsData);
        await File.WriteAllBytesAsync(Path.Combine(staging, "slow_state.duckdb"), slowData);

        var eventsHash = await ComputeSha256Async(Path.Combine(staging, "events.duckdb"));
        var slowHash = await ComputeSha256Async(Path.Combine(staging, "slow_state.duckdb"));

        var manifest = new BundleManifest
        {
            BundleId = Ulid.NewUlid().ToString(),
            SchemaVersion = BundleSchemaV1.CurrentVersion,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            TracerVersion = "1.0.0",
            Writer = new BundleWriterInfo { Tool = "test", Version = "1.0", Host = "test-host" },
            TimeRange = new BundleTimeRange { StartUtc = DateTimeOffset.UtcNow, EndUtc = DateTimeOffset.UtcNow },
            SessionContext = new BundleSessionContext { SessionId = "s1", ScenarioId = "scenario1" },
            ParticipatingNodes = new[] { "node1" },
            FastStateScope = "none",
            FastStateEntities = Array.Empty<string>(),
            Statistics = new BundleStatistics { TotalEvents = 0, TotalSlowStateSamples = 0, TotalFastStateRows = 0, UncompressedBytes = 0 },
            Files = new[]
            {
                new BundleFileEntry { Path = "events.duckdb",     SizeBytes = 5, Sha256 = eventsHash },
                new BundleFileEntry { Path = "slow_state.duckdb", SizeBytes = 3, Sha256 = slowHash },
            },
        };

        await BundleDirectoryWriter.WriteAsync(staging, manifest);
        return (staging, manifest);
    }

    [Fact]
    public async Task ValidBundle_PassesValidation()
    {
        var (staging, manifest) = await CreateBundleWithRealHashesAsync();
        try
        {
            var result = await BundleValidator.ValidateAsync(staging, manifest);
            result.IsValid.Should().BeTrue();
            result.Errors.Should().BeEmpty();
        }
        finally { Directory.Delete(staging, recursive: true); }
    }

    [Fact]
    public async Task MissingFile_FailsWithFileNotFoundError()
    {
        var (staging, manifest) = await CreateBundleWithRealHashesAsync();
        try
        {
            File.Delete(Path.Combine(staging, "events.duckdb"));
            var result = await BundleValidator.ValidateAsync(staging, manifest);
            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.Code == "FILE_MISSING" && e.Message.Contains("events.duckdb"));
        }
        finally { Directory.Delete(staging, recursive: true); }
    }

    [Fact]
    public async Task UnrecognizedSchemaVersion_FailsValidation()
    {
        var (staging, manifest) = await CreateBundleWithRealHashesAsync();
        try
        {
            var badManifest = manifest with { SchemaVersion = 99 };
            var result = await BundleValidator.ValidateAsync(staging, badManifest);
            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.Code == "SCHEMA_VERSION");
        }
        finally { Directory.Delete(staging, recursive: true); }
    }

    [Fact]
    public async Task CorruptedContent_NonStrictMode_Passes()
    {
        var (staging, manifest) = await CreateBundleWithRealHashesAsync();
        try
        {
            // Overwrite with same-size content, different bytes
            await File.WriteAllBytesAsync(Path.Combine(staging, "events.duckdb"), new byte[] { 9, 9, 9, 9, 9 });

            var result = await BundleValidator.ValidateAsync(staging, manifest, strict: false);
            result.IsValid.Should().BeTrue();
        }
        finally { Directory.Delete(staging, recursive: true); }
    }

    [Fact]
    public async Task CorruptedContent_StrictMode_FailsWithChecksumError()
    {
        var (staging, manifest) = await CreateBundleWithRealHashesAsync();
        try
        {
            // Overwrite with same-size content, different bytes
            await File.WriteAllBytesAsync(Path.Combine(staging, "events.duckdb"), new byte[] { 9, 9, 9, 9, 9 });

            var result = await BundleValidator.ValidateAsync(staging, manifest, strict: true);
            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.Code == "CHECKSUM_MISMATCH");
        }
        finally { Directory.Delete(staging, recursive: true); }
    }

    [Fact]
    public async Task SizeMismatch_FailsInBothModes()
    {
        var (staging, manifest) = await CreateBundleWithRealHashesAsync();
        try
        {
            // Truncate events.duckdb (3 bytes instead of 5)
            await File.WriteAllBytesAsync(Path.Combine(staging, "events.duckdb"), new byte[] { 1, 2, 3 });

            var resultNonStrict = await BundleValidator.ValidateAsync(staging, manifest, strict: false);
            resultNonStrict.IsValid.Should().BeFalse();

            var resultStrict = await BundleValidator.ValidateAsync(staging, manifest, strict: true);
            resultStrict.IsValid.Should().BeFalse();
        }
        finally { Directory.Delete(staging, recursive: true); }
    }

    [Fact]
    public async Task MultipleErrors_AllReported()
    {
        var (staging, manifest) = await CreateBundleWithRealHashesAsync();
        try
        {
            File.Delete(Path.Combine(staging, "events.duckdb"));
            File.Delete(Path.Combine(staging, "slow_state.duckdb"));

            var result = await BundleValidator.ValidateAsync(staging, manifest);
            result.IsValid.Should().BeFalse();
            result.Errors.Should().HaveCountGreaterThanOrEqualTo(2);
        }
        finally { Directory.Delete(staging, recursive: true); }
    }
}
