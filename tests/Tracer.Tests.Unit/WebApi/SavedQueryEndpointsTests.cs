using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Tracer.Storage.SavedQueries;
using Tracer.Storage.SavedQueries.BuiltIn;
using Tracer.WebApi.Contracts.Dto;
using Tracer.WebApi.Endpoints;
using Xunit;

namespace Tracer.Tests.Unit.WebApi;

public sealed class SavedQueryEndpointsTests
{
    private static SqliteSavedQueryStore CreateStore()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sqep-{Guid.NewGuid():N}.db");
        return new SqliteSavedQueryStore(path, NullLogger<SqliteSavedQueryStore>.Instance);
    }

    [Fact]
    public async Task List_Empty_ReturnsOkEmpty()
    {
        using var store = CreateStore();
        var result = await SavedQueriesEndpoints.HandleListAsync(
            null, null, null, null, store, default);
        var ok = Assert.IsType<Ok<IReadOnlyList<SavedQueryDto>>>(result);
        Assert.Empty(ok.Value!);
    }

    [Fact]
    public async Task Get_NonExistent_ReturnsNotFound()
    {
        using var store = CreateStore();
        var result = await SavedQueriesEndpoints.HandleGetAsync("no-id", store, default);
        Assert.IsType<NotFound>(result.Result);
    }

    [Fact]
    public async Task Create_Valid_ReturnsCreated()
    {
        using var store = CreateStore();
        var dto = new CreateSavedQueryDto { Label = "Test", Sql = "SELECT 1" };
        var result = await SavedQueriesEndpoints.HandleCreateAsync(dto, store, default);
        var created = Assert.IsType<Created<SavedQueryDto>>(result.Result);
        Assert.Equal("Test", created.Value!.Label);
    }

    [Fact]
    public async Task Create_MissingLabel_ReturnsBadRequest()
    {
        using var store = CreateStore();
        var dto = new CreateSavedQueryDto { Label = "  ", Sql = "SELECT 1" };
        var result = await SavedQueriesEndpoints.HandleCreateAsync(dto, store, default);
        Assert.IsType<BadRequest<ProblemDetails>>(result.Result);
    }

    [Fact]
    public async Task Update_NonExistent_ReturnsNotFound()
    {
        using var store = CreateStore();
        var dto = new UpdateSavedQueryDto { Label = "X" };
        var result = await SavedQueriesEndpoints.HandleUpdateAsync("no-id", dto, store, default);
        Assert.IsType<NotFound>(result.Result);
    }

    [Fact]
    public async Task Delete_NonExistent_ReturnsNotFound()
    {
        using var store = CreateStore();
        var result = await SavedQueriesEndpoints.HandleDeleteAsync("no-id", store, default);
        Assert.IsType<NotFound>(result.Result);
    }

    [Fact]
    public async Task Delete_UserQuery_ReturnsNoContent()
    {
        using var store = CreateStore();
        var dto = new CreateSavedQueryDto { Label = "Deletable", Sql = "SELECT 1" };
        var created = await SavedQueriesEndpoints.HandleCreateAsync(dto, store, default);
        var id = ((Created<SavedQueryDto>)created.Result!).Value!.SavedQueryId;
        var result = await SavedQueriesEndpoints.HandleDeleteAsync(id, store, default);
        Assert.IsType<NoContent>(result.Result);
    }

    [Fact]
    public async Task Favorite_BuiltIn_Toggles()
    {
        using var store = CreateStore();
        await BuiltInLoader.EnsureLoadedAsync(store, default);
        var all = await store.ListAsync(new SavedQueryFilter { IsBuiltIn = true }, default);
        var id = all.First().SavedQueryId;
        var result = await SavedQueriesEndpoints.HandleFavoriteAsync(id, store, default);
        var ok = Assert.IsType<Ok<SavedQueryDto>>(result.Result);
        Assert.True(ok.Value!.IsFavorite);
    }

    [Fact]
    public async Task Clone_CopiesRecord()
    {
        using var store = CreateStore();
        var dto = new CreateSavedQueryDto { Label = "Original", Sql = "SELECT 42" };
        var created = await SavedQueriesEndpoints.HandleCreateAsync(dto, store, default);
        var id = ((Created<SavedQueryDto>)created.Result!).Value!.SavedQueryId;

        var cloneDto = new CloneSavedQueryDto { Author = "clone-author" };
        var clone = await SavedQueriesEndpoints.HandleCloneAsync(id, cloneDto, store, default);
        var cloneCreated = Assert.IsType<Created<SavedQueryDto>>(clone.Result);
        Assert.NotEqual(id, cloneCreated.Value!.SavedQueryId);
        Assert.False(cloneCreated.Value!.IsBuiltIn);
    }

    [Fact]
    public async Task Run_IncrementsThenNoContent()
    {
        using var store = CreateStore();
        var dto = new CreateSavedQueryDto { Label = "Runnable", Sql = "SELECT 1" };
        var created = await SavedQueriesEndpoints.HandleCreateAsync(dto, store, default);
        var id = ((Created<SavedQueryDto>)created.Result!).Value!.SavedQueryId;

        var result = await SavedQueriesEndpoints.HandleRunAsync(id, store, default);
        Assert.IsType<NoContent>(result.Result);
        var fetched = await store.GetAsync(id, default);
        Assert.Equal(1, fetched!.RunCount);
    }

    [Fact]
    public async Task Delete_BuiltIn_ReturnsProblem()
    {
        using var store = CreateStore();
        await BuiltInLoader.EnsureLoadedAsync(store, default);
        var all = await store.ListAsync(new SavedQueryFilter { IsBuiltIn = true }, default);
        var id = all.First().SavedQueryId;
        var result = await SavedQueriesEndpoints.HandleDeleteAsync(id, store, default);
        Assert.IsType<ProblemHttpResult>(result.Result);
    }

    [Fact]
    public async Task List_FilterByBuiltIn_ReturnsOnlyBuiltIns()
    {
        using var store = CreateStore();
        await BuiltInLoader.EnsureLoadedAsync(store, default);
        var dto = new CreateSavedQueryDto { Label = "Custom", Sql = "SELECT 1" };
        await SavedQueriesEndpoints.HandleCreateAsync(dto, store, default);

        var result = await SavedQueriesEndpoints.HandleListAsync(
            null, null, null, builtIn: true, store, default);
        var ok = Assert.IsType<Ok<IReadOnlyList<SavedQueryDto>>>(result);
        Assert.All(ok.Value!, q => Assert.True(q.IsBuiltIn));
    }
}
