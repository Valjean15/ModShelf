using System.Globalization;

using ModShelf.Application.Library;
using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;
using ModShelf.Presentation.Tests.Common;
using ModShelf.Presentation.Tests.ViewModels.Browse;
using ModShelf.Presentation.ViewModels.Library;

namespace ModShelf.Presentation.Tests.ViewModels.Library;

/// <summary>
/// The Library's recent activity: what happened to its files, by day, the newest first, narrowed to a kind, each opening
/// its mod's page. The time zone is UTC, and today is Tuesday, October 27, 2026.
/// </summary>
public sealed class LibraryActivityTests : IDisposable
{
    private static readonly ModRef SkyUi = TestData.Mod("12604");
    private static readonly ModRef Smapi = TestData.Mod("2400", "stardewvalley");
    private static readonly LibraryMod InLibrary = TestData.LibraryMod(SkyUi, "SkyUI");

    private readonly LibraryHarness _harness = new();
    private readonly CultureScope _culture = new("en-US");

    private LibraryViewModel Screen => _harness.Screen;

    private LibraryActivityViewModel Activity => Screen.Activity;

    public void Dispose()
    {
        _culture.Dispose();
        _harness.Dispose();
    }

    [Fact]
    public async Task ShowsWhatHappenedByDayTheNewestFirst()
    {
        _harness.History.Events.AddRange(
        [
            Downloaded(TestData.Now.AddMinutes(-30)),
            new(TestData.Now.AddMinutes(-30), LibraryEventKind.Deleted, SkyUi, "SkyUI", File("50"), "SkyUI 5.0", "5.0", ByRule: true),
            new(TestData.Now.AddDays(-1), LibraryEventKind.Found, Smapi, "SMAPI", new ModFileRef(Smapi, "1"), "SMAPI 4.1", null),
            new(new DateTimeOffset(2026, 10, 15, 23, 0, 0, TimeSpan.Zero), LibraryEventKind.Removed, SkyUi, "SkyUI"),
        ]);
        await _harness.StartAsync(InLibrary);

        await Screen.ShowActivityCommand.ExecuteAsync(null);

        Assert.True(Screen.IsShowingActivity);
        Assert.Equal(["Today", "Yesterday", "Thursday, October 15, 2026"], Activity.Days.Select(day => day.Heading));
        Assert.Equal(
            [
                ("Downloaded", "SkyUI", "Skyrim Special Edition", "Downloaded SkyUI 5.3 (5.3), replacing 5.2."),
                ("Deleted", "SkyUI", "Skyrim Special Edition", "Deleted SkyUI 5.0 (5.0), an old version, as the setting for old versions says."),
            ],
            Activity.Days[0].Entries.Select(entry => (entry.KindText, entry.ModName, entry.GameName, entry.Text)));
        Assert.Equal(TestData.Now.AddMinutes(-30).ToString("t", CultureInfo.GetCultureInfo("en-US")), Activity.Days[0].Entries[0].TimeText);

        // A game the library doesn't have goes by its key.
        ActivityEntryViewModel found = Assert.Single(Activity.Days[1].Entries);
        Assert.Equal(("Found", "stardewvalley", "Found SMAPI 4.1 on disk in a rescan, and added it."), (found.KindText, found.GameName, found.Text));
        ActivityEntryViewModel removed = Assert.Single(Activity.Days[2].Entries);
        Assert.Equal(("Removed", "Removed from the library; its files stayed on disk."), (removed.KindText, removed.Text));
        Assert.Equal(((string?)null, (string?)null, (string?)null, false), (Activity.EmptyText, Activity.Problem, Activity.CappedText, Activity.IsLoading));
        Assert.Equal([LibraryActivityViewModel.Limit], _harness.History.Asked);
    }

    [Fact]
    public async Task DaysAndTimesAreTheUsersOwn()
    {
        // Six hours behind UTC, today's 3:00 UTC was yesterday at 9 PM.
        _harness.Time.SetLocalTimeZone(TimeZoneInfo.CreateCustomTimeZone("Six behind", TimeSpan.FromHours(-6), "Six behind", "Six behind"));
        _harness.History.Events.Add(Downloaded(new DateTimeOffset(2026, 10, 27, 3, 0, 0, TimeSpan.Zero)));
        await _harness.StartAsync(InLibrary);

        await Screen.ShowActivityCommand.ExecuteAsync(null);

        ActivityDayViewModel day = Assert.Single(Activity.Days);
        Assert.Equal("Yesterday", day.Heading);
        Assert.Equal(new DateTime(2026, 1, 1, 21, 0, 0).ToString("t", CultureInfo.GetCultureInfo("en-US")), Assert.Single(day.Entries).TimeText);
    }

