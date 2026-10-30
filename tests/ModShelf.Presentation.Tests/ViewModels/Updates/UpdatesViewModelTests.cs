using System.Globalization;

using ModShelf.Application.Updates;
using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;
using ModShelf.Presentation.Tests.Common;
using ModShelf.Presentation.ViewModels.Updates;

namespace ModShelf.Presentation.Tests.ViewModels.Updates;

public sealed class UpdatesViewModelTests : IDisposable
{
    private static readonly ModRef SkyUi = TestData.Mod("12604");
    private static readonly ModRef Address = TestData.Mod("3863");
    private static readonly ModRef Smapi = TestData.Mod("2400", "stardewvalley");

    private readonly UpdatesHarness _harness = new();

    private UpdatesViewModel Screen => _harness.Screen;

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task ListsTheUpdatesByModThenNewestFirst()
    {
        await _harness.StartAsync(
            UpdatesHarness.ModWithUpdates(SkyUi, "SkyUI", TestData.Now, "52", "53"),
            UpdatesHarness.ModWithUpdates(Address, "Address Library", TestData.Now, "7"),
            UpdatesHarness.ModWithUpdates(Smapi, "SMAPI", TestData.Now));

        Assert.Equal(["Address Library 7", "SkyUI 53", "SkyUI 52"], Screen.Updates.Select(update => $"{update.ModName} {update.File.Id}"));
        Assert.Equal(3, Screen.Badge);
        Assert.True(Screen.HasUpdates);
    }

    [Fact]
    public async Task WithoutUpdatesItSaysWhy()
    {
        await _harness.StartAsync(UpdatesHarness.ModWithUpdates(SkyUi, "SkyUI", TestData.Now));

        Assert.Equal((false, "No updates found.", 0), (Screen.HasUpdates, Screen.NoUpdatesText, Screen.Badge));
        Assert.True(Screen.CheckNowCommand.CanExecute(null));
    }

    [Fact]
    public async Task WithoutWatchedModsTheresNothingToCheck()
    {
        await _harness.StartAsync(TestData.LibraryMod(SkyUi, "SkyUI", watched: false, TestData.LibraryFile(SkyUi, "1", "a.7z")));

        Assert.Equal("No mods are watched. Mods you download into the library are watched for updates.", Screen.NoUpdatesText);
        Assert.Equal(string.Empty, Screen.CheckedText);
        Assert.False(Screen.CheckNowCommand.CanExecute(null));
    }

