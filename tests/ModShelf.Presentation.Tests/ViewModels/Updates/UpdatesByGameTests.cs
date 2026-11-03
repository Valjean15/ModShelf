using ModShelf.Application.Downloads;
using ModShelf.Application.Library;
using ModShelf.Application.Settings;
using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;
using ModShelf.Presentation.Tests.Common;
using ModShelf.Presentation.ViewModels.Updates;
using ModShelf.Sources.Abstractions.Accounts;
using ModShelf.Sources.Abstractions.Catalog;

namespace ModShelf.Presentation.Tests.ViewModels.Updates;

/// <summary>
/// The Updates screen by game: each game's updates under its heading, what they add up to, Update all for every update
/// or a game's, and the changelog entries read once the screen is shown.
/// </summary>
public sealed class UpdatesByGameTests : IDisposable
{
    private static readonly ModRef SkyUi = TestData.Mod("12604");
    private static readonly ModRef Address = TestData.Mod("3863");
    private static readonly ModRef Smapi = TestData.Mod("2400", "stardewvalley");

    private readonly UpdatesHarness _harness = new();

    private UpdatesViewModel Screen => _harness.Screen;

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task GroupsTheUpdatesByGameInOrderOfName()
    {
        await _harness.StartAsync(
            UpdatesHarness.ModWithUpdates(Smapi, "SMAPI", TestData.Now, "9"),
            UpdatesHarness.ModWithUpdates(SkyUi, "SkyUI", TestData.Now, "52", "53"),
            UpdatesHarness.ModWithUpdates(Address, "Address Library", TestData.Now, "7"));

        Assert.Equal(
            ["Skyrim Special Edition: 3 updates, Update all (3)", "Stardew Valley: 1 update, Update all (1)"],
            Screen.Games.Select(game => $"{game.Name}: {game.CountText}, {game.UpdateAllText}"));
        Assert.Equal([["7", "53", "52"], ["9"]], Screen.Games.Select(game => game.Updates.Select(update => update.File.Id)));
        Assert.Equal(["7", "53", "52", "9"], Screen.Updates.Select(update => update.File.Id));
        Assert.Equal(("Update all (4)", true), (Screen.UpdateAllText, Screen.UpdateAllCommand.CanExecute(null)));
        Assert.Equal(("S", 313), (Screen.Games[0].Initial, Screen.Games[0].CoverHue));
    }

    [Fact]
    public async Task AHiddenGamesUpdatesAreLeftOutAndNotCounted()
    {
        _harness.Settings.Stored = new AppSettings
        {
            Games = new Dictionary<string, GameSettings>(StringComparer.OrdinalIgnoreCase) { ["StardewValley"] = new() { Hidden = true } },
        };

        await _harness.StartAsync(
            UpdatesHarness.ModWithUpdates(Smapi, "SMAPI", TestData.Now, "9"),
            UpdatesHarness.ModWithUpdates(SkyUi, "SkyUI", TestData.Now, "52", "53"));

        Assert.Equal(["Skyrim Special Edition"], Screen.Games.Select(game => game.Name));
        Assert.Equal(["53", "52"], Screen.Updates.Select(update => update.File.Id));
        Assert.Equal((2, "Update all (2)"), (Screen.Badge, Screen.UpdateAllText));
    }

    [Fact]
    public async Task HidingAGameOnTheSettingsScreenLeavesItsUpdatesOutAtOnce()
    {
        await _harness.StartAsync(
            UpdatesHarness.ModWithUpdates(Smapi, "SMAPI", TestData.Now, "9"),
            UpdatesHarness.ModWithUpdates(SkyUi, "SkyUI", TestData.Now, "53"));

        _harness.Hidden.Show(new AppSettings { Games = new Dictionary<string, GameSettings> { ["skyrimspecialedition"] = new() { Hidden = true } } });

        Assert.Equal("Stardew Valley", Assert.Single(Screen.Games).Name);
        Assert.Equal(1, Screen.Badge);

        _harness.Hidden.Show(new AppSettings());

        Assert.Equal(2, Screen.Badge);
    }

    [Fact]
    public async Task AGameItsSiteCantNameGoesByItsKey()
    {
        ModRef plain = new(new GameRef(UpdatesHarness.Plain, "agame"), "1");
        ModRef unknown = TestData.Mod("1", "zgame");

        await _harness.StartAsync(
            UpdatesHarness.ModWithUpdates(plain, "Plain", TestData.Now, "2"),
            UpdatesHarness.ModWithUpdates(unknown, "Unknown", TestData.Now, "2"),
            UpdatesHarness.ModWithUpdates(SkyUi, "SkyUI", TestData.Now, "53"));

        Assert.Equal(["agame", "Skyrim Special Edition", "zgame"], Screen.Games.Select(game => game.Name));
        Assert.Equal(
            ["Information: Couldn't read the name of game zgame from nexus, so it goes by its key: No such game. "],
            _harness.Logger.Entries);
    }

