using Microsoft.Extensions.Logging.Abstractions;
using Tracer.Storage.SavedQueries;
using Xunit;

namespace Tracer.Tests.Unit.SavedQueries;

public sealed class SavedQueriesRoundTripTests
{
    private static SqliteSavedQueryStore CreateStore()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sq-rt-{Guid.NewGuid():N}.db");
        return new SqliteSavedQueryStore(path, NullLogger<SqliteSavedQueryStore>.Instance);
    }

    private static SavedQueryRecord Sample(string label = "My Query") => new()
    {
        SavedQueryId = "",
        Label        = label,
        Sql          = "SELECT 1",
        Parameters   = [],
        Tags         = ["tag-a"],
        IsBuiltIn    = false,
        IsFavorite   = false,
        Author       = "tester",
        CreatedAtUtc = DateTimeOffset.UtcNow,
        RunCount     = 0,
    };

    [Fact]
    public async Task Create_ThenGet_ReturnsRecord()
    {
        using var store = CreateStore();
        var created = await store.CreateAsync(Sample(), default);
        var fetched = await store.GetAsync(created.SavedQueryId, default);
        Assert.NotNull(fetched);
        Assert.Equal(created.SavedQueryId, fetched!.SavedQueryId);
        Assert.Equal("My Query", fetched.Label);
    }

    [Fact]
    public async Task Create_AssignsUniqueId()
    {
        using var store = CreateStore();
        var a = await store.CreateAsync(Sample(), default);
        var b = await store.CreateAsync(Sample("Other"), default);
        Assert.NotEqual(a.SavedQueryId, b.SavedQueryId);
    }

    [Fact]
    public async Task List_EmptyFilter_ReturnsAll()
    {
        using var store = CreateStore();
        await store.CreateAsync(Sample("A"), default);
        await store.CreateAsync(Sample("B"), default);
        var all = await store.ListAsync(new SavedQueryFilter(), default);
        Assert.Equal(2, all.Count);
    }

    [Fact]
    public async Task List_FilterByFavorite_OnlyReturnsMatch()
    {
        using var store = CreateStore();
        var a = await store.CreateAsync(Sample("A"), default);
        await store.CreateAsync(Sample("B"), default);
        await store.ToggleFavoriteAsync(a.SavedQueryId, default);

        var favs = await store.ListAsync(new SavedQueryFilter { IsFavorite = true }, default);
        Assert.Single(favs);
        Assert.Equal("A", favs[0].Label);
    }

    [Fact]
    public async Task List_FilterByTag_OnlyReturnsMatch()
    {
        using var store = CreateStore();
        var r = Sample("Tagged");
        var r2 = r with { Label = "Untagged", Tags = [] };
        await store.CreateAsync(r, default);
        await store.CreateAsync(r2, default);

        var tagged = await store.ListAsync(new SavedQueryFilter { Tag = "tag-a" }, default);
        Assert.Single(tagged);
    }

    [Fact]
    public async Task Update_ChangesLabel()
    {
        using var store = CreateStore();
        var created = await store.CreateAsync(Sample(), default);
        var updated = await store.UpdateAsync(created with { Label = "Updated" }, default);
        Assert.NotNull(updated);
        Assert.Equal("Updated", updated!.Label);
    }

    [Fact]
    public async Task Delete_RemovesRecord()
    {
        using var store = CreateStore();
        var created = await store.CreateAsync(Sample(), default);
        var deleted = await store.DeleteAsync(created.SavedQueryId, default);
        Assert.True(deleted);
        var fetched = await store.GetAsync(created.SavedQueryId, default);
        Assert.Null(fetched);
    }

    [Fact]
    public async Task IncrementRunCount_IncrementsCounter()
    {
        using var store = CreateStore();
        var created = await store.CreateAsync(Sample(), default);
        await store.IncrementRunCountAsync(created.SavedQueryId, default);
        await store.IncrementRunCountAsync(created.SavedQueryId, default);
        var fetched = await store.GetAsync(created.SavedQueryId, default);
        Assert.Equal(2, fetched!.RunCount);
    }

    [Fact]
    public async Task Get_NonExistent_ReturnsNull()
    {
        using var store = CreateStore();
        var result = await store.GetAsync("does-not-exist", default);
        Assert.Null(result);
    }

    [Fact]
    public async Task ToggleFavorite_TogglesBack()
    {
        using var store = CreateStore();
        var created = await store.CreateAsync(Sample(), default);
        Assert.False(created.IsFavorite);

        var on = await store.ToggleFavoriteAsync(created.SavedQueryId, default);
        Assert.True(on!.IsFavorite);

        var off = await store.ToggleFavoriteAsync(created.SavedQueryId, default);
        Assert.False(off!.IsFavorite);
    }

    [Fact]
    public async Task ToggleFavorite_NonExistent_ReturnsNull()
    {
        using var store = CreateStore();
        var result = await store.ToggleFavoriteAsync("nope", default);
        Assert.Null(result);
    }
}
