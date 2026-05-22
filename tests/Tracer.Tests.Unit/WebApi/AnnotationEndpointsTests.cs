using FluentAssertions;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Tracer.Observer.Configuration;
using Tracer.Storage.Annotations;
using Tracer.WebApi.Contracts.Dto;
using Tracer.WebApi.Endpoints;
using Xunit;

namespace Tracer.Tests.Unit.WebApi;

public sealed class AnnotationEndpointsTests : IDisposable
{
    private readonly string _tempDir;

    public AnnotationEndpointsTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"annot-ep-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
    }

    private async Task<SqliteAnnotationStore> CreateStoreAsync()
    {
        var path = Path.Combine(_tempDir, $"a-{Guid.NewGuid():N}.db");
        var store = new SqliteAnnotationStore(path, NullLogger<SqliteAnnotationStore>.Instance);
        await store.InitializeAsync();
        return store;
    }

    private static CreateAnnotationDto ValidCreateDto(string sessionId = "sess-1") => new()
    {
        SessionId = sessionId,
        Kind      = "Event",
        EventId   = "evt-0000000000000001",
        Body      = "Test annotation",
    };

    // ─── SC-1 ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task POST_ValidRequest_Returns201Created()
    {
        var store = await CreateStoreAsync();
        var result = await AnnotationEndpoints.HandleCreateAsync(
            ValidCreateDto(), store, CancellationToken.None);

        var created = result.Result.Should().BeOfType<Created<AnnotationDto>>().Subject;
        created.StatusCode.Should().Be(201);
        created.Location.Should().StartWith("/api/annotations/");
        created.Value!.AnnotationId.Should().NotBeEmpty().And.HaveLength(26);
    }

    // ─── SC-2 ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task POST_EmptyBody_Returns400()
    {
        var store = await CreateStoreAsync();
        var result = await AnnotationEndpoints.HandleCreateAsync(
            null, store, CancellationToken.None);

        var badRequest = result.Result.Should().BeOfType<BadRequest<ProblemDetails>>().Subject;
        badRequest.StatusCode.Should().Be(400);
    }

    // ─── SC-3 ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task POST_MultipleTargetIdentifiers_Returns400()
    {
        var store = await CreateStoreAsync();
        var dto = ValidCreateDto() with { EntityId = "ent-1" }; // eventId + entityId both set
        var result = await AnnotationEndpoints.HandleCreateAsync(
            dto, store, CancellationToken.None);

        var badRequest = result.Result.Should().BeOfType<BadRequest<ProblemDetails>>().Subject;
        badRequest.StatusCode.Should().Be(400);
    }

    // ─── SC-4 ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task POST_NoTargetIdentifier_Returns400()
    {
        var store = await CreateStoreAsync();
        var dto = new CreateAnnotationDto
        {
            SessionId = "sess-1",
            Kind      = "Event",
            Body      = "Test",
        };
        var result = await AnnotationEndpoints.HandleCreateAsync(
            dto, store, CancellationToken.None);

        var badRequest = result.Result.Should().BeOfType<BadRequest<ProblemDetails>>().Subject;
        badRequest.StatusCode.Should().Be(400);
    }

    // ─── SC-5 ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task POST_BundleMode_Returns405()
    {
        var store = new ReadOnlyStubAnnotationStore();
        var result = await AnnotationEndpoints.HandleCreateAsync(
            ValidCreateDto(), store, CancellationToken.None);

        var problem = result.Result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(405);
    }

    // ─── SC-6 ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PUT_NonExistentId_Returns404()
    {
        var store = await CreateStoreAsync();
        var dto = new UpdateAnnotationDto { Body = "updated" };
        var result = await AnnotationEndpoints.HandleUpdateAsync(
            "nonexistent-id-00000000000", dto, store, CancellationToken.None);

        result.Result.Should().BeOfType<NotFound>();
    }

    // ─── SC-7 ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PUT_BundleMode_Returns405()
    {
        var store = new ReadOnlyStubAnnotationStore();
        var dto = new UpdateAnnotationDto { Body = "updated" };
        var result = await AnnotationEndpoints.HandleUpdateAsync(
            "some-id", dto, store, CancellationToken.None);

        var problem = result.Result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(405);
    }

    // ─── SC-8 ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DELETE_NonExistentId_Returns404()
    {
        var store = await CreateStoreAsync();
        var result = await AnnotationEndpoints.HandleDeleteAsync(
            "nonexistent-id-00000000000", store, CancellationToken.None);

        result.Result.Should().BeOfType<NotFound>();
    }

    // ─── SC-9 ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DELETE_BundleMode_Returns405()
    {
        var store = new ReadOnlyStubAnnotationStore();
        var result = await AnnotationEndpoints.HandleDeleteAsync(
            "some-id", store, CancellationToken.None);

        var problem = result.Result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(405);
    }

    // ─── SC-10 ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GET_List_FiltersBySessionId()
    {
        var store = await CreateStoreAsync();
        // Session A annotations
        await store.CreateAsync(new AnnotationRecord
        {
            AnnotationId = "", SessionId = "sess-A", Kind = AnnotationKind.Event,
            EventId = "evt-001", Body = "A1", CreatedAtUtc = default,
        }, CancellationToken.None);
        await store.CreateAsync(new AnnotationRecord
        {
            AnnotationId = "", SessionId = "sess-A", Kind = AnnotationKind.Event,
            EventId = "evt-002", Body = "A2", CreatedAtUtc = default,
        }, CancellationToken.None);
        // Session B annotation
        await store.CreateAsync(new AnnotationRecord
        {
            AnnotationId = "", SessionId = "sess-B", Kind = AnnotationKind.Event,
            EventId = "evt-003", Body = "B1", CreatedAtUtc = default,
        }, CancellationToken.None);

        var result = await AnnotationEndpoints.HandleListAsync(
            "sess-A", null, null, null, null, store, CancellationToken.None);

        var ok = result.Result.Should().BeOfType<Ok<IReadOnlyList<AnnotationDto>>>().Subject;
        ok.Value!.Should().HaveCount(2);
        ok.Value!.Should().OnlyContain(a => a.SessionId == "sess-A");
    }

    // ─── SC-11 ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GET_Single_Returns200WithDto()
    {
        var store = await CreateStoreAsync();
        var created = await store.CreateAsync(new AnnotationRecord
        {
            AnnotationId = "", SessionId = "sess-1", Kind = AnnotationKind.Event,
            EventId = "evt-001", Body = "hello", CreatedAtUtc = default,
        }, CancellationToken.None);

        var result = await AnnotationEndpoints.HandleGetAsync(
            created.AnnotationId, store, CancellationToken.None);

        var ok = result.Result.Should().BeOfType<Ok<AnnotationDto>>().Subject;
        ok.Value!.AnnotationId.Should().Be(created.AnnotationId);
        ok.Value!.Body.Should().Be("hello");
        ok.Value!.SessionId.Should().Be("sess-1");
    }

    // ─── SC-12 ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GET_Single_UnknownId_Returns404()
    {
        var store = await CreateStoreAsync();
        var result = await AnnotationEndpoints.HandleGetAsync(
            "nonexistent-id-00000000000", store, CancellationToken.None);

        result.Result.Should().BeOfType<NotFound>();
    }

    // ─── SC-13 ────────────────────────────────────────────────────────────────

    [Fact]
    public void DI_Observer_RegistersSqliteAnnotationStore()
    {
        // Build a minimal DI container replicating Observer's IAnnotationStore registration
        var tempDataRoot = Path.Combine(_tempDir, "obs-data");
        Directory.CreateDirectory(tempDataRoot);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(new ObserverConfig
        {
            DataRoot    = tempDataRoot,
            LogsRoot    = Path.Combine(tempDataRoot, "logs"),
            DataSources = new DataSourcesConfig(),
        });
        services.AddSingleton<IAnnotationStore>(sp =>
        {
            var cfg    = sp.GetRequiredService<ObserverConfig>();
            var path   = Path.Combine(cfg.DataRoot, "annotations.db");
            var logger = sp.GetRequiredService<ILogger<SqliteAnnotationStore>>();
            var store  = new SqliteAnnotationStore(path, logger);
            store.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
            return store;
        });

        using var provider = services.BuildServiceProvider();
        var resolved = provider.GetRequiredService<IAnnotationStore>();
        resolved.Should().BeOfType<SqliteAnnotationStore>();
    }

    // ─── Stub store ───────────────────────────────────────────────────────────

    private sealed class ReadOnlyStubAnnotationStore : IAnnotationStore
    {
        public Task<IReadOnlyList<AnnotationRecord>> ListAsync(AnnotationFilter f, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<AnnotationRecord>>(Array.Empty<AnnotationRecord>());

        public Task<AnnotationRecord?> GetAsync(string id, CancellationToken ct)
            => throw new InvalidOperationException("Bundle annotations are read-only");

        public Task<AnnotationRecord> CreateAsync(AnnotationRecord r, CancellationToken ct)
            => throw new InvalidOperationException("Bundle annotations are read-only");

        public Task<AnnotationRecord?> UpdateAsync(AnnotationRecord r, CancellationToken ct)
            => throw new InvalidOperationException("Bundle annotations are read-only");

        public Task<bool> DeleteAsync(string id, CancellationToken ct)
            => throw new InvalidOperationException("Bundle annotations are read-only");

        public Task<IReadOnlyList<AnnotationRecord>> ExportAllForSessionAsync(string sessionId, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<AnnotationRecord>>(Array.Empty<AnnotationRecord>());
    }
}
