using FluentAssertions;
using Tracer.Adapters.Nas;
using Xunit;

namespace Tracer.Tests.Unit.Adapters.Nas;

public sealed class SmbPathResolverTests
{
    [Fact]
    public void Resolve_ValidComponents_ReturnsExpectedPath()
    {
        var resolver = new SmbPathResolver(@"\\nas-server\tracer");

        var result = resolver.Resolve("blue-cmd-01", "20260519T140000Z");

        // Path.Combine normalises separators; verify key segments.
        result.Should().Contain("blue-cmd-01");
        result.Should().Contain("20260519T140000Z.zip");
        result.Should().Contain("telemetry");
    }

    [Fact]
    public void Resolve_DirectoryTraversalInNodeId_ThrowsArgumentException()
    {
        var resolver = new SmbPathResolver(@"C:\fake-nas");

        var act = () => resolver.Resolve(@"..\evil", "20260519T140000Z");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Resolve_DirectoryTraversalInTimestamp_ThrowsArgumentException()
    {
        var resolver = new SmbPathResolver(@"C:\fake-nas");

        var act = () => resolver.Resolve("node-1", @"..\bad");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Resolve_SlashInNodeId_ThrowsArgumentException()
    {
        var resolver = new SmbPathResolver(@"C:\fake-nas");

        var act = () => resolver.Resolve("node/1", "20260519T140000Z");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ResolveNodeDir_ValidNodeId_ReturnsNodeDirectory()
    {
        var resolver = new SmbPathResolver(@"C:\nas");

        var result = resolver.ResolveNodeDir("node-1");

        result.Should().EndWith("node-1");
        result.Should().Contain("telemetry");
    }
}