    [Fact]
    public async Task SaysWhenEveryWatchedModWasCheckedBy()
    {
        using CultureScope culture = new("en-US");

        await _harness.StartAsync(
            UpdatesHarness.ModWithUpdates(SkyUi, "SkyUI", TestData.Now.AddHours(-1)),
            UpdatesHarness.ModWithUpdates(Address, "Address Library", TestData.Now.AddHours(-3)));

        // The oldest check, in the user's time zone (UTC here); how the culture writes a time varies with its ICU version.
        string oldest = new DateTime(2026, 10, 27, 9, 0, 0).ToString("g", CultureInfo.CurrentCulture);
        Assert.Equal($"Every watched mod was checked on {oldest} or later.", Screen.CheckedText);
        Assert.StartsWith("Every watched mod was checked on 10/27/2026 9:00", Screen.CheckedText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SaysWhenSomeWatchedModsWerentCheckedYet()
    {
        await _harness.StartAsync(
            UpdatesHarness.ModWithUpdates(SkyUi, "SkyUI", TestData.Now),
            UpdatesHarness.ModWithUpdates(Address, "Address Library", null));

        Assert.Equal("Some watched mods haven't been checked yet.", Screen.CheckedText);
    }

    [Fact]
    public async Task FollowsTheLibrarysChanges()
    {
        await _harness.StartAsync(UpdatesHarness.ModWithUpdates(SkyUi, "SkyUI", TestData.Now, "52"));
        UpdateViewModel row = _harness.Row("52");

        _harness.Library.Change(Address, UpdatesHarness.ModWithUpdates(Address, "Address Library", TestData.Now, "7"));
        _harness.Library.Change(SkyUi, UpdatesHarness.ModWithUpdates(SkyUi, "SkyUI renamed", TestData.Now, "52", "53"));
        int badge = Screen.Badge;
        _harness.Library.Change(Address, null);

        Assert.Equal(3, badge);
        Assert.Equal(["53", "52"], Screen.Updates.Select(update => update.File.Id));
        Assert.Same(row, _harness.Row("52"));
        Assert.Equal("SkyUI renamed", row.ModName);
        Assert.Equal(2, Screen.Badge);
    }

    [Fact]
    public async Task ChangesShowOnlyOnTheUiThread()
    {
        _harness.UiThread.Inline = false;
        await _harness.StartAsync(UpdatesHarness.ModWithUpdates(SkyUi, "SkyUI", TestData.Now, "52"));
        int listedBeforeTheUiThreadRan = Screen.Updates.Count;
        _harness.UiThread.RunPending();

        _harness.Library.Change(SkyUi, UpdatesHarness.ModWithUpdates(SkyUi, "SkyUI", TestData.Now));
        int beforeTheUiThreadRan = Screen.Updates.Count;
        _harness.UiThread.RunPending();

        Assert.Equal((0, 1), (listedBeforeTheUiThreadRan, beforeTheUiThreadRan));
        Assert.Empty(Screen.Updates);
    }

    [Fact]
    public async Task CheckingNowSaysWhatTheCheckDid()
    {
        await _harness.StartAsync(UpdatesHarness.ModWithUpdates(SkyUi, "SkyUI", TestData.Now), UpdatesHarness.ModWithUpdates(Address, "Address Library", TestData.Now));
        LibraryMod found = UpdatesHarness.ModWithUpdates(SkyUi, "SkyUI", TestData.Now, "53");
        _harness.Checker.During = () => _harness.Library.Change(SkyUi, found);
        _harness.Checker.Answer = new UpdateCheck(12, 3, [found.Updates[0]], []);

        await Screen.CheckNowCommand.ExecuteAsync(null);

        Assert.Equal(("Checked 12 mods with 3 requests. 1 new update.", false), (Screen.CheckResult, Screen.IsCheckResultAProblem));
        Assert.Empty(Screen.CheckFailures);
        Assert.Equal(["53"], Screen.Updates.Select(update => update.File.Id));
        Assert.False(Screen.IsChecking);
    }

    [Theory]
    [InlineData(1, 1, 0, "Checked 1 mod with 1 request. No new updates.")]
    [InlineData(2, 0, 4, "Checked 2 mods with 0 requests. 4 new updates.")]
    public async Task TheCheckSaysHowMany(int mods, int requests, int found, string text)
    {
        await _harness.StartAsync(UpdatesHarness.ModWithUpdates(SkyUi, "SkyUI", TestData.Now));
        _harness.Checker.Answer = new UpdateCheck(mods, requests, [.. Enumerable.Range(1, found).Select(id => UpdatesHarness.Update(SkyUi, $"{id + 1}", TestData.Now))], []);

        await Screen.CheckNowCommand.ExecuteAsync(null);

        Assert.Equal(text, Screen.CheckResult);
    }

    [Fact]
    public async Task WhileCheckingItSaysSoAndCantCheckAgain()
    {
        await _harness.StartAsync(UpdatesHarness.ModWithUpdates(SkyUi, "SkyUI", TestData.Now));
        _harness.Checker.Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        Task checking = Screen.CheckNowCommand.ExecuteAsync(null);
        (bool, string?, bool) during = (Screen.IsChecking, Screen.CheckResult, Screen.CheckNowCommand.CanExecute(null));
        _harness.Checker.Hold.SetResult();
        await checking;

        Assert.Equal((true, "Checking the watched mods…", false), during);
        Assert.True(Screen.CheckNowCommand.CanExecute(null));
        Assert.Equal(1, _harness.Checker.Checks);
    }

    [Fact]
    public async Task ListsWhatTheCheckCouldntCheck()
    {
        await _harness.StartAsync(UpdatesHarness.ModWithUpdates(SkyUi, "SkyUI", TestData.Now));
        _harness.Checker.Answer = new UpdateCheck(
            1,
            2,
            [],
            [
                new UpdateCheckFailure(SkyUi.Game, SkyUi, "The site couldn't be reached."),
                new UpdateCheckFailure(Address.Game, Address, "No such mod."),
                new UpdateCheckFailure(Smapi.Game, null, "The account was refused."),
                new UpdateCheckFailure(new GameRef(UpdatesHarness.Plain, "game"), null, "Slow down."),
                new UpdateCheckFailure(new GameRef(new SourceId("gone"), "game"), null, "Unknown."),
            ]);

        await Screen.CheckNowCommand.ExecuteAsync(null);

        Assert.Equal(
            [
                "Couldn't check SkyUI: The site couldn't be reached.",
                "Couldn't check 3863: No such mod.",
                "Couldn't check the mods of stardewvalley on Nexus Mods: The account was refused.",
                "Couldn't check the mods of game on Plain Site: Slow down.",
                "Couldn't check the mods of game on gone: Unknown.",
            ],
            Screen.CheckFailures);
    }

    [Fact]
    public async Task ALibraryThatCantBeReadIsToldOf()
    {
        await _harness.StartAsync(UpdatesHarness.ModWithUpdates(SkyUi, "SkyUI", TestData.Now));
        _harness.Checker.Refusal = new InvalidDataException("The library database holds a value no mod can have.");

        await Screen.CheckNowCommand.ExecuteAsync(null);

        Assert.Equal(("The library database holds a value no mod can have.", true), (Screen.CheckResult, Screen.IsCheckResultAProblem));
        Assert.Empty(_harness.Logger.Entries);
    }

    [Fact]
    public async Task ACheckThatFailsUnexpectedlyIsLogged()
    {
        await _harness.StartAsync(UpdatesHarness.ModWithUpdates(SkyUi, "SkyUI", TestData.Now));
        _harness.Checker.Refusal = new InvalidOperationException("Something broke.");

        await Screen.CheckNowCommand.ExecuteAsync(null);

        Assert.Equal(("Something went wrong. The log has the details.", true), (Screen.CheckResult, Screen.IsCheckResultAProblem));
        Assert.StartsWith(
            "Error: Checking the watched mods for updates failed System.InvalidOperationException",
            Assert.Single(_harness.Logger.Entries),
            StringComparison.Ordinal);
        Assert.False(Screen.IsChecking);
    }

    [Fact]
    public async Task ANewCheckClearsWhatTheLastCouldntCheck()
    {
        await _harness.StartAsync(UpdatesHarness.ModWithUpdates(SkyUi, "SkyUI", TestData.Now));
        _harness.Checker.Answer = new UpdateCheck(1, 1, [], [new UpdateCheckFailure(SkyUi.Game, SkyUi, "Offline.")]);
        await Screen.CheckNowCommand.ExecuteAsync(null);
        _harness.Checker.Answer = new UpdateCheck(1, 1, [], []);

        await Screen.CheckNowCommand.ExecuteAsync(null);

        Assert.Empty(Screen.CheckFailures);
    }

    [Fact]
    public async Task TheWorkEndsWhenTheAppCloses()
    {
        await _harness.StartAsync();

        _harness.Dispose();

        await _harness.Work.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
    }
}
