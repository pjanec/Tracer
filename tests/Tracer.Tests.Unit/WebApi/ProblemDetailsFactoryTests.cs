using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Tracer.Core.Errors;
using Tracer.WebApi.Errors;
using Xunit;

namespace Tracer.Tests.Unit.WebApi;

public sealed class ProblemDetailsFactoryTests
{
    [Fact]
    public void ArgumentException_Returns400()
    {
        var result = ProblemDetailsFactory.From(new ArgumentException("bad input"));
        result.Status.Should().Be(400);
    }

    [Fact]
    public void TracerStorageException_Returns500()
    {
        var result = ProblemDetailsFactory.From(new TracerStorageException("storage failed"));
        result.Status.Should().Be(500);
    }

    [Fact]
    public void NullException_Returns500()
    {
        var result = ProblemDetailsFactory.From(null);
        result.Status.Should().Be(500);
    }

    [Fact]
    public void ArgumentException_DetailContainsMessage()
    {
        var msg = "value cannot be null";
        var result = ProblemDetailsFactory.From(new ArgumentException(msg));
        result.Detail.Should().Contain(msg);
    }
}
