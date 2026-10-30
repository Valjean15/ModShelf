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
}
