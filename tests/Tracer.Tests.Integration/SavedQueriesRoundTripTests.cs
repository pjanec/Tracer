using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Tracer.Storage.SavedQueries;
using Tracer.Storage.SavedQueries.BuiltIn;
using Tracer.TestHarness.Observer;
using Tracer.WebApi.Contracts.Dto;
using Xunit;

namespace Tracer.Tests.Integration;

/// <summary>
/// Integration tests for the Saved Queries endpoints via an in-process ObserverFixture.
/// </summary>
[Collection("SavedQueriesRoundTrip")]
public sealed class SavedQueriesRoundTripTests : IAsyncLifetime
{
    private ObserverFixture? _observer;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public async Task InitializeAsync()
    {
        _observer = await ObserverFixture.CreateAsync();

        // Seed built-in queries
        var store = _observer.App.Services.GetRequiredService<ISavedQueryStore>();
        await BuiltInLoader.EnsureLoadedAsync(store, default);
    }

    public async Task DisposeAsync()
    {
        if (_observer is not null)
            await _observer.DisposeAsync();
    }

    private HttpClient Client => _observer!.Client;

    [Fact]
    public async Task GetList_ReturnsBuiltIns()
    {
        var response = await Client.GetAsync("/api/saved-queries");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dtos = await response.Content.ReadFromJsonAsync<List<SavedQueryDto>>(JsonOpts);
        dtos.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Create_ThenGet_RoundTrip()
    {
        var create = new { label = "Integration Test", sql = "SELECT 1" };
        var postResp = await Client.PostAsJsonAsync("/api/saved-queries", create);
        postResp.StatusCode.Should().Be(HttpStatusCode.Created);

        var created = await postResp.Content.ReadFromJsonAsync<SavedQueryDto>(JsonOpts);
        created.Should().NotBeNull();

        var getResp = await Client.GetAsync($"/api/saved-queries/{created!.SavedQueryId}");
        getResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var fetched = await getResp.Content.ReadFromJsonAsync<SavedQueryDto>(JsonOpts);
        fetched!.Label.Should().Be("Integration Test");
    }

    [Fact]
    public async Task ToggleFavorite_OnBuiltIn_Works()
    {
        var listResp = await Client.GetAsync("/api/saved-queries?builtIn=true");
        listResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var list = await listResp.Content.ReadFromJsonAsync<List<SavedQueryDto>>(JsonOpts);
        var first = list!.First();

        var togResp = await Client.PostAsync($"/api/saved-queries/{first.SavedQueryId}/favorite", null);
        togResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var toggled = await togResp.Content.ReadFromJsonAsync<SavedQueryDto>(JsonOpts);
        toggled!.IsFavorite.Should().BeTrue();
    }
}
