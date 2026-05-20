using FluentAssertions;
using Tracer.Core.Domain;
using Xunit;

namespace Tracer.Tests.Unit.Core;

public sealed class IntervalTimestampTests
{
    [Fact]
    public void IntervalTimestamp_ValidFormat_RoundTripsToDateTimeOffset()
    {
        var ts = new IntervalTimestamp("20260519T140000Z");
        var dto = ts.ToDateTimeOffset();
        dto.Year.Should().Be(2026);
        dto.Month.Should().Be(5);
        dto.Day.Should().Be(19);
        dto.Hour.Should().Be(14);
        dto.Minute.Should().Be(0);
        dto.Second.Should().Be(0);
        dto.Offset.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void IntervalTimestamp_MalformedString_ThrowsArgumentException()
    {
        var act = () => new IntervalTimestamp("bad");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void IntervalTimestamp_NonUtcDateTimeOffset_ThrowsArgumentException()
    {
        var nonUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.FromHours(1));
        var act = () => IntervalTimestamp.FromUtc(nonUtc);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void IntervalTimestamp_TryParse_ReturnsFalseForInvalidInput()
    {
        var result = IntervalTimestamp.TryParse("not-a-ts", out _);
        result.Should().BeFalse();
    }

    [Fact]
    public void IntervalTimestamp_TryParse_ReturnsTrueForValidInput()
    {
        var result = IntervalTimestamp.TryParse("20260101T120000Z", out var ts);
        result.Should().BeTrue();
        ts.Value.Should().Be("20260101T120000Z");
    }

    [Fact]
    public void CaptureGap_CanBeConstructedWithAllReasons()
    {
        var start = Tracer.Core.Time.WallclockTime.Zero;
        var end = start + TimeSpan.FromHours(1);

        foreach (var reason in Enum.GetValues<CaptureGapReason>())
        {
            var gap = new CaptureGap
            {
                StartUtc = start,
                EndUtc = end,
                Reason = reason,
                DroppedRecordCount = 0
            };
            gap.Reason.Should().Be(reason);
        }
    }
}
