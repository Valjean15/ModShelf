using Microsoft.Extensions.Time.Testing;

using ModShelf.Domain.Mods;
using ModShelf.Presentation.Common;
using ModShelf.Presentation.Tests.Fakes;
using ModShelf.Sources.Abstractions.Quota;

namespace ModShelf.Presentation.Tests.Common;

/// <summary>
/// Before a big check or download: going on at once while each site has enough requests left, or doesn't say; asking,
/// with what's left and when it renews, when one likely hasn't.
/// </summary>
public sealed class QuotaChecksTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 10, 0, 0, TimeSpan.Zero);

    private readonly ScriptedCatalog _nexus = new(TestData.Nexus, "Nexus Mods");
    private readonly ScriptedCatalog _example = new(new SourceId("example"), "Example Mods");
    private readonly ScriptedDialogs _dialogs = new();
    private readonly FakeTimeProvider _time = new(Now);
    private readonly CultureScope _culture = new("en-US");

    public QuotaChecksTests()
    {
        _time.SetLocalTimeZone(TimeZoneInfo.Utc);
    }

    public void Dispose() => _culture.Dispose();

    [Fact]
    public async Task GoesOnAtOnceWhileEnoughIsLeftOrNothingIsSaid()
    {
        _nexus.Quota = Quota(hourLeft: 10, dayLeft: 900);

        Assert.True(await GoOnAsync((TestData.Nexus, 6), (TestData.Nexus, 4), (_example.Id, 500)));
        Assert.Empty(_dialogs.Confirmations);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AsksWhenASiteLikelyRunsOutAndGoesOnAsAnswered(bool goOn)
    {
        _nexus.Quota = Quota(hourLeft: 10, dayLeft: 900);
        _dialogs.Confirm.Enqueue(goOn);

        Assert.Equal(goOn, await GoOnAsync((TestData.Nexus, 6), (TestData.Nexus, 6)));
        Assert.Equal(
            [("Requests may run out", $"This may take about 12 requests to Nexus Mods, and 10 are left this hour. Once they're used up, the rest waits until {Time(Now.AddMinutes(45))}.", "Go on")],
            _dialogs.Confirmations);
    }

    [Fact]
    public async Task SaysTheDaysWhenTheyreTheFewer()
    {
        _example.Quota = Quota(hourLeft: 90, dayLeft: 30);
        _dialogs.Confirm.Enqueue(true);

        await GoOnAsync((_example.Id, 40));

        Assert.Contains($"and 30 are left today. Once they're used up, the rest waits until Sat {Time(Now.Date)}.", _dialogs.Confirmations[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnAllowanceThatsRenewedDoesntCount()
    {
        _nexus.Quota = new ApiQuota(
        [
            new QuotaAllowance(TimeSpan.FromDays(1), 1000, 900, Now.AddHours(14)),
            new QuotaAllowance(TimeSpan.FromHours(1), 100, 0, Now.AddMinutes(-1)),
        ]);

        Assert.True(await GoOnAsync((TestData.Nexus, 50)));
    }

    [Fact]
    public async Task OtherAllowancesSayOnlyHowManyAreLeft()
    {
        _nexus.Quota = new ApiQuota([new QuotaAllowance(TimeSpan.FromMinutes(15), 30, 3, Now.AddMinutes(10))]);

        await GoOnAsync((TestData.Nexus, 4));

        Assert.StartsWith("This may take about 4 requests to Nexus Mods, and 3 are left. Once", _dialogs.Confirmations[0].Message, StringComparison.Ordinal);
    }

    private static string Time(DateTimeOffset at) => at.ToString("t", System.Globalization.CultureInfo.CurrentCulture);

    private static ApiQuota Quota(int hourLeft, int dayLeft)
        => new(
        [
            new QuotaAllowance(TimeSpan.FromDays(1), 1000, dayLeft, new DateTimeOffset(2026, 11, 7, 0, 0, 0, TimeSpan.Zero)),
            new QuotaAllowance(TimeSpan.FromHours(1), 100, hourLeft, Now.AddMinutes(45)),
        ]);

    private Task<bool> GoOnAsync(params (SourceId Source, int Requests)[] needs)
        => QuotaChecks.GoOnAsync(new FakeSourceRegistry(_nexus, _example), _dialogs, _time, needs, TestContext.Current.CancellationToken);
}
