using DuckDB.NET.Data;
using FluentAssertions;
using Tracer.Core.Domain;
using Tracer.Core.Identity;
using Tracer.Core.Queries;
using Tracer.Core.Time;
using Tracer.Storage.DuckDB.Queries;
using Xunit;

namespace Tracer.Tests.Unit.Storage;

public sealed class QueryBuilderTests
{
    private static WallclockTime T(long ns) => new WallclockTime(ns);

    [Fact]
    public void EventFilter_All_HasNoConstraints()
    {
        var filter = EventFilter.All;

        filter.From.Should().BeNull();
        filter.To.Should().BeNull();
        filter.Topic.Should().BeNull();
        filter.TraceId.Should().BeNull();
        filter.EntityId.Should().BeNull();
        filter.MinSeverity.Should().BeNull();
        filter.PayloadSearch.Should().BeNull();
    }

    [Fact]
    public void Build_NoFilters_ContainsLimitAndOffset()
    {
        var query = new EventQuery
        {
            Filter = EventFilter.All,
            Limit = 500,
            Offset = 100,
        };

        var (sql, parameters) = EventQueryBuilder.Build(query);

        sql.Should().Contain("LIMIT $limit");
        sql.Should().Contain("OFFSET $offset");
        parameters.Should().Contain(p => p.ParameterName == "limit");
        parameters.Should().Contain(p => p.ParameterName == "offset");
        parameters.Single(p => p.ParameterName == "limit").Value.Should().Be(500);
        parameters.Single(p => p.ParameterName == "offset").Value.Should().Be(100);
    }

    [Fact]
    public void Build_TimeRange_AppendsWallclockClauses()
    {
        var query = new EventQuery
        {
            Filter = new EventFilter
            {
                From = T(1_000_000_000L),
                To = T(2_000_000_000L),
            },
            Limit = 100,
            Offset = 0,
        };

        var (sql, parameters) = EventQueryBuilder.Build(query);

        sql.Should().Contain("publish_wallclock >= $from");
        sql.Should().Contain("publish_wallclock < $to");
        parameters.Should().Contain(p => p.ParameterName == "from");
        parameters.Should().Contain(p => p.ParameterName == "to");
    }

    [Fact]
    public void Build_TraceIdFilter_AppendsSingleAndClause()
    {
        var query = new EventQuery
        {
            Filter = new EventFilter { TraceId = new TraceId(99) },
            Limit = 100,
            Offset = 0,
        };

        var (sql, parameters) = EventQueryBuilder.Build(query);

        sql.Should().Contain("trace_id = $trace_id");
        parameters.Should().ContainSingle(p => p.ParameterName == "trace_id");
    }

    [Fact]
    public void Build_MinSeverityWarning_ExpandsToInClause()
    {
        var query = new EventQuery
        {
            Filter = new EventFilter { MinSeverity = Severity.Warning },
            Limit = 100,
            Offset = 0,
        };

        var (sql, parameters) = EventQueryBuilder.Build(query);

        // Warning and above = Warning, Error
        sql.Should().Contain("severity IN (");
        // Two parameters: sev0, sev1 — plus limit and offset from DT-001
        parameters.Should().Contain(p => p.ParameterName == "sev0");
        parameters.Should().Contain(p => p.ParameterName == "sev1");
        parameters.Should().HaveCount(4, "sev0, sev1, limit, offset");

        // Verify the parameter VALUES are correct
        var sev0 = parameters.Single(p => p.ParameterName == "sev0");
        var sev1 = parameters.Single(p => p.ParameterName == "sev1");
        sev0.Value!.ToString().Should().Be("Warning");
        sev1.Value!.ToString().Should().Be("Error");

        // Negative case: Info must NOT be included
        parameters.Should().NotContain(p => p.Value!.ToString() == "Info");
    }

    [Fact]
    public void Build_PayloadSearch_EscapesLikeSpecialChars()
    {
        var query = new EventQuery
        {
            Filter = new EventFilter { PayloadSearch = "100% done_now" },
            Limit = 100,
            Offset = 0,
        };

        var (sql, parameters) = EventQueryBuilder.Build(query);

        sql.Should().Contain("payload LIKE $search");
        var searchParam = parameters.Single(p => p.ParameterName == "search");
        var value = searchParam.Value!.ToString()!;
        value.Should().Contain("\\%");   // % escaped
        value.Should().Contain("\\_");   // _ escaped
    }

    [Fact]
    public void Build_MultipleFilters_CombineWithAnd()
    {
        var query = new EventQuery
        {
            Filter = new EventFilter
            {
                TraceId = new TraceId(1),
                Topic = new TopicName("my.topic"),
                MinSeverity = Severity.Error,
            },
            Limit = 50,
            Offset = 0,
        };

        var (sql, _) = EventQueryBuilder.Build(query);

        // Should have at least two AND clauses beyond the base WHERE 1=1
        var andCount = sql.Split(" AND ", StringSplitOptions.None).Length - 1;
        andCount.Should().BeGreaterThanOrEqualTo(2, "multiple filters should join with AND");
    }

    [Fact]
    public void BuildCount_AnyFilter_ReturnsSELECTCOUNT()
    {
        var filter = new EventFilter { Topic = new TopicName("x") };

        var (sql, _) = EventQueryBuilder.BuildCount(filter);

        sql.Should().StartWith("SELECT COUNT(*) FROM events");
        sql.Should().Contain("topic = $topic");
    }

    [Fact]
    public void Build_SqlInjectionAttempt_IsParameterized()
    {
        // Attacker attempts SQL injection via the PayloadSearch (LIKE) filter — most dangerous path
        var maliciousInput = "'; DROP TABLE events; --";
        var query = new EventQuery
        {
            Filter = new EventFilter { PayloadSearch = maliciousInput },
            Limit = 100,
            Offset = 0,
        };

        var (sql, parameters) = EventQueryBuilder.Build(query);

        // The SQL must NOT contain the raw injection string
        sql.Should().NotContain(maliciousInput);
        sql.Should().Contain("payload LIKE $search");

        // The $search parameter must contain the wrapped value (%...%)
        var searchParam = parameters.Single(p => p.ParameterName == "search");
        searchParam.Value!.ToString().Should().Be($"%{maliciousInput}%",
            "the payload search value should be wrapped in % wildcards for LIKE");
    }
}
