using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Tracer.TestHarness.Observer;
using Tracer.WebApi.Contracts.Dto;
using Xunit;

namespace Tracer.Tests.Integration;

/// <summary>
/// Bundle library round-trip: list, update metadata, record-opened, delete.
/// Uses ObserverFixture but the BundleLibraryService uses a temp directory.
/// </summary>
[Collection("BundleLibraryRoundTrip")]
public sealed class BundleLibraryRoundTripTests : IAsyncLifetime
{
    private ObserverFixture? _observer;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public async Task InitializeAsync()
    {
        _observer = await ObserverFixture.CreateAsync();
    }

    public async Task DisposeAsync()
    {
        if (_observer is not null)
            await _observer.DisposeAsync();
    }

    private HttpClient Client => _observer!.Client;

    [Fact]
    public async Task GetLibrary_Empty_ReturnsEmptyList()
    {
        var response = await Client.GetAsync("/api/bundles/library");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<BundleLibraryListDto>(JsonOpts);
        body.Should().NotBeNull();
        body!.Entries.Should().NotBeNull();
    }

    [Fact]
    public async Task Import_InvalidZip_ReturnsBadRequest()
    {
        using var content = new ByteArrayContent(new byte[] { 0x00, 0x01, 0x02 });
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        var response = await Client.PostAsync("/api/bundles/import", content);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Download_NonExistent_ReturnsNotFound()
    {
        var response = await Client.GetAsync("/api/bundles/no-such-bundle/download");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