    [Fact]
    public async Task SaysEachKindOfWhatHappened()
    {
        _harness.History.Events.AddRange(
        [
            Downloaded(TestData.Now) with { Replaces = File("52"), ReplacesVersion = null },
            Downloaded(TestData.Now) with { Replaces = null, ReplacesVersion = null, VersionLabel = null },
            new(TestData.Now, LibraryEventKind.Archived, SkyUi, "SkyUI", File("52"), "SkyUI 5.2", "5.2"),
            new(TestData.Now, LibraryEventKind.Deleted, SkyUi, "SkyUI", File("51"), null, null),
        ]);
        await _harness.StartAsync(InLibrary);

        await Screen.ShowActivityCommand.ExecuteAsync(null);

        Assert.Equal(
            [
                "Downloaded SkyUI 5.3 (5.3), replacing an older version.",
                "Downloaded SkyUI 5.3.",
                "Moved SkyUI 5.2 (5.2) to its _archive folder.",
                "Deleted 51 from disk and the library.",
            ],
            Assert.Single(Activity.Days).Entries.Select(entry => entry.Text));
        Assert.Equal(["Downloaded", "Downloaded", "Archived", "Deleted"], Activity.Days[0].Entries.Select(entry => entry.KindText));
    }

    [Fact]
    public async Task NarrowsToAKind()
    {
        _harness.History.Events.AddRange(
        [
            Downloaded(TestData.Now),
            new(TestData.Now.AddDays(-3), LibraryEventKind.Archived, SkyUi, "SkyUI", File("52"), "SkyUI 5.2", "5.2"),
        ]);
        await _harness.StartAsync(InLibrary);
        await Screen.ShowActivityCommand.ExecuteAsync(null);

        Activity.SelectedKind = Activity.Kinds.Single(kind => kind.Value == LibraryEventKind.Archived);

        Assert.Equal(["Archived"], Assert.Single(Activity.Days).Entries.Select(entry => entry.KindText));
        Assert.Equal("Saturday, October 24, 2026", Activity.Days[0].Heading);

        Activity.SelectedKind = Activity.Kinds.Single(kind => kind.Value == LibraryEventKind.Removed);

        Assert.Empty(Activity.Days);
        Assert.Equal("Nothing of that kind happened in the last year.", Activity.EmptyText);

        Activity.SelectedKind = Activity.Kinds[0];

        Assert.Equal(2, Activity.Days.Count);
        Assert.Null(Activity.EmptyText);
        Assert.Equal(
            ["Everything", "Downloads", "Archived versions", "Deleted files", "Found by rescans", "Removed mods"],
            Activity.Kinds.Select(kind => kind.Label));
    }

