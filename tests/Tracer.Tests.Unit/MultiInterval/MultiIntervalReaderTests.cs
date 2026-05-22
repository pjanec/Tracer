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

    // ── TRC-P4-012: Additional required test methods ────────────────────────

    [Fact]
    public async Task CreateWithNFiles_AllAliasesPresent()
    {
        // Must use N ≥ 3 files per spec
        const int n = 3;
        var paths = new string[n];
        for (var i = 0; i < n; i++)
            paths[i] = await CreateTempDuckDbAsync();

        var files = paths.Select((p, i) => new IntervalDbFile(p, $"node{i:D2}"));
        await using var reader = await MultiIntervalReader.CreateAsync(files);

        reader.Attachments.Should().HaveCount(n,
            $"all {n} files should be attached");

        var sql = reader.BuildEventsUnionSql();
        foreach (var alias in reader.Attachments.Keys)
            sql.Should().Contain(alias,
                $"alias '{alias}' should appear in the generated UNION ALL SQL");
    }

    [Fact]
    public async Task Dispose_DetachesAllDatabases()
    {
        var path1 = await CreateTempDuckDbAsync();
        var path2 = await CreateTempDuckDbAsync();

        // Do NOT use await-using here — we need to inspect Attachments after disposal
        var reader = await MultiIntervalReader.CreateAsync(
        [
            new IntervalDbFile(path1, "n1"),
            new IntervalDbFile(path2, "n2"),
        ]);

        reader.Attachments.Should().HaveCount(2,
            "both databases should be attached before dispose");

        await reader.DisposeAsync();

        // After disposal, the AttachedDatabaseManager must have cleared its dictionary
        reader.Attachments.Should().BeEmpty(
            "DisposeAsync should detach all databases and clear the Attachments dictionary");
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

    // ── TRC-P7-006: BuildSlowStateUnionSql tests ──────────────────────────

    [Fact]
    public async Task BuildSlowStateUnionSql_TwoAttachments_ProducesUnionAll()
    {
        var path1 = await CreateTempDuckDbAsync();
        var path2 = await CreateTempDuckDbAsync();
        await using var reader = await MultiIntervalReader.CreateAsync(
        [
            new IntervalDbFile(path1, "node01"),
            new IntervalDbFile(path2, "node02"),
        ]);

        var sql = reader.BuildSlowStateUnionSql();

        CountOccurrences(sql, "UNION ALL").Should().Be(1,
            "two attachments should produce exactly one UNION ALL");
        CountOccurrences(sql, "slow_state").Should().Be(2,
            "each attachment should contribute one slow_state reference");
    }

    [Fact]
    public async Task BuildSlowStateUnionSql_WhereClause_AppearsInBothArms()
    {
        var path1 = await CreateTempDuckDbAsync();
        var path2 = await CreateTempDuckDbAsync();
        await using var reader = await MultiIntervalReader.CreateAsync(
        [
            new IntervalDbFile(path1, "node01"),
            new IntervalDbFile(path2, "node02"),
        ]);

        var sql = reader.BuildSlowStateUnionSql(whereClause: "WHERE instance_key = 'e1'");

        CountOccurrences(sql, "WHERE instance_key = 'e1'").Should().Be(2,
            "the WHERE clause should appear in both UNION arms");
    }

    [Fact]
    public async Task BuildSlowStateUnionSql_NoAttachments_ReturnsSentinel()
    {
        await using var reader = await MultiIntervalReader.CreateAsync(
            Enumerable.Empty<IntervalDbFile>());

        reader.BuildSlowStateUnionSql().Should().Be("SELECT NULL WHERE FALSE");
    }

    [Fact]
    public async Task BuildSlowStateUnionSql_LimitSet_AppendsLimitClause()
    {
        var path = await CreateTempDuckDbAsync();
        await using var reader = await MultiIntervalReader.CreateAsync(
            [new IntervalDbFile(path, "node01")]);

        var sql = reader.BuildSlowStateUnionSql(limit: 500);

        sql.Should().Contain("LIMIT 500");
    }

    [Fact]
    public async Task BuildSlowStateUnionSql_DoesNotReferenceEventsTable()
    {
        var path1 = await CreateTempDuckDbAsync();
        var path2 = await CreateTempDuckDbAsync();
        await using var reader = await MultiIntervalReader.CreateAsync(
        [
            new IntervalDbFile(path1, "node01"),
            new IntervalDbFile(path2, "node02"),
        ]);

        var sql = reader.BuildSlowStateUnionSql();

        sql.Should().NotContain(".events",
            "slow_state union SQL must not reference the events table");
    }
}
