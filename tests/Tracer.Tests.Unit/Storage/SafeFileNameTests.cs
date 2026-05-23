using FluentAssertions;
using Tracer.Bundle.Format;
using Xunit;

namespace Tracer.Tests.Unit.Storage;

/// <summary>Unit tests for SafeFileName collision prevention in fast-state file naming (FIX-C2).</summary>
public sealed class SafeFileNameTests
{
    // ── BundleNaming.SafeFileName ─────────────────────────────────────────────

    [Fact]
    public void SafeFileName_ReplacesSpecialChars()
    {
        var result = BundleNaming.SafeFileName("my/topic:name");
        result.Should().NotContain("/").And.NotContain(":");
    }

    [Fact]
    public void SafeFileName_AppendsFourCharHexSuffix()
    {
        // The suffix is 4 hex chars: [0-9a-f]{4}
        var result = BundleNaming.SafeFileName("test.topic");
        var parts = result.Split('_');
        var suffix = parts[^1];
        suffix.Should().HaveLength(4).And.MatchRegex("^[0-9a-f]{4}$",
            because: "collision prevention suffix must be 4 lowercase hex chars");
    }

    [Fact]
    public void SafeFileName_DifferentInputs_ProduceDifferentSuffixes()
    {
        // Two topics that normalize to the same sanitized prefix must differ by suffix
        var topic1 = "my/topic";   // normalizes to "my_topic"
        var topic2 = "my_topic";   // already "my_topic"
        var r1 = BundleNaming.SafeFileName(topic1);
        var r2 = BundleNaming.SafeFileName(topic2);
        r1.Should().NotBe(r2,
            because: "distinct topics that collapse to the same safe prefix must differ via hash suffix");
    }

    [Fact]
    public void SafeFileName_SameInput_IsDeterministic()
    {
        var topic = "some.topic/name";
        BundleNaming.SafeFileName(topic).Should().Be(BundleNaming.SafeFileName(topic),
            because: "same topic must always produce the same filename");
    }

    [Fact]
    public void SafeFileName_AllowsHyphenDotUnderscore()
    {
        var result = BundleNaming.SafeFileName("topic-v1.0_beta");
        // The leading segment before the underscore suffix should preserve these chars
        result.Should().Contain("topic-v1.0_beta",
            because: "hyphens, dots, and underscores in input are preserved");
    }

    [Fact]
    public void SafeFileName_NullInput_Throws()
    {
        var act = () => BundleNaming.SafeFileName(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // ── Verify DuckDbStorageWriter no longer uses the old simple method ─────────

    [Fact]
    public void SafeFileName_CollidingTopics_ProduceDifferentFilenames()
    {
        // "a/b" and "a_b" both look like "a_b" without a hash — verify they differ
        var f1 = BundleNaming.SafeFileName("a/b");
        var f2 = BundleNaming.SafeFileName("a_b");
        f1.Should().NotBe(f2,
            because: "the hash suffix prevents filename collisions between distinct topics");
    }
}
