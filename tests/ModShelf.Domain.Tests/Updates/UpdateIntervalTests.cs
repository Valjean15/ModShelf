using ModShelf.Domain.Updates;

namespace ModShelf.Domain.Tests.Updates;

public sealed class UpdateIntervalTests
{
    [Theory]
    [InlineData("15m", 15)]
    [InlineData("90m", 90)]
    [InlineData(" 6H ", 360)]
    [InlineData("1d", 1440)]
    [InlineData("7d", 10080)]
    public void ReadsMinutesHoursOrDays(string text, int minutes)
    {
        Assert.Equal(TimeSpan.FromMinutes(minutes), UpdateInterval.Parse(text));
    }

    [Theory]
    [InlineData("14m")]
    [InlineData("8d")]
    [InlineData("0h")]
    [InlineData("-1h")]
    [InlineData("+1h")]
    [InlineData("1.5h")]
    [InlineData("1h30m")]
    [InlineData("6")]
    [InlineData("h")]
    [InlineData("6w")]
    [InlineData("999999999d")]
    [InlineData("")]
    public void RefusesWhatIsntAnIntervalInRange(string text)
    {
        ArgumentException refused = Assert.Throws<ArgumentException>(() => UpdateInterval.Parse(text));

        Assert.StartsWith(
            $"'{text}' isn't an interval. Use a number of minutes, hours or days, such as 30m, 6h or 1d, from 15m to 7d.",
            refused.Message,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(15, "15m")]
    [InlineData(90, "90m")]
    [InlineData(360, "6h")]
    [InlineData(1440, "1d")]
    [InlineData(2880, "2d")]
    public void WritesTheLargestWholeUnit(int minutes, string text)
    {
        Assert.Equal(text, UpdateInterval.Format(TimeSpan.FromMinutes(minutes)));
    }

    [Fact]
    public void TheDefaultIsSixHours()
    {
        Assert.Equal(TimeSpan.FromHours(6), UpdateInterval.Default);
    }
}