    [Fact]
    public async Task AnEmptyHistorySaysWhatShowsThere()
    {
        await _harness.StartAsync(InLibrary);

        await Screen.ShowActivityCommand.ExecuteAsync(null);

        Assert.Empty(Activity.Days);
        Assert.StartsWith("Nothing has happened in the library yet.", Activity.EmptyText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AHistoryThatCantBeReadIsLoggedAndSaid()
    {
        _harness.History.Refusal = new InvalidOperationException("The database is locked.");
        await _harness.StartAsync(InLibrary);

        await Screen.ShowActivityCommand.ExecuteAsync(null);

        Assert.Equal("The library's history can't be read right now. The log has the details.", Activity.Problem);
        Assert.Null(Activity.EmptyText);
        Assert.Empty(Activity.Days);
        Assert.False(Activity.IsLoading);
        Assert.StartsWith(
            "Error: Reading the library's history failed System.InvalidOperationException: The database is locked.",
            Assert.Single(_harness.Logger.Entries),
            StringComparison.Ordinal);

        _harness.History.Refusal = null;
        _harness.History.Events.Add(Downloaded(TestData.Now));
        Screen.ShowGamesCommand.Execute(null);
        await Screen.ShowActivityCommand.ExecuteAsync(null);

        Assert.Null(Activity.Problem);
        Assert.Single(Activity.Days);
    }

    [Fact]
    public async Task AHistoryThatCantBeReadAgainShowsNothingFromBefore()
    {
        _harness.History.Events.Add(Downloaded(TestData.Now));
        await _harness.StartAsync(InLibrary);
        await Screen.ShowActivityCommand.ExecuteAsync(null);
        Screen.ShowGamesCommand.Execute(null);
        _harness.History.Refusal = new InvalidOperationException("The database is locked.");

        await Screen.ShowActivityCommand.ExecuteAsync(null);

        Assert.Empty(Activity.Days);
        Assert.NotNull(Activity.Problem);
    }

    [Fact]
    public async Task AReadStoppedAsTheAppClosesIsntLeftLoading()
    {
        TaskCompletionSource held = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _harness.History.Held = held;
        await _harness.StartAsync(InLibrary);
        Task showing = Screen.ShowActivityCommand.ExecuteAsync(null);

        _harness.Dispose();
        await showing;

        Assert.False(Activity.IsLoading);
        Assert.Empty(_harness.Logger.Entries);
    }

    [Fact]
    public async Task SaysWhenItShowsOnlyTheNewest()
    {
        _harness.History.Events.AddRange(Enumerable.Range(0, LibraryActivityViewModel.Limit + 1).Select(minutes => Downloaded(TestData.Now.AddMinutes(-minutes))));
        await _harness.StartAsync(InLibrary);

        await Screen.ShowActivityCommand.ExecuteAsync(null);

        Assert.Equal("The newest 1,000 are shown.", Activity.CappedText);
        Assert.Equal(LibraryActivityViewModel.Limit, Activity.Days.Sum(day => day.Entries.Count));
    }

    [Fact]
    public async Task ShowsLoadingWhileItsRead()
    {
        TaskCompletionSource held = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _harness.History.Held = held;
        await _harness.StartAsync(InLibrary);

        Task showing = Screen.ShowActivityCommand.ExecuteAsync(null);

        Assert.True(Activity.IsLoading);
        Assert.True(Screen.IsShowingActivity);
        held.SetResult();
        await showing;
        Assert.False(Activity.IsLoading);
    }

    [Fact]
    public async Task IsReadAgainEachTimeItOpens()
    {
        await _harness.StartAsync(InLibrary);
        await Screen.ShowActivityCommand.ExecuteAsync(null);
        Assert.True(Screen.ShowGamesCommand.CanExecute(null));

        Screen.ShowGamesCommand.Execute(null);
        _harness.History.Events.Add(Downloaded(TestData.Now));
        await Screen.ShowActivityCommand.ExecuteAsync(null);

        Assert.False(Screen.IsGameOpen);
        Assert.Single(Activity.Days);
        Assert.Equal(2, _harness.History.Asked.Count);
    }

    [Fact]
    public async Task TheLibraryCrumbGoesBackToTheGames()
    {
        await _harness.StartAsync(InLibrary);
        await Screen.ShowActivityCommand.ExecuteAsync(null);

        Screen.ShowGamesCommand.Execute(null);

        Assert.False(Screen.IsShowingActivity);
        Assert.False(Screen.ShowGamesCommand.CanExecute(null));
    }

    [Fact]
    public async Task OpensAModsPageAndGoesBackToTheActivity()
    {
        _harness.Lookup.Pages[SkyUi] = BrowseHarness.Page(BrowseHarness.Info(SkyUi, "SkyUI"));
        _harness.History.Events.Add(Downloaded(TestData.Now));
        await _harness.StartAsync(InLibrary);
        await Screen.ShowActivityCommand.ExecuteAsync(null);

        await Assert.Single(Assert.Single(Activity.Days).Entries).OpenCommand.ExecuteAsync(null);
        await Screen.Opening;

        Assert.Equal(("SkyUI", "Recent activity"), (Screen.Mod?.Name, Screen.BackText));

        Screen.BackCommand.Execute(null);

        Assert.Null(Screen.Mod);
        Assert.True(Screen.IsShowingActivity);
        Assert.Null(Screen.OpenGame);

        await Assert.Single(Activity.Days[0].Entries).OpenCommand.ExecuteAsync(null);
        await Screen.Opening;
        Screen.ShowGamesCommand.Execute(null);

        Assert.Equal((false, false), (Screen.IsModOpen, Screen.IsShowingActivity));
    }

    [Fact]
    public async Task AModOpenedFromItsGameStillGoesBackToItsGame()
    {
        _harness.Lookup.Pages[SkyUi] = BrowseHarness.Page(BrowseHarness.Info(SkyUi, "SkyUI"));
        await _harness.StartAsync(InLibrary);
        await Screen.ShowActivityCommand.ExecuteAsync(null);
        Screen.ShowGamesCommand.Execute(null);

        await _harness.Row(InLibrary).OpenCommand.ExecuteAsync(null);
        await Screen.Opening;

        Assert.Equal("Skyrim Special Edition", Screen.BackText);
    }

    private static ModFileRef File(string id) => new(SkyUi, id);

    private static LibraryEvent Downloaded(DateTimeOffset at)
        => new(at, LibraryEventKind.Downloaded, SkyUi, "SkyUI", File("53"), "SkyUI 5.3", "5.3", "SkyUI/SkyUI 5.3.7z", File("52"), "5.2");
}
