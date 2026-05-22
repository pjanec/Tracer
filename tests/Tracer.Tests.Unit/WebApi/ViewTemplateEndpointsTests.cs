using Microsoft.Extensions.Logging.Abstractions;
using Tracer.WebApi.Queries;
using Xunit;

namespace Tracer.Tests.Unit.WebApi;

public sealed class ViewTemplateEndpointsTests
{
    private static readonly ViewSqlTemplateService Svc = new();

    [Theory]
    [InlineData("timeline")]
    [InlineData("entity-history")]
    [InlineData("causal")]
    [InlineData("latency")]
    [InlineData("gaps")]
    [InlineData("topology")]
    public void AllKnownViews_GeneratesSql(string view)
    {
        var p = new ViewTemplateParams { Topic = "/test/topic", PublisherNode = "node-1" };
        var result = Svc.Generate(view, p);
        Assert.False(string.IsNullOrWhiteSpace(result.Sql));
    }

    [Fact]
    public void UnknownView_ThrowsArgumentException()
    {
        var p = new ViewTemplateParams { Topic = "/t" };
        Assert.Throws<ArgumentException>(() => Svc.Generate("not-a-view", p));
    }

    [Fact]
    public void IsKnownView_ReturnsTrueForKnown() =>
        Assert.True(Svc.IsKnownView("timeline"));

    [Fact]
    public void IsKnownView_ReturnsFalseForUnknown() =>
        Assert.False(Svc.IsKnownView("rainbow"));

    [Fact]
    public void Generate_NullView_Throws() =>
        Assert.Throws<ArgumentNullException>(() => Svc.Generate(null!, new ViewTemplateParams()));

    [Fact]
    public void Generate_NullParams_Throws() =>
        Assert.Throws<ArgumentNullException>(() => Svc.Generate("timeline", null!));

    [Fact]
    public void TopicInSql_EscapesSingleQuotes()
    {
        var p = new ViewTemplateParams { Topic = "it's/bad" };
        var result = Svc.Generate("timeline", p);
        Assert.DoesNotContain("it's", result.Sql);
    }

    [Fact]
    public void Timeline_ContainsTopicFilter()
    {
        var p = new ViewTemplateParams { Topic = "/sensor/data" };
        var result = Svc.Generate("timeline", p);
        Assert.Contains("/sensor/data", result.Sql);
    }

    [Fact]
    public void Latency_ContainsTopic()
    {
        var p = new ViewTemplateParams { Topic = "/latency/test" };
        var result = Svc.Generate("latency", p);
        Assert.Contains("/latency/test", result.Sql);
    }
}
