using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Tracer.Storage.Annotations;
using Xunit;

namespace Tracer.Tests.Unit.Annotations;

public sealed class BundleAnnotationStoreTests : IDisposable
{
    private readonly string _bundleDir;

    private static readonly JsonSerializerOptions s_writeOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    public BundleAnnotationStoreTests()
    {
        _bundleDir = Path.Combine(Path.GetTempPath(), $"bundle-annot-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_bundleDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_bundleDir, recursive: true); } catch { /* best effort */ }
    }

    private async Task WriteAnnotationsJsonAsync(string bundleDir, IEnumerable<AnnotationRecord> records)
    {
        var annotDir = Path.Combine(bundleDir, "annotations");
        Directory.CreateDirectory(annotDir);
        var path = Path.Combine(annotDir, "annotations.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(records, s_writeOptions));
    }

    private static AnnotationRecord MakeRecord(string sessionId = "sess-1", AnnotationKind kind = AnnotationKind.Event,
        string? annotationId = null) =>
        new AnnotationRecord
        {
            AnnotationId = annotationId ?? Ulid.NewUlid().ToString(),
            SessionId    = sessionId,
            Kind         = kind,
            Body         = "Test annotation body",
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };

    // ─── TRC-P8-003 SC-1 ──────────────────────────────────────────────────────

    [Fact]
    public async Task ListAsync_FileAbsent_ReturnsEmpty()
    {
        var store = new BundleAnnotationStore(_bundleDir);
        var result = await store.ListAsync(new AnnotationFilter(), CancellationToken.None);
        result.Should().BeEmpty();
    }

    // ─── TRC-P8-003 SC-2 ──────────────────────────────────────────────────────

    [Fact]
    public async Task ListAsync_ValidFile_ReturnsParsedRecords()
    {
        var records = new[] { MakeRecord(), MakeRecord() };
        await WriteAnnotationsJsonAsync(_bundleDir, records);

        var store = new BundleAnnotationStore(_bundleDir);
        var result = await store.ListAsync(new AnnotationFilter(), CancellationToken.None);

        result.Should().HaveCount(2);
    }

    // ─── TRC-P8-003 SC-3 ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetAsync_MatchingId_ReturnsRecord()
    {
        var record = MakeRecord(annotationId: "01ABCDEFGHIJKLMNOPQRSTUV00");
        await WriteAnnotationsJsonAsync(_bundleDir, new[] { record });

        var store = new BundleAnnotationStore(_bundleDir);
        var result = await store.GetAsync(record.AnnotationId, CancellationToken.None);

        result.Should().NotBeNull();
        result!.AnnotationId.Should().Be(record.AnnotationId);
    }

    // ─── TRC-P8-003 SC-4 ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetAsync_UnknownId_ReturnsNull()
    {
        await WriteAnnotationsJsonAsync(_bundleDir, new[] { MakeRecord() });
        var store = new BundleAnnotationStore(_bundleDir);
        var result = await store.GetAsync("does-not-exist-00000000000", CancellationToken.None);
        result.Should().BeNull();
    }

    // ─── TRC-P8-003 SC-5 ──────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_ThrowsInvalidOperationException()
    {
        var store = new BundleAnnotationStore(_bundleDir);
        var act = async () => await store.CreateAsync(MakeRecord(), CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*read-only*");
    }

    // ─── TRC-P8-003 SC-6 ──────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_ThrowsInvalidOperationException()
    {
        var store = new BundleAnnotationStore(_bundleDir);
        var act = async () => await store.UpdateAsync(MakeRecord(), CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*read-only*");
    }

    // ─── TRC-P8-003 SC-7 ──────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_ThrowsInvalidOperationException()
    {
        var store = new BundleAnnotationStore(_bundleDir);
        var act = async () => await store.DeleteAsync("some-id", CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*read-only*");
    }

    // ─── TRC-P8-003 SC-8 ──────────────────────────────────────────────────────

    [Fact]
    public async Task ExportAllForSessionAsync_FiltersBySessionId()
    {
        var records = new[]
        {
            MakeRecord("session-A"),
            MakeRecord("session-A"),
            MakeRecord("session-B"),
        };
        await WriteAnnotationsJsonAsync(_bundleDir, records);

        var store = new BundleAnnotationStore(_bundleDir);
        var result = await store.ExportAllForSessionAsync("session-A", CancellationToken.None);

        result.Should().HaveCount(2);
        result.Should().OnlyContain(r => r.SessionId == "session-A");
    }

    // ─── TRC-P8-003 SC-9 ──────────────────────────────────────────────────────

    [Fact]
    public async Task Cache_NotRefreshedOnSecondCall()
    {
        var original = new[] { MakeRecord(annotationId: "ID-ORIGINAL-0000000000000") };
        await WriteAnnotationsJsonAsync(_bundleDir, original);

        var store = new BundleAnnotationStore(_bundleDir);
        var first = await store.ListAsync(new AnnotationFilter(), CancellationToken.None);

        // Overwrite the file with different data
        var replacement = new[]
        {
            MakeRecord(annotationId: "ID-REPLACEMENT-000000000"),
            MakeRecord(annotationId: "ID-REPLACEMENT-000000001"),
        };
        await WriteAnnotationsJsonAsync(_bundleDir, replacement);

        var second = await store.ListAsync(new AnnotationFilter(), CancellationToken.None);

        // Should still see the first call's data (cached)
        second.Should().HaveCount(first.Count);
        second[0].AnnotationId.Should().Be(first[0].AnnotationId);
    }

    // ─── TRC-P8-003 SC-10 (extra test from batch instructions) ───────────────

    [Fact]
    public async Task ListAsync_FilterByKind()
    {
        var records = new[]
        {
            MakeRecord(kind: AnnotationKind.Event),
            MakeRecord(kind: AnnotationKind.Event),
            MakeRecord(kind: AnnotationKind.Trace),
        };
        await WriteAnnotationsJsonAsync(_bundleDir, records);

        var store = new BundleAnnotationStore(_bundleDir);
        var result = await store.ListAsync(new AnnotationFilter { Kind = AnnotationKind.Event }, CancellationToken.None);

        result.Should().HaveCount(2);
        result.Should().OnlyContain(r => r.Kind == AnnotationKind.Event);
    }
}
