using FluentAssertions;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Tracer.Storage.SavedViews;
using Tracer.WebApi.Contracts.Dto;
using Tracer.WebApi.Endpoints;
using Xunit;

namespace Tracer.Tests.Unit.WebApi;

public sealed class SavedViewEndpointsTests : IDisposable
{
    private readonly string _tempDir;

    public SavedViewEndpointsTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"sv-ep-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
    }

    private async Task<SqliteSavedViewStore> CreateStoreAsync()
    {
        var path = Path.Combine(_tempDir, $"sv-{Guid.NewGuid():N}.db");
        var store = new SqliteSavedViewStore(path, NullLogger<SqliteSavedViewStore>.Instance);
        await store.InitializeAsync();
        return store;
    }

    private static CreateSavedViewDto ValidCreateDto(string sessionId = "sess-1") => new()
    {
        SessionId = sessionId,
        Kind      = "SavedView",
        ViewType  = "timeline",
        Url       = "/view?session=sess-1",
        Label     = "My View",
        Persona   = "engineer",
    };

    // ─── SC-1 ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task POST_ValidRequest_Returns201Created()
    {
        var store = await CreateStoreAsync();
        var result = await SavedViewEndpoints.HandleCreateAsync(
            ValidCreateDto(), store, CancellationToken.None);

        var created = result.Result.Should().BeOfType<Created<SavedViewDto>>().Subject;
        created.StatusCode.Should().Be(201);
        created.Location.Should().StartWith("/api/saved-views/");
        created.Value!.SavedViewId.Should().NotBeEmpty().And.HaveLength(26);
    }

    // ─── SC-2 ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task POST_EmptyBody_Returns400()
    {
        var store = await CreateStoreAsync();
        var result = await SavedViewEndpoints.HandleCreateAsync(
            null, store, CancellationToken.None);

        var badRequest = result.Result.Should().BeOfType<BadRequest<ProblemDetails>>().Subject;
        badRequest.StatusCode.Should().Be(400);
    }

    // ─── SC-3 ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task POST_BundleMode_Returns405()
    {
        var store = new ReadOnlySavedViewStore();
        var result = await SavedViewEndpoints.HandleCreateAsync(
            ValidCreateDto(), store, CancellationToken.None);

        var problem = result.Result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(405);
    }

    // ─── SC-4 ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PUT_NonExistentId_Returns404()
    {
        var store = await CreateStoreAsync();
        var dto = new UpdateSavedViewDto { Label = "updated" };
        var result = await SavedViewEndpoints.HandleUpdateAsync(
            "nonexistent-id-00000000000", dto, store, CancellationToken.None);

        result.Result.Should().BeOfType<NotFound>();
    }

    // ─── SC-5 ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PUT_BundleMode_Returns405()
    {
        var store = new ReadOnlySavedViewStore();
        var dto = new UpdateSavedViewDto { Label = "updated" };
        var result = await SavedViewEndpoints.HandleUpdateAsync(
            "some-id", dto, store, CancellationToken.None);

        var problem = result.Result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(405);
    }

    // ─── SC-6 ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DELETE_NonExistentId_Returns404()
    {
        var store = await CreateStoreAsync();
        var result = await SavedViewEndpoints.HandleDeleteAsync(
            "nonexistent-id-00000000000", store, CancellationToken.None);

        result.Result.Should().BeOfType<NotFound>();
    }

    // ─── SC-7 ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DELETE_BundleMode_Returns405()
    {
        var store = new ReadOnlySavedViewStore();
        var result = await SavedViewEndpoints.HandleDeleteAsync(
            "some-id", store, CancellationToken.None);

        var problem = result.Result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(405);
    }

    // ─── SC-8 ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GET_List_FiltersBySessionId()
    {
        var store = await CreateStoreAsync();
        await store.CreateAsync(ValidCreateDto("sess-A").ToRecord(), CancellationToken.None);
        await store.CreateAsync(ValidCreateDto("sess-A").ToRecord(), CancellationToken.None);
        await store.CreateAsync(ValidCreateDto("sess-B").ToRecord(), CancellationToken.None);

        var result = await SavedViewEndpoints.HandleListAsync(
            "sess-A", null, null, null, store, CancellationToken.None);

        var ok = result.Result.Should().BeOfType<Ok<IReadOnlyList<SavedViewDto>>>().Subject;
        ok.Value!.Should().HaveCount(2);
        ok.Value!.Should().OnlyContain(v => v.SessionId == "sess-A");
    }

    // ─── SC-9 ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GET_Single_Returns200WithDto()
    {
        var store = await CreateStoreAsync();
        var created = await store.CreateAsync(ValidCreateDto().ToRecord(), CancellationToken.None);

        var result = await SavedViewEndpoints.HandleGetAsync(
            created.SavedViewId, store, CancellationToken.None);

        var ok = result.Result.Should().BeOfType<Ok<SavedViewDto>>().Subject;
        ok.Value!.SavedViewId.Should().Be(created.SavedViewId);
        ok.Value!.Label.Should().Be("My View");
        ok.Value!.SessionId.Should().Be("sess-1");
    }

    // ─── SC-10 ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task POST_Opened_Always204()
    {
        var store = await CreateStoreAsync();
        var created = await store.CreateAsync(ValidCreateDto().ToRecord(), CancellationToken.None);

        // Call with existing ID
        var result1 = await SavedViewEndpoints.HandleRecordOpenedAsync(
            created.SavedViewId, store, CancellationToken.None);
        result1.StatusCode.Should().Be(204);

        // Call with unknown ID - should still return 204
        var result2 = await SavedViewEndpoints.HandleRecordOpenedAsync(
            "nonexistent-id-000000000000", store, CancellationToken.None);
        result2.StatusCode.Should().Be(204);
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static class CreateSavedViewDtoExtensions
    {
        // not a real extension - just a helper via local method
    }

    private sealed class ReadOnlySavedViewStore : ISavedViewStore
    {
        public Task<IReadOnlyList<SavedViewRecord>> ListAsync(SavedViewFilter f, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<SavedViewRecord>>(Array.Empty<SavedViewRecord>());

        public Task<SavedViewRecord?> GetAsync(string id, CancellationToken ct)
            => throw new InvalidOperationException("Bundle saved views are read-only");

        public Task<SavedViewRecord> CreateAsync(SavedViewRecord r, CancellationToken ct)
            => throw new InvalidOperationException("Bundle saved views are read-only");

        public Task<SavedViewRecord?> UpdateAsync(SavedViewRecord r, CancellationToken ct)
            => throw new InvalidOperationException("Bundle saved views are read-only");

        public Task<bool> DeleteAsync(string id, CancellationToken ct)
            => throw new InvalidOperationException("Bundle saved views are read-only");

        public Task RecordOpenedAsync(string id, CancellationToken ct)
            => Task.CompletedTask;
    }
}

file static class CreateSavedViewDtoHelper
{
    public static SavedViewRecord ToRecord(this CreateSavedViewDto dto) =>
        new SavedViewRecord
        {
            SavedViewId  = "",
            SessionId    = dto.SessionId,
            Kind         = Enum.Parse<SavedViewKind>(dto.Kind, ignoreCase: true),
            ViewType     = dto.ViewType,
            Url          = dto.Url,
            Label        = dto.Label,
            Persona      = dto.Persona,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            OpenCount    = 0,
        };
}