    [Fact]
    public async Task AGameIsNamedOnceAndLeavesWithItsLastUpdate()
    {
        await _harness.StartAsync(UpdatesHarness.ModWithUpdates(Smapi, "SMAPI", TestData.Now, "9"));

        _harness.Library.Change(Smapi, UpdatesHarness.ModWithUpdates(Smapi, "SMAPI", TestData.Now));
        bool left = Screen.Games.Count == 0;
        _harness.Library.Change(Smapi, UpdatesHarness.ModWithUpdates(Smapi, "SMAPI", TestData.Now, "10"));

        Assert.True(left);
        Assert.Equal("Stardew Valley", Assert.Single(Screen.Games).Name);
        Assert.Equal(["GetGameAsync stardewvalley"], _harness.Catalog.Asked);
    }

    [Fact]
    public async Task SaysHowManyUpdatesForHowManyModsAndWhenTheNextCheckIs()
    {
        await _harness.StartAsync(
            UpdatesHarness.ModWithUpdates(SkyUi, "SkyUI", TestData.Now.AddHours(-2), "52", "53"),
            UpdatesHarness.ModWithUpdates(Address, "Address Library", TestData.Now.AddMinutes(-30), "7"),
            UpdatesHarness.ModWithUpdates(Smapi, "SMAPI", TestData.Now.AddMinutes(-10)));

        Assert.Equal("3 updates for 2 mods · checked 2 hours ago, next in 4 hours", Screen.SummaryText);
    }

    [Fact]
    public async Task OneUpdateOfOneModSaysSo()
    {
        await _harness.StartAsync(UpdatesHarness.ModWithUpdates(SkyUi, "SkyUI", null, "53"));

        Assert.Equal("1 update for 1 mod · some watched mods haven't been checked yet", Screen.SummaryText);
    }

    [Fact]
    public async Task TheNextCheckFollowsTheIntervalReadWhenTheScreenIsShown()
    {
        _harness.Settings.Stored = new AppSettings { UpdateInterval = TimeSpan.FromHours(1) };
        await _harness.StartAsync(UpdatesHarness.ModWithUpdates(SkyUi, "SkyUI", TestData.Now.AddHours(-3)));
        string overdue = Screen.SummaryText;
        _harness.Settings.Stored = new AppSettings { UpdateInterval = TimeSpan.FromDays(2) };

        await _harness.ShowAsync();

        Assert.Equal("No updates · checked 3 hours ago, next in a moment", overdue);
        Assert.Equal("No updates · checked 3 hours ago, next in 1 day", Screen.SummaryText);
    }

    [Fact]
    public async Task SettingsThatCantBeReadLeaveTheNextCheckOut()
    {
        _harness.Settings.ReadRefusal = new InvalidDataException("settings.json isn't JSON.");

        await _harness.StartAsync(UpdatesHarness.ModWithUpdates(SkyUi, "SkyUI", TestData.Now.AddHours(-3)));

        Assert.Equal("No updates · checked 3 hours ago", Screen.SummaryText);
    }

    [Fact]
    public async Task UpdateAllDownloadsEveryUpdateOneAfterTheOther()
    {
        await _harness.StartAsync(
            UpdatesHarness.ModWithUpdates(Smapi, "SMAPI", TestData.Now, "9"),
            UpdatesHarness.ModWithUpdates(SkyUi, "SkyUI", TestData.Now, "53"));
        List<(bool, bool, bool)> during = [];
        _harness.Downloads.Answer = link =>
        {
            during.Add((Screen.IsUpdating, Screen.UpdateAllCommand.CanExecute(null), Screen.Games[0].UpdateAllCommand.CanExecute(null)));
            return Queued(link);
        };

        await Screen.UpdateAllCommand.ExecuteAsync(null);

        Assert.Equal(["12604?tab=files&file_id=53", "2400?tab=files&file_id=9"], _harness.Downloads.Added.Select(link => link.Segments[^1] + link.Query));
        Assert.All(during, state => Assert.Equal((true, false, false), state));
        Assert.All(Screen.Updates, update => Assert.StartsWith("Downloading into", update.Note, StringComparison.Ordinal));
        Assert.Equal((false, true), (Screen.IsUpdating, Screen.UpdateAllCommand.CanExecute(null)));
    }

    [Fact]
    public async Task AGamesUpdateAllDownloadsOnlyItsUpdates()
    {
        await _harness.StartAsync(
            UpdatesHarness.ModWithUpdates(Smapi, "SMAPI", TestData.Now, "9"),
            UpdatesHarness.ModWithUpdates(SkyUi, "SkyUI", TestData.Now, "52", "53"));
        _harness.Downloads.Answer = Queued;

        await Screen.Games[1].UpdateAllCommand.ExecuteAsync(null);

        Assert.Equal([new Uri("https://www.nexusmods.com/stardewvalley/mods/2400?tab=files&file_id=9")], _harness.Downloads.Added);
        Assert.Null(_harness.Row("53").Note);
    }

