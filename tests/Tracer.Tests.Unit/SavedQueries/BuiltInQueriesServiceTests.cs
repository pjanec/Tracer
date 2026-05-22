using Microsoft.Extensions.Logging.Abstractions;
using Tracer.Storage.SavedQueries;
using Tracer.Storage.SavedQueries.BuiltIn;
using Xunit;

namespace Tracer.Tests.Unit.SavedQueries;

public sealed class BuiltInQueriesServiceTests
{
    private static SqliteSavedQueryStore CreateStore()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sq-test-{Guid.NewGuid():N}.db");
        return new SqliteSavedQueryStore(path, NullLogger<SqliteSavedQueryStore>.Instance);
    }

    [Fact]
    public async Task EnsureLoadedAsync_PopulatesBuiltIns()
    {
        using var store = CreateStore();
        await BuiltInLoader.EnsureLoadedAsync(store, default);
        var all = await store.ListAsync(new SavedQueryFilter { IsBuiltIn = true }, default);
        Assert.NotEmpty(all);
    }

    [Fact]
    public async Task EnsureLoadedAsync_IsIdempotent()
    {
        using var store = CreateStore();
        await BuiltInLoader.EnsureLoadedAsync(store, default);
        await BuiltInLoader.EnsureLoadedAsync(store, default);
        var all = await store.ListAsync(new SavedQueryFilter { IsBuiltIn = true }, default);
        // Each built-in id should appear exactly once
        var ids = all.Select(r => r.SavedQueryId).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public async Task EnsureLoadedAsync_AllBuiltInsHaveLabels()
    {
        using var store = CreateStore();
        await BuiltInLoader.EnsureLoadedAsync(store, default);
        var all = await store.ListAsync(new SavedQueryFilter { IsBuiltIn = true }, default);
        Assert.All(all, r => Assert.False(string.IsNullOrWhiteSpace(r.Label)));
    }

    [Fact]
    public async Task EnsureLoadedAsync_AllSqlPassesGuardrails()
    {
        using var store = CreateStore();
        await BuiltInLoader.EnsureLoadedAsync(store, default);
        var all = await store.ListAsync(new SavedQueryFilter { IsBuiltIn = true }, default);
        foreach (var r in all)
        {
            var result = Tracer.WebApi.Queries.SqlGuardrails.Validate(r.Sql);
            Assert.True(result.IsValid, $"Built-in query '{r.SavedQueryId}' failed guardrails: {result.RejectionReason}");
        }
    }

    [Fact]
    public async Task EnsureLoadedAsync_LoadsAtLeastFiveBuiltIns()
    {
        using var store = CreateStore();
        await BuiltInLoader.EnsureLoadedAsync(store, default);
        var all = await store.ListAsync(new SavedQueryFilter { IsBuiltIn = true }, default);
        Assert.True(all.Count >= 5, $"Expected at least 5 built-in queries, got {all.Count}");
    }

    [Fact]
    public async Task BuiltInQueries_HaveNonEmptySql()
    {
        using var store = CreateStore();
        await BuiltInLoader.EnsureLoadedAsync(store, default);
        var all = await store.ListAsync(new SavedQueryFilter { IsBuiltIn = true }, default);
        Assert.All(all, r => Assert.False(string.IsNullOrWhiteSpace(r.Sql)));
    }

    [Fact]
    public async Task BuiltInQueries_CannotBeDeleted()
    {
        using var store = CreateStore();
        await BuiltInLoader.EnsureLoadedAsync(store, default);
        var all = await store.ListAsync(new SavedQueryFilter { IsBuiltIn = true }, default);
        var first = all.First();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.DeleteAsync(first.SavedQueryId, default));
    }

    [Fact]
    public async Task BuiltInQueries_CanToggleFavorite()
    {
        using var store = CreateStore();
        await BuiltInLoader.EnsureLoadedAsync(store, default);
        var all = await store.ListAsync(new SavedQueryFilter { IsBuiltIn = true }, default);
        var first = all.First();
        Assert.False(first.IsFavorite);

        var toggled = await store.ToggleFavoriteAsync(first.SavedQueryId, default);
        Assert.NotNull(toggled);
        Assert.True(toggled!.IsFavorite);

        var toggledBack = await store.ToggleFavoriteAsync(first.SavedQueryId, default);
        Assert.False(toggledBack!.IsFavorite);
    }
}
