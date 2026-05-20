using DuckDB.NET.Data;
using FluentAssertions;
using Tracer.Storage.DuckDB.MultiInterval;
using Xunit;

namespace Tracer.Tests.Unit.MultiInterval;

public class MultiIntervalReaderTests
{
    private static async Task<string> CreateTempDuckDbAsync(bool withEventRow = false)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.duckdb");
        await using var conn = new DuckDBConnection($"DataSource={path}");
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "CREATE TABLE events (id INTEGER)";
        await cmd.ExecuteNonQueryAsync();
        if (withEventRow)
        {
            cmd.CommandText = "INSERT INTO events VALUES (1)";
            await cmd.ExecuteNonQueryAsync();
        }
        return path;
    }

    [Fact]
    public async Task CreateWithZeroFiles_BuildEventsUnionSql_ReturnsEmptySentinel()
    {
        await using var reader = await MultiIntervalReader.CreateAsync(Enumerable.Empty<IntervalDbFile>());

        reader.BuildEventsUnionSql().Should().Be("SELECT NULL WHERE FALSE");
    }

    [Fact]
    public async Task CreateWithOneFile_SqlReferencesAlias()
    {
        var dbPath = await CreateTempDuckDbAsync();
        await using var reader = await MultiIntervalReader.CreateAsync(
            [new IntervalDbFile(dbPath, "node01")]);

        var sql = reader.BuildEventsUnionSql();
        var alias = reader.Attachments.Keys.Single();
        sql.Should().Contain(alias);
    }

    [Fact]
    public async Task CreateWithTwoFiles_SqlContainsOneUnionAll()
    {
        var path1 = await CreateTempDuckDbAsync();
        var path2 = await CreateTempDuckDbAsync();
        await using var reader = await MultiIntervalReader.CreateAsync(
        [
            new IntervalDbFile(path1, "node01"),
            new IntervalDbFile(path2, "node02"),
        ]);

        var sql = reader.BuildEventsUnionSql();
        var count = CountOccurrences(sql, "UNION ALL");
        count.Should().Be(1);
    }

    [Fact]
    public async Task SourceAliasColumn_PresentInResults()
    {
        var path = await CreateTempDuckDbAsync(withEventRow: true);
        await using var reader = await MultiIntervalReader.CreateAsync(
            [new IntervalDbFile(path, "nodeA")]);

        var sql = reader.BuildEventsUnionSql();
        await using var cmd = reader.Connection.CreateCommand();
        cmd.CommandText = sql;
        await using var result = await cmd.ExecuteReaderAsync();

        // Find the __source_alias column
        var columnNames = Enumerable.Range(0, result.FieldCount)
            .Select(i => result.GetName(i))
            .ToList();

        columnNames.Should().Contain("__source_alias");
    }

    [Fact]
    public async Task DisposeAsync_CompletesWithoutThrowing()
    {
        var path1 = await CreateTempDuckDbAsync();
        var path2 = await CreateTempDuckDbAsync();

        var reader = await MultiIntervalReader.CreateAsync(
        [
            new IntervalDbFile(path1, "n1"),
            new IntervalDbFile(path2, "n2"),
        ]);

        var act = async () => await reader.DisposeAsync();
        await act.Should().NotThrowAsync();

        // Second dispose should also not throw
        var act2 = async () => await reader.DisposeAsync();
        await act2.Should().NotThrowAsync();
    }

    private static int CountOccurrences(string source, string pattern)
    {
        var count = 0;
        var index = 0;
        while ((index = source.IndexOf(pattern, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += pattern.Length;
        }
        return count;
    }
}
