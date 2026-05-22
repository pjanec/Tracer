using System.Text.RegularExpressions;
using Tracer.WebApi.Queries;
using Xunit;

namespace Tracer.Tests.Unit.WebApi;

public sealed class SqlGuardrailsTests
{
    private static SqlGuardrailsResult Check(string sql) => SqlGuardrails.Validate(sql);

    // ── allow-list ────────────────────────────────────────────────────────────

    [Fact] public void Select_IsAllowed() =>
        Assert.True(Check("SELECT * FROM events").IsValid);

    [Fact] public void With_IsAllowed() =>
        Assert.True(Check("WITH cte AS (SELECT 1) SELECT * FROM cte").IsValid);

    [Fact] public void Explain_IsAllowed() =>
        Assert.True(Check("EXPLAIN SELECT 1").IsValid);

    [Fact] public void Describe_IsAllowed() =>
        Assert.True(Check("DESCRIBE events").IsValid);

    [Fact] public void Show_IsAllowed() =>
        Assert.True(Check("SHOW TABLES").IsValid);

    [Fact] public void Values_IsAllowed() =>
        Assert.True(Check("VALUES (1, 2)").IsValid);

    // ── mutations ─────────────────────────────────────────────────────────────

    [Fact] public void Insert_IsForbidden() =>
        Assert.False(Check("INSERT INTO t VALUES (1)").IsValid);

    [Fact] public void Update_IsForbidden() =>
        Assert.False(Check("UPDATE t SET x=1").IsValid);

    [Fact] public void Delete_IsForbidden() =>
        Assert.False(Check("DELETE FROM t").IsValid);

    [Fact] public void Drop_IsForbidden() =>
        Assert.False(Check("DROP TABLE t").IsValid);

    [Fact] public void Create_IsForbidden() =>
        Assert.False(Check("CREATE TABLE t(id INT)").IsValid);

    [Fact] public void Alter_IsForbidden() =>
        Assert.False(Check("ALTER TABLE t ADD COLUMN x INT").IsValid);

    // ── multi-statement ───────────────────────────────────────────────────────

    [Fact] public void MultiStatement_IsForbidden() =>
        Assert.False(Check("SELECT 1; DROP TABLE t").IsValid);

    [Fact] public void MultiStatement_BothSelect_IsForbidden() =>
        Assert.False(Check("SELECT 1; SELECT 2").IsValid);

    // ── comment stripping should not affect result ────────────────────────────

    [Fact] public void CommentBeforeSelect_IsAllowed()
    {
        const string sql = "-- look at me\nSELECT 1";
        Assert.True(Check(sql).IsValid);
    }

    // ── double-quoted identifier should not be treated as keyword ─────────────

    [Fact] public void DoubleQuotedUpdate_IsAllowed() =>
        Assert.True(Check("""SELECT "update" FROM t""").IsValid);

    // ── forbidden functions ───────────────────────────────────────────────────

    [Fact] public void ReadCsvAuto_IsForbidden() =>
        Assert.False(Check("SELECT * FROM read_csv_auto('file.csv')").IsValid);

    [Fact] public void ReadParquet_IsForbidden() =>
        Assert.False(Check("SELECT * FROM read_parquet('file.parquet')").IsValid);
}
