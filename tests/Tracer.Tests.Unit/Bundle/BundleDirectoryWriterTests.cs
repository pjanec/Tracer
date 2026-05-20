using System.IO.Compression;
using System.Security.Cryptography;
using FluentAssertions;
using Tracer.Bundle.Format;
using Tracer.Bundle.Packaging;
using Xunit;

namespace Tracer.Tests.Unit.Bundle;

public class BundleDirectoryWriterTests
{
    private static async Task<(string stagingPath, BundleManifest manifest)> CreateValidStagingAsync()
    {
        var staging = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(staging);

        await File.WriteAllBytesAsync(Path.Combine(staging, "events.duckdb"), new byte[] { 1, 2, 3, 4, 5 });
        await File.WriteAllBytesAsync(Path.Combine(staging, "slow_state.duckdb"), new byte[] { 6, 7, 8 });

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
                new BundleFileEntry { Path = "events.duckdb",     SizeBytes = 5, Sha256 = "" },
                new BundleFileEntry { Path = "slow_state.duckdb", SizeBytes = 3, Sha256 = "" },
            },
        };

        return (staging, manifest);
    }

    [Fact]
    public async Task WriteAsync_CreatesManifestJson()
    {
        var (staging, manifest) = await CreateValidStagingAsync();
        try
        {
            await BundleDirectoryWriter.WriteAsync(staging, manifest);
            File.Exists(Path.Combine(staging, "manifest.json")).Should().BeTrue();
        }
        finally { Directory.Delete(staging, recursive: true); }
    }

    [Fact]
    public async Task WriteAsync_CreatesChecksumsFileWithOneLinePerManifestFile()
    {
        var (staging, manifest) = await CreateValidStagingAsync();
        try
        {
            await BundleDirectoryWriter.WriteAsync(staging, manifest);
            var lines = (await File.ReadAllLinesAsync(Path.Combine(staging, "checksums.txt")))
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .ToArray();
            lines.Length.Should().Be(manifest.Files.Count);
        }
        finally { Directory.Delete(staging, recursive: true); }
    }

    [Fact]
    public async Task WriteAsync_ChecksumsMatchActualFileHashes()
    {
        var (staging, manifest) = await CreateValidStagingAsync();
        try
        {
            await BundleDirectoryWriter.WriteAsync(staging, manifest);
            var lines = await File.ReadAllLinesAsync(Path.Combine(staging, "checksums.txt"));
            foreach (var line in lines.Where(l => !string.IsNullOrWhiteSpace(l)))
            {
                var parts = line.Split("  ", 2);
                parts.Should().HaveCount(2);
                var expectedHash = parts[0];
                var relativePath = parts[1];
                var actualHash = await BundleDirectoryWriter.ComputeSha256Async(Path.Combine(staging, relativePath));
                actualHash.Should().Be(expectedHash);
            }
        }
        finally { Directory.Delete(staging, recursive: true); }
    }

    [Fact]
    public async Task WriteAsync_CreatesAnnotationsKeep()
    {
        var (staging, manifest) = await CreateValidStagingAsync();
        try
        {
            await BundleDirectoryWriter.WriteAsync(staging, manifest);
            File.Exists(Path.Combine(staging, "annotations", ".keep")).Should().BeTrue();
        }
        finally { Directory.Delete(staging, recursive: true); }
    }

    [Fact]
    public async Task BundleZipWriter_ProducesReadableZip()
    {
        var (staging, manifest) = await CreateValidStagingAsync();
        var zipPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.zip");
        try
        {
            await BundleZipWriter.WriteAsync(staging, manifest, zipPath);

            using var archive = ZipFile.OpenRead(zipPath);
            archive.Should().NotBeNull();
        }
        finally
        {
            Directory.Delete(staging, recursive: true);
            if (File.Exists(zipPath)) File.Delete(zipPath);
        }
    }

    [Fact]
    public async Task BundleZipWriter_ZipContainsManifestAtRoot()
    {
        var (staging, manifest) = await CreateValidStagingAsync();
        var zipPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.zip");
        try
        {
            await BundleZipWriter.WriteAsync(staging, manifest, zipPath);
            using var archive = ZipFile.OpenRead(zipPath);
            archive.GetEntry("manifest.json").Should().NotBeNull();
        }
        finally
        {
            Directory.Delete(staging, recursive: true);
            if (File.Exists(zipPath)) File.Delete(zipPath);
        }
    }

    [Fact]
    public async Task BundleReader_Directory_ReturnsMatchingManifest()
    {
        var (staging, manifest) = await CreateValidStagingAsync();
        try
        {
            await BundleDirectoryWriter.WriteAsync(staging, manifest);
            var read = await BundleReader.ReadManifestAsync(staging);
            read.BundleId.Should().Be(manifest.BundleId);
        }
        finally { Directory.Delete(staging, recursive: true); }
    }

    [Fact]
    public async Task BundleReader_Zip_ReturnsMatchingManifest()
    {
        var (staging, manifest) = await CreateValidStagingAsync();
        var zipPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.zip");
        try
        {
            await BundleZipWriter.WriteAsync(staging, manifest, zipPath);
            var read = await BundleReader.ReadManifestAsync(zipPath);
            read.BundleId.Should().Be(manifest.BundleId);
        }
        finally
        {
            Directory.Delete(staging, recursive: true);
            if (File.Exists(zipPath)) File.Delete(zipPath);
        }
    }

    [Fact]
    public async Task BundleExtractor_ExtractsManifestToTargetDirectory()
    {
        var (staging, manifest) = await CreateValidStagingAsync();
        var zipPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.zip");
        var extractDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            await BundleZipWriter.WriteAsync(staging, manifest, zipPath);
            await BundleExtractor.ExtractAsync(zipPath, extractDir);
            File.Exists(Path.Combine(extractDir, "manifest.json")).Should().BeTrue();
        }
        finally
        {
            Directory.Delete(staging, recursive: true);
            if (File.Exists(zipPath)) File.Delete(zipPath);
            if (Directory.Exists(extractDir)) Directory.Delete(extractDir, recursive: true);
        }
    }
}