    [Fact]
    public async Task UpdatesWithoutALinkArentCountedOrDownloaded()
    {
        LibraryUpdate linkless = new(new ModFileRef(SkyUi, "54"), "Update 54", "update-54.7z", "54", ModFileCategory.Main, TestData.Now, null, TestData.Now);
        LibraryMod skyUi = UpdatesHarness.ModWithUpdates(SkyUi, "SkyUI", TestData.Now, "53");
        await _harness.StartAsync(skyUi.WithUpdates([.. skyUi.Updates, linkless]));
        _harness.Downloads.Answer = Queued;

        string counted = Screen.UpdateAllText;
        await Screen.UpdateAllCommand.ExecuteAsync(null);
        _harness.Library.Change(SkyUi, skyUi.WithUpdates([linkless]));

        Assert.Equal("Update all (1)", counted);
        Assert.Single(_harness.Downloads.Added);
        Assert.Equal(("Update all (0)", false, false), (Screen.UpdateAllText, Screen.UpdateAllCommand.CanExecute(null), Screen.Games[0].UpdateAllCommand.CanExecute(null)));
    }

    [Fact]
    public async Task AnUpdateThatFailsDoesntStopTheOthers()
    {
        await _harness.StartAsync(
            UpdatesHarness.ModWithUpdates(Address, "Address Library", TestData.Now, "7"),
            UpdatesHarness.ModWithUpdates(SkyUi, "SkyUI", TestData.Now, "53"),
            UpdatesHarness.ModWithUpdates(Smapi, "SMAPI", TestData.Now, "9"));
        _harness.Downloads.Refusals.Enqueue(new InvalidOperationException("Something broke."));
        _harness.Downloads.Refusals.Enqueue(new AuthenticationFailedException());
        _harness.Downloads.Answer = Queued;

        await Screen.UpdateAllCommand.ExecuteAsync(null);

        Assert.Equal(3, _harness.Downloads.Added.Count);
        Assert.Equal(("Something went wrong. The log has the details.", true), (_harness.Row("7").Note, _harness.Row("7").IsNoteAProblem));
        Assert.True(_harness.Row("53").IsNoteAProblem);
        Assert.False(_harness.Row("9").IsNoteAProblem);
        Assert.StartsWith("Error: Download of update 7 of mod 3863 failed System.InvalidOperationException", Assert.Single(_harness.Logger.Entries), StringComparison.Ordinal);
        Assert.Empty(_harness.Dialogs.Messages);
    }

    [Fact]
    public async Task ChangelogsArentReadUntilTheScreenIsShown()
    {
        _harness.Catalog.Changelogs["12604"] = [new ModChangelog("53", ["Fixed the map."])];
        await _harness.StartAsync(UpdatesHarness.ModWithUpdates(SkyUi, "SkyUI", TestData.Now, "53"));
        int before = _harness.ChangelogsAsked.Count;

        await _harness.ShowAsync();

        Assert.Equal(0, before);
        Assert.Equal(["Fixed the map."], _harness.Row("53").Changes);
    }

    [Fact]
    public async Task ChangelogsAreReadFourAtATime()
    {
        LibraryMod[] mods = [.. Enumerable.Range(1, 6).Select(id => UpdatesHarness.ModWithUpdates(TestData.Mod($"{id}"), $"Mod {id}", TestData.Now, "2"))];
        await _harness.StartAsync(mods);
        _harness.Catalog.HoldChangelogs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        Screen.OnShown();
        bool reading = SpinWait.SpinUntil(() => _harness.Catalog.MostChangelogsAtOnce == 4, TimeSpan.FromSeconds(10));
        string waiting = Screen.Updates[^1].ChangesNote;
        _harness.Catalog.HoldChangelogs.SetResult();
        await Screen.ReadingChanges.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.True(reading);
        Assert.Equal("Reading the changelog…", waiting);
        Assert.Equal(4, _harness.Catalog.MostChangelogsAtOnce);
        Assert.Equal(6, _harness.ChangelogsAsked.Count);
        Assert.All(Screen.Updates, update => Assert.Equal("The changelog says nothing about this version.", update.ChangesNote));
    }

    [Fact]
    public async Task TheChangesOfUpdatesFoundLaterAreReadOnceTheScreenWasShown()
    {
        _harness.Catalog.Changelogs["3863"] = [new ModChangelog("7", ["Faster."])];
        await _harness.StartAsync(UpdatesHarness.ModWithUpdates(SkyUi, "SkyUI", TestData.Now, "53"));
        await _harness.ShowAsync();

        _harness.Library.Change(Address, UpdatesHarness.ModWithUpdates(Address, "Address Library", TestData.Now, "7"));
        await Screen.ReadingChanges.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Equal(["Faster."], _harness.Row("7").Changes);
        Assert.Equal(["GetChangelogsAsync 12604", "GetChangelogsAsync 3863"], _harness.ChangelogsAsked);
    }

    private static LibraryDownload Queued(Uri link)
    {
        string folder = Path.Combine(TestData.LibraryRoot, "mods");
        return new LibraryDownload(Path.Combine(folder, "file.7z"), new LinkDownload(TestData.Job("file.7z", folder: folder), AlreadyQueued: false));
    }
}
