using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Tracer.Storage.Annotations;
using Tracer.Storage.Annotations.Schema;
using Xunit;

namespace Tracer.Tests.Unit.Annotations;

public sealed class SqliteAnnotationStoreTests : IDisposable
{
    private readonly string _tempDir;

    public SqliteAnnotationStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"annot-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
    }

    private SqliteAnnotationStore CreateStore(out string dbPath)
    {
        dbPath = Path.Combine(_tempDir, $"annot-{Guid.NewGuid():N}.db");
        return new SqliteAnnotationStore(dbPath, NullLogger<SqliteAnnotationStore>.Instance);
    }

    private static AnnotationRecord MakeRecord(string sessionId = "sess-1", AnnotationKind kind = AnnotationKind.Event) =>
        new AnnotationRecord
        {
            AnnotationId = "",
            SessionId    = sessionId,
            Kind         = kind,
            EventId      = kind == AnnotationKind.Event ? "0000000000000001" : null,
            Body         = "Test annotation",
            CreatedAtUtc = default,
        };

    // ─── TRC-P8-002 SC-1 ──────────────────────────────────────────────────────

    [Fact]
    public async Task InitializeAsync_CreatesSchemaAndIndexes()
    {
        var store = CreateStore(out var dbPath);
        await store.InitializeAsync(CancellationToken.None);

        File.Exists(dbPath).Should().BeTrue();

        await using var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='annotations'";
        var tableName = await cmd.ExecuteScalarAsync();
        tableName.Should().Be("annotations");

        var expectedIndexes = new[]
        {
            "idx_annotations_session",
            "idx_annotations_event_id",
            "idx_annotations_entity_id",
            "idx_annotations_trace_id",
            "idx_annotations_created_at",
        };

        foreach (var idx in expectedIndexes)
        {
            cmd.CommandText = $"SELECT name FROM sqlite_master WHERE type='index' AND name='{idx}'";
            var name = await cmd.ExecuteScalarAsync();
            name.Should().Be(idx, $"index {idx} should exist");
        }
    }

    // ─── TRC-P8-002 SC-2 ──────────────────────────────────────────────────────

    [Fact]
    public async Task InitializeAsync_IsIdempotent()
    {
        var store = CreateStore(out _);
        await store.InitializeAsync(CancellationToken.None);
        var act = async () => await store.InitializeAsync(CancellationToken.None);
        await act.Should().NotThrowAsync();
    }

    // ─── TRC-P8-002 SC-3 ──────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_GeneratesUlid_WhenIdEmpty()
    {
        var store = CreateStore(out _);
        await store.InitializeAsync();

        var result = await store.CreateAsync(MakeRecord(), CancellationToken.None);

        result.AnnotationId.Should().NotBeEmpty();
        result.AnnotationId.Should().HaveLength(26);
    }

    // ─── TRC-P8-002 SC-4 ──────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_SetsCreatedAtUtc_WhenDefault()
    {
        var store = CreateStore(out _);
        await store.InitializeAsync();

        var before = DateTimeOffset.UtcNow;
        var result = await store.CreateAsync(MakeRecord() with { CreatedAtUtc = default }, CancellationToken.None);
        var after  = DateTimeOffset.UtcNow;

        result.CreatedAtUtc.Should().BeOnOrAfter(before.AddSeconds(-5));
        result.CreatedAtUtc.Should().BeOnOrBefore(after.AddSeconds(5));
    }

    // ─── TRC-P8-002 SC-5 ──────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_SetsModifiedAtUtc()
    {
        var store = CreateStore(out _);
        await store.InitializeAsync();

        var created = await store.CreateAsync(MakeRecord(), CancellationToken.None);
        var updated = await store.UpdateAsync(created with { Body = "Updated body" }, CancellationToken.None);

        updated.Should().NotBeNull();
        updated!.ModifiedAtUtc.Should().NotBeNull();
        updated.ModifiedAtUtc!.Value.Should().BeOnOrAfter(updated.CreatedAtUtc);
    }

    // ─── TRC-P8-002 SC-6 ──────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_UnknownId_ReturnsNull()
    {
        var store = CreateStore(out _);
        await store.InitializeAsync();

        var record = MakeRecord() with { AnnotationId = "nonexistent-id-00000000000" };
        var result = await store.UpdateAsync(record, CancellationToken.None);

        result.Should().BeNull();
    }

    // ─── TRC-P8-002 SC-7 ──────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_UnknownId_ReturnsFalse()
    {
        var store = CreateStore(out _);
        await store.InitializeAsync();

        var result = await store.DeleteAsync("nonexistent-id-00000000000", CancellationToken.None);

        result.Should().BeFalse();
    }

    // ─── TRC-P8-002 SC-8 ──────────────────────────────────────────────────────

    [Fact]
    public async Task ListAsync_FilterBySessionId()
    {
        var store = CreateStore(out _);
        await store.InitializeAsync();

        await store.CreateAsync(MakeRecord("session-A"), CancellationToken.None);
        await store.CreateAsync(MakeRecord("session-A"), CancellationToken.None);
        await store.CreateAsync(MakeRecord("session-B"), CancellationToken.None);

        var results = await store.ListAsync(new AnnotationFilter { SessionId = "session-A" }, CancellationToken.None);

        results.Should().HaveCount(2);
        results.Should().OnlyContain(r => r.SessionId == "session-A");
    }

    // ─── TRC-P8-002 SC-9 ──────────────────────────────────────────────────────

    [Fact]
    public async Task ListAsync_OrdersByCreatedAtDesc()
    {
        var store = CreateStore(out _);
        await store.InitializeAsync();

        const string sid = "order-test-session";
        var t1 = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var t2 = t1.AddHours(1);
        var t3 = t2.AddHours(1);

        await store.CreateAsync(MakeRecord(sid) with { AnnotationId = "A1", CreatedAtUtc = t1 }, CancellationToken.None);
        await store.CreateAsync(MakeRecord(sid) with { AnnotationId = "A2", CreatedAtUtc = t2 }, CancellationToken.None);
        await store.CreateAsync(MakeRecord(sid) with { AnnotationId = "A3", CreatedAtUtc = t3 }, CancellationToken.None);

        var results = await store.ListAsync(new AnnotationFilter { SessionId = sid }, CancellationToken.None);

        results.Should().HaveCount(3);
        results[0].CreatedAtUtc.Should().Be(t3);
        results[1].CreatedAtUtc.Should().Be(t2);
        results[2].CreatedAtUtc.Should().Be(t1);
    }

    // ─── TRC-P8-002 SC-10 ─────────────────────────────────────────────────────

    [Fact]
    public async Task ListAsync_RespectsLimit()
    {
        var store = CreateStore(out _);
        await store.InitializeAsync();

        const string sid = "limit-test-session";
        for (int i = 0; i < 5; i++)
            await store.CreateAsync(MakeRecord(sid), CancellationToken.None);

        var results = await store.ListAsync(new AnnotationFilter { SessionId = sid, Limit = 2 }, CancellationToken.None);

        results.Should().HaveCount(2);
    }

    // ─── TRC-P8-002 SC-11 ─────────────────────────────────────────────────────

    [Fact]
    public async Task Tags_RoundTrip()
    {
        var store = CreateStore(out _);
        await store.InitializeAsync();

        var record = MakeRecord() with { Tags = new[] { "alpha", "beta" } };
        var created = await store.CreateAsync(record, CancellationToken.None);

        var fetched = await store.GetAsync(created.AnnotationId, CancellationToken.None);

        fetched.Should().NotBeNull();
        fetched!.Tags.Should().Equal("alpha", "beta");
    }

    // ─── TRC-P8-001 SC-5 (also TRC-P8-002 SC-12 helper) ─────────────────────

    [Fact]
    public void AnnotationFilter_LimitDefaultIs500()
    {
        new AnnotationFilter().Limit.Should().Be(500);
    }

    // ─── TRC-P8-002 SC-12 ─────────────────────────────────────────────────────

    [Fact]
    public async Task NoSqlInjection_BodyContainingSqlText()
    {
        var store = CreateStore(out var dbPath);
        await store.InitializeAsync();

        var maliciousBody = "'; DROP TABLE annotations; --";
        var record = MakeRecord() with { Body = maliciousBody };
        var created = await store.CreateAsync(record, CancellationToken.None);

        // The SQL built by BuildSelectSql should not contain the literal injection text
        var (sql, _) = SqliteAnnotationStore.BuildSelectSql(new AnnotationFilter());
        sql.Should().NotContain(maliciousBody);

        // The record should be retrievable and the table should still exist
        var fetched = await store.GetAsync(created.AnnotationId, CancellationToken.None);
        fetched.Should().NotBeNull();
        fetched!.Body.Should().Be(maliciousBody);

        var list = await store.ListAsync(new AnnotationFilter(), CancellationToken.None);
        list.Should().NotBeEmpty();
    }
}

public sealed class AnnotationsSchemaTests
{
    [Fact]
    public async Task AnnotationsSchema_ExecutesWithoutError()
    {
        await using var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();

        var statements = AnnotationsSchema.CreateSql
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var stmt in statements)
        {
            if (string.IsNullOrWhiteSpace(stmt)) continue;
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = stmt;
            await cmd.ExecuteNonQueryAsync();
        }

        await using var check = conn.CreateCommand();
        check.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='annotations'";
        var tableName = await check.ExecuteScalarAsync();
        tableName.Should().Be("annotations");
    }

    [Fact]
    public async Task AnnotationsSchema_IsIdempotent()
    {
        await using var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();

        var statements = AnnotationsSchema.CreateSql
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        // Execute twice
        for (int pass = 0; pass < 2; pass++)
        {
            foreach (var stmt in statements)
            {
                if (string.IsNullOrWhiteSpace(stmt)) continue;
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = stmt;
                var act = async () => await cmd.ExecuteNonQueryAsync();
                await act.Should().NotThrowAsync($"pass {pass + 1}");
            }
        }
    }
}
