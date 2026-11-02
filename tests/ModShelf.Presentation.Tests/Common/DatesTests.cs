using Microsoft.Extensions.Time.Testing;

using ModShelf.Presentation.Common;

namespace ModShelf.Presentation.Tests.Common;

public sealed class DatesTests
{
    // Late on the 29th in UTC: already the 30th fourteen hours ahead.
    private static readonly DateTimeOffset Moment = new(2026, 10, 29, 22, 5, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _time = new();

    public DatesTests()
    {
        _time.SetLocalTimeZone(TimeZoneInfo.CreateCustomTimeZone("UTC+14", TimeSpan.FromHours(14), "UTC+14", "UTC+14"));
    }

    [Fact]
    public void WritesTheDayInTheUsersTimeZone()
    {
        using CultureScope culture = new("en-US");

        Assert.Equal("10/30/2026", Dates.Day(Moment, _time));
    }

    // German writes times in 24 hours, with no space before "PM" that varies between systems.
    [Fact]
    public void WritesTheDayAndTimeInTheUsersTimeZone()
    {
        using CultureScope culture = new("de-DE");

        Assert.Equal("30.10.2026 12:05", Dates.DayAndTime(Moment, _time));
    }

    [Fact]
    public void WritesTheTimeInTheUsersTimeZone()
    {
        using CultureScope culture = new("de-DE");

        Assert.Equal("12:05", Dates.Time(Moment, _time));
    }

    [Fact]
    public void WritesDatesAsTheCultureDoes()
    {
        using CultureScope culture = new("de-DE");

        Assert.Equal("30.10.2026", Dates.Day(Moment, _time));
    }

    [Theory]
    [InlineData(-59, "just now")]
    [InlineData(0, "just now")]
    [InlineData(59, "just now")]
    [InlineData(60, "1 minute ago")]
    [InlineData(59 * 60, "59 minutes ago")]
    [InlineData(60 * 60, "1 hour ago")]
    [InlineData((24 * 60 * 60) - 1, "23 hours ago")]
    [InlineData(24 * 60 * 60, "yesterday")]
    [InlineData(2 * 24 * 60 * 60, "2 days ago")]
    [InlineData(30 * 24 * 60 * 60, "30 days ago")]
    public void SaysHowLongAgo(int seconds, string expected)
    {
        using CultureScope culture = new("en-US");
        _time.SetUtcNow(Moment.AddSeconds(seconds));

        Assert.Equal(expected, Dates.Ago(Moment, _time));
    }

    [Theory]
    [InlineData((30 * 24 * 60 * 60) + 1)]
    [InlineData(-60)]
    public void WritesTheDayOfAMomentLongAgoOrToCome(int seconds)
    {
        using CultureScope culture = new("en-US");
        _time.SetUtcNow(Moment.AddSeconds(seconds));

        Assert.Equal("10/30/2026", Dates.Ago(Moment, _time));
    }
}
