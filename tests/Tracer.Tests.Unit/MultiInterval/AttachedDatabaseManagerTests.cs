using System.Text.RegularExpressions;
using DuckDB.NET.Data;
using FluentAssertions;
using Tracer.Storage.DuckDB.MultiInterval;
using Xunit;

namespace Tracer.Tests.Unit.MultiInterval;

public class AttachedDatabaseManagerTests
{
    private static async Task<string> CreateTempDuckDbAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.duckdb");
        await using var conn = new DuckDBConnection($"DataSource={path}");
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "CREATE TABLE events (id INTEGER)";
        await cmd.ExecuteNonQueryAsync();
        return path;
    }

    private static async Task<DuckDBConnection> OpenInMemoryAsync()
    {
        var conn = new DuckDBConnection("DataSource=:memory:");
        await conn.OpenAsync();
        return conn;
    }

    [Fact]
    public async Task AttachAsync_ProducesAliasMatchingPattern()
    {
        var dbPath = await CreateTempDuckDbAsync();
        await using var conn = await OpenInMemoryAsync();
        await using var manager = new AttachedDatabaseManager(conn);

        var alias = await manager.AttachAsync(new IntervalDbFile(dbPath, "node01"));

        alias.Should().MatchRegex(@"^db_[a-z0-9_]+_[0-9a-f]{6}$");
    }

    [Fact]
    public async Task AttachAsync_SameHint_TwiceProducesDistinctAliases()
    {
        var dbPath1 = await CreateTempDuckDbAsync();
        var dbPath2 = await CreateTempDuckDbAsync();
        await using var conn = await OpenInMemoryAsync();
        await using var manager = new AttachedDatabaseManager(conn);

        var alias1 = await manager.AttachAsync(new IntervalDbFile(dbPath1, "same-hint"));
        var alias2 = await manager.AttachAsync(new IntervalDbFile(dbPath2, "same-hint"));

        alias1.Should().NotBe(alias2);
    }

    [Fact]
    public async Task DetachAsync_RemovesAliasFromAttachments()
    {
        var dbPath = await CreateTempDuckDbAsync();
        await using var conn = await OpenInMemoryAsync();
        var manager = new AttachedDatabaseManager(conn);

        var alias = await manager.AttachAsync(new IntervalDbFile(dbPath, "node01"));
        manager.Attachments.Should().ContainKey(alias);

        await manager.DetachAsync(alias);
        manager.Attachments.Should().NotContainKey(alias);

        await manager.DisposeAsync();
    }

    [Fact]
    public async Task DisposeAsync_DetachesAllAttachments()
    {
        var paths = new List<string>();
        await using var conn = await OpenInMemoryAsync();
        var manager = new AttachedDatabaseManager(conn);

        for (var i = 0; i < 3; i++)
        {
            var p = await CreateTempDuckDbAsync();
            paths.Add(p);
            await manager.AttachAsync(new IntervalDbFile(p, $"node{i:00}"));
        }

        manager.Attachments.Should().HaveCount(3);

        await manager.DisposeAsync();

        manager.Attachments.Should().BeEmpty();
    }

    [Fact]
    public async Task AliasGeneration_ProducesValidSqlIdentifier()
    {
        var dbPath = await CreateTempDuckDbAsync();
        await using var conn = await OpenInMemoryAsync();
        await using var manager = new AttachedDatabaseManager(conn);

        // hint contains special characters
        var alias = await manager.AttachAsync(new IntervalDbFile(dbPath, "my-node:01/test"));

        alias.Should().MatchRegex(@"^[a-zA-Z_][a-zA-Z0-9_]*$");
    }
}
