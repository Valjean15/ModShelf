using ModShelf.Sources.Abstractions.Quota;

namespace ModShelf.Sources.Abstractions.Tests.Quota;

public sealed class ApiQuotaTests
{
    private static readonly DateTimeOffset Midnight = new(2026, 10, 30, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void KeepsItsAllowancesInOrder()
    {
        QuotaAllowance day = new(TimeSpan.FromDays(1), 20000, 19997, Midnight);
        QuotaAllowance hour = new(TimeSpan.FromHours(1), 2000, 1997, Midnight.AddHours(-1));

        ApiQuota quota = new([day, hour]);

        Assert.Equal([day, hour], quota.Allowances);
        Assert.Equal((TimeSpan.FromDays(1), 20000, 19997, Midnight), (day.Period, day.Limit, day.Remaining, day.ResetsAt));
    }

    [Fact]
    public void HasAtLeastOneAllowance()
    {
        Assert.Throws<ArgumentException>(() => new ApiQuota([]));
        Assert.Throws<ArgumentException>(() => new ApiQuota([null!]));
    }

    [Theory]
    [InlineData(0, 10, 5)]
    [InlineData(-1, 10, 5)]
    [InlineData(60, -1, 5)]
    [InlineData(60, 10, -1)]
    public void AnAllowanceHasAPeriodAndNoCountBelowZero(int minutes, int limit, int remaining)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new QuotaAllowance(TimeSpan.FromMinutes(minutes), limit, remaining, Midnight));
    }

    [Fact]
    public void AnAllowanceMayBeUsedUp()
    {
        Assert.Equal(0, new QuotaAllowance(TimeSpan.FromHours(1), 0, 0, Midnight).Remaining);
    }
}
