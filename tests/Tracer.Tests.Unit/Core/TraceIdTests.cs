using FluentAssertions;
using Tracer.Core.Domain;
using Tracer.Core.Identity;
using Xunit;

namespace Tracer.Tests.Unit.Core;

public sealed class TraceIdTests
{
    [Fact]
    public void TraceId_None_ValueIsZero()
    {
        TraceId.None.Value.Should().Be(0UL);
    }

    [Fact]
    public void TraceId_FormatsAs16CharUppercaseHex()
    {
        var id = new TraceId(0xABCDEF1234567890UL);
        id.ToString().Should().Be("ABCDEF1234567890");
        id.ToString().Should().HaveLength(16);
    }

    [Fact]
    public void TraceId_Equality_WorksAcrossConstructionPaths()
    {
        var a = new TraceId(42);
        var b = new TraceId(42);
        var c = new TraceId(99);

        a.Should().Be(b);
        a.Should().NotBe(c);
    }

    [Fact]
    public void AgentId_RejectsNullOrEmpty()
    {
        var actNull = () => new AgentId(null!);
        var actEmpty = () => new AgentId("");
        var actWhitespace = () => new AgentId("   ");

        actNull.Should().Throw<ArgumentException>();
        actEmpty.Should().Throw<ArgumentException>();
        actWhitespace.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AgentId_RejectsOver64Chars()
    {
        var tooLong = new string('x', 65);
        var act = () => new AgentId(tooLong);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void EntityId_RejectsEmpty()
    {
        var actEmpty = () => new EntityId("");
        var actWhitespace = () => new EntityId("  ");

        actEmpty.Should().Throw<ArgumentException>();
        actWhitespace.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TopicName_RejectsEmpty()
    {
        var actEmpty = () => new TopicName("");
        var actWhitespace = () => new TopicName("   ");

        actEmpty.Should().Throw<ArgumentException>();
        actWhitespace.Should().Throw<ArgumentException>();
    }
}
