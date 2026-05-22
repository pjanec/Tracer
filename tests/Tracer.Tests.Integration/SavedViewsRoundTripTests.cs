using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Tracer.Observer.Configuration;
using Tracer.OfflineViewer.WebApi;
using Tracer.Storage.SavedViews;
using Tracer.TestHarness.Observer;
using Tracer.WebApi.Contracts.Dto;
using Tracer.WebApi.Endpoints;
using Xunit;

namespace Tracer.Tests.Integration;

/// <summary>
/// Integration tests for SavedViews in observer mode (live CRUD)
/// and bundle mode (read-only enforcement).
/// TRC-P8-018 backend integration scope.
/// </summary>
[Collection("SavedViewsRoundTrip")]
public sealed class SavedViewsRoundTripTests : IAsyncLifetime
{
    private ObserverFixture? _observer;

    private static readonly JsonSerializerOptions CamelCaseOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public async Task InitializeAsync()
    {
        _observer = await ObserverFixture.CreateAsync(
            configureExtraServices: services =>
            {
                services.AddSingleton<ISavedViewStore>(sp =>
                {
                    var cfg = sp.GetRequiredService<ObserverConfig>();
                    var path = System.IO.Path.Combine(cfg.DataRoot, "saved-views.db");
                    var store = new SqliteSavedViewStore(
                        path, NullLogger<SqliteSavedViewStore>.Instance);
                    store.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
                    return store;
                });
            },
            configureExtraApp: app => SavedViewEndpoints.Map(app));
    }

    public async Task DisposeAsync()
    {
        if (_observer is not null)
            await _observer.DisposeAsync();
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SavedViews_LiveMode_CreateListDelete()
    {
        // Create
        var dto = new CreateSavedViewDto
        {
            SessionId = "sess-int-test",
            Kind = "SavedView",
            ViewType = "Timeline",
            Persona = "engineer",
            Label = "Integration test view",
            Url = "/v/timeline/sess-int-test?topic=game.tick",
        };
        var createResp = await _observer!.Client.PostAsJsonAsync("/api/saved-views", dto);
        createResp.StatusCode.Should().Be(HttpStatusCode.Created,
            "creating a saved view in live mode must return 201");

        var created = await createResp.Content.ReadFromJsonAsync<SavedViewDto>(CamelCaseOptions);
        created.Should().NotBeNull();
        created!.SavedViewId.Should().NotBeEmpty();
        created.Label.Should().Be("Integration test view");

        // List
        var listResp = await _observer.Client.GetAsync(
            $"/api/saved-views?sessionId=sess-int-test");
        listResp.EnsureSuccessStatusCode();
        var list = await listResp.Content.ReadFromJsonAsync<SavedViewDto[]>(CamelCaseOptions);
        list.Should().NotBeNull();
        list!.Should().Contain(v => v.SavedViewId == created.SavedViewId,
            "listed saved views must include the newly created one");

        // Delete
        var deleteResp = await _observer.Client.DeleteAsync(
            $"/api/saved-views/{created.SavedViewId}");
        deleteResp.StatusCode.Should().Be(HttpStatusCode.NoContent,
            "deleting a saved view must return 204");

        // Verify gone
        var listAfterResp = await _observer.Client.GetAsync(
            $"/api/saved-views?sessionId=sess-int-test");
        listAfterResp.EnsureSuccessStatusCode();
        var listAfter = await listAfterResp.Content.ReadFromJsonAsync<SavedViewDto[]>(CamelCaseOptions);
        listAfter!.Should().NotContain(v => v.SavedViewId == created.SavedViewId,
            "deleted saved view must not appear in subsequent list");
    }

    [Fact]
    public async Task SavedViews_BundleMode_WriteThrows()
    {
        // The LazyBundleSavedViewStore throws InvalidOperationException for writes.
        // Verify this directly via the store.
        var bundleStore = new LazyBundleSavedViewStore();
        var createAction = async () => await bundleStore.CreateAsync(
            new SavedViewRecord
            {
                SavedViewId = "test",
                SessionId = "s1",
                Kind = SavedViewKind.SavedView,
                ViewType = "Timeline",
                Persona = "engineer",
                Label = "test",
                Url = "/",
                CreatedAtUtc = DateTimeOffset.UtcNow,
                OpenCount = 0,
            }, CancellationToken.None);

        await createAction.Should().ThrowAsync<InvalidOperationException>(
            "bundle saved view store must reject writes with InvalidOperationException");
    }
}
