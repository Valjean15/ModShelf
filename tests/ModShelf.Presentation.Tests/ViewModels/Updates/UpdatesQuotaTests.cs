using ModShelf.Domain.Mods;
using ModShelf.Presentation.ViewModels.Updates;
using ModShelf.Sources.Abstractions.Quota;

namespace ModShelf.Presentation.Tests.ViewModels.Updates;

/// <summary>
/// The Updates screen before a check or a batch of downloads that would likely use up a site's requests: it asks first,
/// and goes on only as told.
/// </summary>
public sealed class UpdatesQuotaTests : IDisposable
{
    private static readonly ModRef SkyUi = TestData.Mod("12604");
    private static readonly ModRef Skse = TestData.Mod("30379");
    private static readonly ModRef Smapi = TestData.Mod("2400", "stardewvalley");

    private readonly UpdatesHarness _harness = new();

    private UpdatesViewModel Screen => _harness.Screen;

    public void Dispose() => _harness.Dispose();

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CheckNowAsksFirstWhenTheCheckLikelyUsesUpTheRequests(bool goOn)
    {
        // A feed for each game, and one request for each mod never checked: 2 + 2.
        await _harness.StartAsync(
            TestData.LibraryMod(SkyUi, "SkyUI"),
            UpdatesHarness.ModWithUpdates(Skse, "SKSE64", TestData.Now),
            TestData.LibraryMod(Smapi, "SMAPI"));
        _harness.Catalog.Quota = new ApiQuota([new QuotaAllowance(TimeSpan.FromHours(1), 100, 3, TestData.Now.AddMinutes(30))]);
        _harness.Dialogs.Confirm.Enqueue(goOn);

        await Screen.CheckNowCommand.ExecuteAsync(null);

        Assert.StartsWith("This may take about 4 requests to Nexus Mods, and 3 are left this hour.", Assert.Single(_harness.Dialogs.Confirmations).Message, StringComparison.Ordinal);
        Assert.Equal(goOn ? 1 : 0, _harness.Checker.Checks);
        Assert.False(Screen.IsChecking);
    }

    [Fact]
    public async Task CheckNowGoesOnAtOnceWithEnoughLeft()
    {
        await _harness.StartAsync(TestData.LibraryMod(SkyUi, "SkyUI"));
        _harness.Catalog.Quota = new ApiQuota([new QuotaAllowance(TimeSpan.FromHours(1), 100, 2, TestData.Now.AddMinutes(30))]);

        await Screen.CheckNowCommand.ExecuteAsync(null);

        Assert.Empty(_harness.Dialogs.Confirmations);
        Assert.Equal(1, _harness.Checker.Checks);
    }

    [Fact]
    public async Task UpdateAllAsksForTwoRequestsAFile()
    {
        await _harness.StartAsync(UpdatesHarness.ModWithUpdates(SkyUi, "SkyUI", TestData.Now, "52", "53"));
        _harness.Catalog.Quota = new ApiQuota([new QuotaAllowance(TimeSpan.FromHours(1), 100, 3, TestData.Now.AddMinutes(30))]);

        await Screen.UpdateAllCommand.ExecuteAsync(null);

        Assert.StartsWith("This may take about 4 requests to Nexus Mods", Assert.Single(_harness.Dialogs.Confirmations).Message, StringComparison.Ordinal);
        Assert.Empty(_harness.Downloads.Added);
        Assert.False(Screen.IsUpdating);
    }
}
