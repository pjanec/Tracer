using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Tracer.Storage.SavedViews;
using Xunit;

namespace Tracer.Tests.Unit.SavedViews;

public sealed class SqliteSavedViewStoreTests : IDisposable
{
    private readonly string _tempDir;

    public SqliteSavedViewStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"sv-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
    }

    private SqliteSavedViewStore CreateStore(out string dbPath)
    {
        dbPath = Path.Combine(_tempDir, $"sv-{Guid.NewGuid():N}.db");
        return new SqliteSavedViewStore(dbPath, NullLogger<SqliteSavedViewStore>.Instance);
    }

    private static SavedViewRecord MakeRecord(
        string sessionId = "sess-1",
        SavedViewKind kind = SavedViewKind.SavedView,
        string persona = "engineer",
        DateTimeOffset createdAt = default) =>
        new SavedViewRecord
        {
            SavedViewId  = "",
            SessionId    = sessionId,
            Kind         = kind,
            ViewType     = "timeline",
            Url          = "/view?session=sess-1",
            Label        = "My View",
            Persona      = persona,
            CreatedAtUtc = createdAt == default ? DateTimeOffset.UtcNow : createdAt,
            OpenCount    = 0,
        };

    // ─── TRC-P8-004 SC-1 ──────────────────────────────────────────────────────

    [Fact]
    public async Task SchemaInitialization_IsIdempotent()
    {
        var store = CreateStore(out var dbPath);
        await store.InitializeAsync(CancellationToken.None);
        var act = async () => await store.InitializeAsync(CancellationToken.None);
        await act.Should().NotThrowAsync();

        // Verify table and all 3 indexes exist
        await using var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='saved_views'";
        var tableName = await cmd.ExecuteScalarAsync();
        tableName.Should().Be("saved_views");

        var expectedIndexes = new[]
        {
            "idx_saved_views_session_persona",
            "idx_saved_views_session_kind",
            "idx_saved_views_last_opened",
        };
        foreach (var idx in expectedIndexes)
        {
            cmd.CommandText = $"SELECT name FROM sqlite_master WHERE type='index' AND name='{idx}'";
            var name = await cmd.ExecuteScalarAsync();
            name.Should().Be(idx, $"index {idx} should exist");
        }
    }

    // ─── TRC-P8-004 SC-2 ──────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_AssignsUlid_WhenIdEmpty()
    {
        var store = CreateStore(out _);
        await store.InitializeAsync();

        var result = await store.CreateAsync(MakeRecord(), CancellationToken.None);

        result.SavedViewId.Should().NotBeEmpty();
        result.SavedViewId.Should().HaveLength(26);
    }

    // ─── TRC-P8-004 SC-3 ──────────────────────────────────────────────────────

    [Fact]
    public async Task RecordOpenedAsync_IncrementsOpenCount()
    {
        var store = CreateStore(out _);
        await store.InitializeAsync();

        var created = await store.CreateAsync(MakeRecord(), CancellationToken.None);
        await store.RecordOpenedAsync(created.SavedViewId, CancellationToken.None);

        var fetched = await store.GetAsync(created.SavedViewId, CancellationToken.None);
        fetched.Should().NotBeNull();
        fetched!.OpenCount.Should().Be(1);
        fetched.LastOpenedAtUtc.Should().NotBeNull();
    }

    // ─── TRC-P8-004 SC-4 ──────────────────────────────────────────────────────

    [Fact]
    public async Task RecordOpenedAsync_CalledTwice_OpenCountIsTwo()
    {
        var store = CreateStore(out _);
        await store.InitializeAsync();

        var created = await store.CreateAsync(MakeRecord(), CancellationToken.None);
        await store.RecordOpenedAsync(created.SavedViewId, CancellationToken.None);
        await store.RecordOpenedAsync(created.SavedViewId, CancellationToken.None);

        var fetched = await store.GetAsync(created.SavedViewId, CancellationToken.None);
        fetched!.OpenCount.Should().Be(2);
    }

    // ─── TRC-P8-004 SC-5 ──────────────────────────────────────────────────────

    [Fact]
    public async Task FilterByPersona_ReturnsOnlyMatchingPersona()
    {
        var store = CreateStore(out _);
        await store.InitializeAsync();

        await store.CreateAsync(MakeRecord(persona: "engineer"), CancellationToken.None);
        await store.CreateAsync(MakeRecord(persona: "engineer"), CancellationToken.None);
        await store.CreateAsync(MakeRecord(persona: "scenario-author"), CancellationToken.None);

        var filter = new SavedViewFilter { Persona = "engineer" };
        var results = await store.ListAsync(filter, CancellationToken.None);

        results.Should().HaveCount(2);
        results.Should().OnlyContain(r => r.Persona == "engineer");
    }

    // ─── TRC-P8-004 SC-6 ──────────────────────────────────────────────────────

    [Fact]
    public async Task FilterByKind_ReturnsOnlyBookmarks()
    {
        var store = CreateStore(out _);
        await store.InitializeAsync();

        await store.CreateAsync(MakeRecord(kind: SavedViewKind.SavedView), CancellationToken.None);
        await store.CreateAsync(MakeRecord(kind: SavedViewKind.Bookmark), CancellationToken.None);
        await store.CreateAsync(MakeRecord(kind: SavedViewKind.Bookmark), CancellationToken.None);

        var filter = new SavedViewFilter { Kind = SavedViewKind.Bookmark };
        var results = await store.ListAsync(filter, CancellationToken.None);

        results.Should().HaveCount(2);
        results.Should().OnlyContain(r => r.Kind == SavedViewKind.Bookmark);
    }

    // ─── TRC-P8-004 SC-7 ──────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_UpdatesLabelAndDescription()
    {
        var store = CreateStore(out _);
        await store.InitializeAsync();

        var created = await store.CreateAsync(MakeRecord() with { Label = "old" }, CancellationToken.None);
        var updated = await store.UpdateAsync(created with { Label = "new", Description = "desc" }, CancellationToken.None);

        updated.Should().NotBeNull();

        var fetched = await store.GetAsync(created.SavedViewId, CancellationToken.None);
        fetched.Should().NotBeNull();
        fetched!.Label.Should().Be("new");
        fetched.Description.Should().Be("desc");
    }

    // ─── TRC-P8-004 SC-8 ──────────────────────────────────────────────────────

    [Fact]
    public async Task ListAsync_OrderByCreated_Descending()
    {
        var store = CreateStore(out _);
        await store.InitializeAsync();

        var t1 = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var t2 = new DateTimeOffset(2025, 2, 1, 0, 0, 0, TimeSpan.Zero);
        var t3 = new DateTimeOffset(2025, 3, 1, 0, 0, 0, TimeSpan.Zero);

        await store.CreateAsync(MakeRecord(createdAt: t1) with { Label = "oldest" }, CancellationToken.None);
        await store.CreateAsync(MakeRecord(createdAt: t2) with { Label = "middle" }, CancellationToken.None);
        await store.CreateAsync(MakeRecord(createdAt: t3) with { Label = "newest" }, CancellationToken.None);

        var results = await store.ListAsync(new SavedViewFilter { OrderBy = "created" }, CancellationToken.None);

        results.Should().HaveCount(3);
        results[0].Label.Should().Be("newest");
        results[0].CreatedAtUtc.Should().Be(t3);
    }

    // ─── TRC-P8-004 SC-9 ──────────────────────────────────────────────────────

    [Fact]
    public async Task ListAsync_OrderByRecent_NullsLast()
    {
        var store = CreateStore(out _);
        await store.InitializeAsync();

        var createdA = await store.CreateAsync(MakeRecord() with { Label = "A" }, CancellationToken.None);
        await store.CreateAsync(MakeRecord() with { Label = "B" }, CancellationToken.None);

        // Only open view A
        await store.RecordOpenedAsync(createdA.SavedViewId, CancellationToken.None);

        var results = await store.ListAsync(new SavedViewFilter { OrderBy = "recent" }, CancellationToken.None);

        results.Should().HaveCount(2);
        results[0].Label.Should().Be("A");  // has last_opened_at set
        results[1].Label.Should().Be("B");  // null last_opened_at → last
    }

    // ─── TRC-P8-004 SC-10 ─────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_RemovesRecord()
    {
        var store = CreateStore(out _);
        await store.InitializeAsync();

        var created = await store.CreateAsync(MakeRecord(), CancellationToken.None);
        var deleted = await store.DeleteAsync(created.SavedViewId, CancellationToken.None);

        deleted.Should().BeTrue();
        var fetched = await store.GetAsync(created.SavedViewId, CancellationToken.None);
        fetched.Should().BeNull();
    }
}
