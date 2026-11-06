using ModShelf.Application.Library;
using ModShelf.Application.Settings;
using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;
using ModShelf.Presentation.ViewModels.Library;

namespace ModShelf.Presentation.Tests.ViewModels.Library;

public sealed class LibraryModViewModelTests : IDisposable
{
    private static readonly ModRef SkyUi = TestData.Mod("12604");

    private readonly LibraryHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task ShowsTheModAndWhatTheLibraryHasOfIt()
    {
        LibraryMod mod = TestData.LibraryMod(SkyUi, "SkyUI", watched: true, TestData.LibraryFile(SkyUi, "1", "a.7z"), TestData.LibraryFile(SkyUi, "2", "b.7z"));

        LibraryModViewModel row = await ShowAsync(mod);

        Assert.Equal(("SkyUI", "by An Author", true, "Watched for updates"), (row.Name, row.AuthorText, row.IsWatched, row.WatchedText));
        Assert.Equal(("2 files", true, string.Empty, false, true), (row.FilesText, row.HasFiles, row.UpdatesText, row.HasUpdates, row.HasPage));
    }

    [Fact]
    public async Task SaysHowManyFilesAndUpdatesThereAre()
    {
        LibraryFile file = TestData.LibraryFile(SkyUi, "1", "a.7z");
        LibraryMod oneEach = TestData.LibraryMod(SkyUi, "SkyUI", watched: true, file).WithUpdates([Update("2")]);
        LibraryModViewModel row = await ShowAsync(oneEach);
        (string, string, bool) one = (row.FilesText, row.UpdatesText, row.HasUpdates);

        _harness.Library.Change(SkyUi, TestData.LibraryMod(SkyUi, "SkyUI", watched: true, file).WithUpdates([Update("2"), Update("3")]));
        string two = row.UpdatesText;
        _harness.Library.Change(SkyUi, TestData.LibraryMod(SkyUi, "SkyUI", watched: true));

        Assert.Equal(("1 file", "1 update", true), one);
        Assert.Equal("2 updates", two);
        Assert.Equal(("No files", false, string.Empty, false), (row.FilesText, row.HasFiles, row.UpdatesText, row.HasUpdates));
    }

    [Fact]
    public async Task AModWithoutAnAuthorOrAPageSaysNeither()
    {
        LibraryMod mod = LibraryMod.Restore(SkyUi, "SkyUI", null, null, isWatched: false, TestData.Now, null, [TestData.LibraryFile(SkyUi, "1", "a.7z")]);

        LibraryModViewModel row = await ShowAsync(mod);

        Assert.Equal((string.Empty, false, "Not watched"), (row.AuthorText, row.HasPage, row.WatchedText));
        Assert.False(row.OpenPageCommand.CanExecute(null));
    }

    [Fact]
    public async Task FollowsItsChangesInTheSameRow()
    {
        LibraryFile older = TestData.LibraryFile(SkyUi, "1", "older.7z", "1.0", daysAgo: 9);
        LibraryModViewModel row = await ShowAsync(TestData.LibraryMod(SkyUi, "SkyUI", watched: true, older));

        LibraryFile newer = TestData.LibraryFile(SkyUi, "2", "newer.7z", "2.0", daysAgo: 0);
        _harness.Library.Change(SkyUi, TestData.LibraryMod(SkyUi, "SkyUI", watched: true, older, newer));

        Assert.Same(row, _harness.Row(TestData.LibraryMod(SkyUi, "SkyUI")));
        Assert.Equal(("2.0", "2 files"), (row.VersionText, row.FilesText));
    }

    [Theory]
    [InlineData(false, "Watch 12604", true)]
    [InlineData(true, "Unwatch 12604", false)]
    public async Task WatchesAndUnwatches(bool watched, string asked, bool after)
    {
        LibraryModViewModel row = await ShowAsync(TestData.LibraryMod(SkyUi, "SkyUI", watched, TestData.LibraryFile(SkyUi, "1", "a.7z")));
        (bool, bool) could = (row.WatchCommand.CanExecute(null), row.UnwatchCommand.CanExecute(null));

        await (watched ? row.UnwatchCommand : row.WatchCommand).ExecuteAsync(null);

        Assert.Equal((!watched, watched), could);
        Assert.Equal([asked], _harness.Library.Asked);
        Assert.Equal(after, row.IsWatched);
        Assert.Equal((!after, after), (row.WatchCommand.CanExecute(null), row.UnwatchCommand.CanExecute(null)));
    }

    [Fact]
    public async Task UnwatchingAModWithoutFilesTakesItOffTheScreen()
    {
        LibraryModViewModel row = await ShowAsync(TestData.LibraryMod(SkyUi, "SkyUI"));

        await row.UnwatchCommand.ExecuteAsync(null);

        Assert.Empty(_harness.Screen.Games);
    }

    [Fact]
    public async Task RemovesTheModFromTheLibraryAtOnceOfferingToUndoIt()
    {
        LibraryModViewModel row = await ShowAsync(TestData.LibraryMod(SkyUi, "SkyUI", watched: true, TestData.LibraryFile(SkyUi, "1", "a.7z")));

        await row.RemoveCommand.ExecuteAsync(null);

        Assert.Empty(_harness.Dialogs.Confirmations);
        Assert.Equal(["Forget 12604"], _harness.Library.Asked);
        Assert.Empty(_harness.Screen.Games);
        Assert.Equal((true, true, "SkyUI removed from the library; its files stay in its folder."), (_harness.Undo.IsShown, _harness.Undo.CanUndo, _harness.Undo.Message));
    }

    [Fact]
    public async Task UndoingTheRemovalBringsTheModBack()
    {
        LibraryMod kept = TestData.LibraryMod(SkyUi, "SkyUI", watched: true, TestData.LibraryFile(SkyUi, "1", "a.7z"));
        LibraryModViewModel row = await ShowAsync(kept);
        await row.RemoveCommand.ExecuteAsync(null);

        await _harness.Undo.UndoCommand.ExecuteAsync(null);

        Assert.Equal(["Forget 12604", "Restore 12604"], _harness.Library.Asked);
        Assert.Same(kept, Assert.Single(_harness.Library.Mods));
        Assert.Equal(["SkyUI"], _harness.Lines().Skip(1));
        Assert.False(_harness.Undo.IsShown);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PinningAndUnpinningOfferToUndoIt(bool pinned)
    {
        LibraryModViewModel row = await ShowAsync(TestData.LibraryMod(SkyUi, "SkyUI").WithPinned(!pinned));

        await (pinned ? row.PinCommand : row.UnpinCommand).ExecuteAsync(null);

        Assert.Equal(pinned ? "SkyUI pinned." : "SkyUI unpinned.", _harness.Undo.Message);
        await _harness.Undo.UndoCommand.ExecuteAsync(null);
        Assert.Equal(!pinned, Assert.Single(_harness.Library.Mods).IsPinned);
    }

    [Fact]
    public async Task UnwatchingOffersToUndoItAndWatchingDoesnt()
    {
        LibraryModViewModel row = await ShowAsync(TestData.LibraryMod(SkyUi, "SkyUI", watched: true, TestData.LibraryFile(SkyUi, "1", "a.7z")));

        await row.UnwatchCommand.ExecuteAsync(null);

        Assert.Equal((true, "SkyUI is no longer watched."), (_harness.Undo.IsShown, _harness.Undo.Message));
        _harness.Undo.CloseCommand.Execute(null);
        await row.WatchCommand.ExecuteAsync(null);
        Assert.False(_harness.Undo.IsShown);
    }

    [Fact]
    public async Task RemovingANoteOffersToUndoItAndChangingOneDoesnt()
    {
        LibraryModViewModel row = await ShowAsync(TestData.LibraryMod(SkyUi, "SkyUI").WithNote("Keep 5.2."));
        _harness.Dialogs.Answers.Enqueue("Keep 5.3.");
        await row.EditNoteCommand.ExecuteAsync(null);
        Assert.False(_harness.Undo.IsShown);

        _harness.Dialogs.Answers.Enqueue("  ");
        await row.EditNoteCommand.ExecuteAsync(null);

        Assert.Equal("Note removed from SkyUI.", _harness.Undo.Message);
        await _harness.Undo.UndoCommand.ExecuteAsync(null);
        Assert.Equal("Keep 5.3.", Assert.Single(_harness.Library.Mods).Note);
    }

    [Theory]
    [InlineData(typeof(InvalidOperationException), "A download of a file of SkyUI into the library isn't finished. Let it finish, or cancel it, first.")]
    [InlineData(typeof(InvalidDataException), "settings.json isn't valid JSON.")]
    public async Task AModThatCantBeRemovedSaysWhy(Type failure, string reason)
    {
        LibraryModViewModel row = await ShowAsync(TestData.LibraryMod(SkyUi, "SkyUI"));
        _harness.Library.Refusal = (Exception)Activator.CreateInstance(failure, reason)!;

        await row.RemoveCommand.ExecuteAsync(null);

        Assert.Equal([("The mod wasn't removed", reason)], _harness.Dialogs.Messages);
        Assert.Empty(_harness.Logger.Entries);
    }

    [Fact]
    public async Task AModRemovedMeanwhileIsLeftAlone()
    {
        LibraryModViewModel row = await ShowAsync(TestData.LibraryMod(SkyUi, "SkyUI"));
        _harness.Library.Refusal = new KeyNotFoundException();

        await row.RemoveCommand.ExecuteAsync(null);

        Assert.Empty(_harness.Dialogs.Messages);
        Assert.Empty(_harness.Logger.Entries);
    }

    [Fact]
    public async Task ARemoveThatFailsUnexpectedlyIsLoggedAndTold()
    {
        LibraryModViewModel row = await ShowAsync(TestData.LibraryMod(SkyUi, "SkyUI"));
        _harness.Library.Refusal = new IOException("The disk is full.");

        await row.RemoveCommand.ExecuteAsync(null);

        Assert.Equal([("That didn't work", "Something went wrong. The log has the details.")], _harness.Dialogs.Messages);
        Assert.StartsWith(
            "Error: Remove of mod 12604 of skyrimspecialedition failed System.IO.IOException: The disk is full.",
            Assert.Single(_harness.Logger.Entries),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task RescansTheModByItsPageAndSaysWhatItFound()
    {
        LibraryModViewModel row = await ShowAsync(TestData.LibraryMod(SkyUi, "SkyUI"));
        _harness.Rescan.Result = new LibraryRescan(1, [TestData.LibraryFile(SkyUi, "2", "b.7z")], [], [], [], []);

        await row.RescanCommand.ExecuteAsync(null);

        Assert.Equal([new Uri("https://www.nexusmods.com/skyrimspecialedition/mods/12604")], _harness.Rescan.Asked);
        Assert.Equal([("Rescan finished", "1 file found on disk was added to the library.")], _harness.Dialogs.Messages);
    }

    [Fact]
    public async Task AModWithoutAPageCantBeRescanned()
    {
        LibraryModViewModel row = await ShowAsync(LibraryMod.Restore(SkyUi, "SkyUI", null, null, isWatched: true, TestData.Now, null, []));

        Assert.False(row.RescanCommand.CanExecute(null));
    }

    [Fact]
    public async Task ARescanThatCantRunSaysWhy()
    {
        LibraryModViewModel row = await ShowAsync(TestData.LibraryMod(SkyUi, "SkyUI"));
        _harness.Rescan.Refusal = new InvalidDataException("settings.json isn't valid JSON.");

        await row.RescanCommand.ExecuteAsync(null);

        Assert.Equal([("The library wasn't rescanned", "settings.json isn't valid JSON.")], _harness.Dialogs.Messages);
        Assert.Empty(_harness.Logger.Entries);
    }

    [Fact]
    public async Task AModThatLeftMeanwhileIsLeftAlone()
    {
        LibraryModViewModel row = await ShowAsync(TestData.LibraryMod(SkyUi, "SkyUI"));
        _harness.Library.Refusal = new KeyNotFoundException();

        await row.UnwatchCommand.ExecuteAsync(null);

        Assert.Empty(_harness.Dialogs.Messages);
        Assert.Empty(_harness.Logger.Entries);
    }

    [Fact]
    public async Task WhatFailsUnexpectedlyIsLoggedAndTold()
    {
        LibraryModViewModel row = await ShowAsync(TestData.LibraryMod(SkyUi, "SkyUI"));
        _harness.Library.Refusal = new InvalidOperationException("The database is locked.");

        await row.UnwatchCommand.ExecuteAsync(null);

        Assert.Equal([("That didn't work", "Something went wrong. The log has the details.")], _harness.Dialogs.Messages);
        Assert.StartsWith(
            "Error: Unwatch of mod 12604 of skyrimspecialedition failed System.InvalidOperationException: The database is locked.",
            Assert.Single(_harness.Logger.Entries),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task OpensItsPage()
    {
        LibraryModViewModel row = await ShowAsync(TestData.LibraryMod(SkyUi, "SkyUI"));

        await row.OpenPageCommand.ExecuteAsync(null);

        Assert.Equal([new Uri("https://www.nexusmods.com/skyrimspecialedition/mods/12604")], _harness.Browser.Opened);
        Assert.Empty(_harness.Dialogs.Messages);
    }

    [Fact]
    public async Task WhenNoBrowserOpensThePagesAddressIsShown()
    {
        LibraryModViewModel row = await ShowAsync(TestData.LibraryMod(SkyUi, "SkyUI"));
        _harness.Browser.CanOpen = false;

        await row.OpenPageCommand.ExecuteAsync(null);

        Assert.Equal(
            [("Couldn't open the browser", "Open this page in your browser: https://www.nexusmods.com/skyrimspecialedition/mods/12604")],
            _harness.Dialogs.Messages);
    }

    [Fact]
    public async Task OpensTheFolderOfTheFileDownloadedLast()
    {
        LibraryFile older = new(new ModFileRef(SkyUi, "1"), "Older", "1.0", ModFileCategory.Main, TestData.Now, "elsewhere/older.7z", 1, TestData.Now.AddDays(-9));
        LibraryFile newer = TestData.LibraryFile(SkyUi, "2", "newer.7z", daysAgo: 2);
        LibraryModViewModel row = await ShowAsync(TestData.LibraryMod(SkyUi, "SkyUI", watched: true, older, newer));

        await row.OpenFolderCommand.ExecuteAsync(null);

        Assert.Equal([Path.Combine(TestData.LibraryRoot, "nexus", "skyrimspecialedition", "Mod (12604)")], _harness.Folders.Opened);
        Assert.Empty(_harness.Dialogs.Messages);
    }

    [Fact]
    public async Task OpensTheFolderInTheGamesOwnFolder()
    {
        string skyrim = Path.Combine(Path.GetTempPath(), "ModShelf.Tests", "Skyrim");
        _harness.Settings.Stored = new AppSettings
        {
            Games = new Dictionary<GameRef, GameSettings>(AppSettings.GameComparer) { [SkyUi.Game] = new() { Root = skyrim } },
        };
        LibraryModViewModel row = await ShowAsync(TestData.LibraryMod(SkyUi, "SkyUI", watched: true, TestData.LibraryFile(SkyUi, "1", "a.7z")));

        await row.OpenFolderCommand.ExecuteAsync(null);

        Assert.Equal([Path.Combine(skyrim, "nexus", "skyrimspecialedition", "Mod (12604)")], _harness.Folders.Opened);
    }

    [Fact]
    public async Task AFolderThatDoesntOpenIsToldOf()
    {
        LibraryModViewModel row = await ShowAsync(TestData.LibraryMod(SkyUi, "SkyUI", watched: true, TestData.LibraryFile(SkyUi, "1", "a.7z")));
        _harness.Folders.CanOpen = false;

        await row.OpenFolderCommand.ExecuteAsync(null);

        string folder = Path.Combine(TestData.LibraryRoot, "nexus", "skyrimspecialedition", "Mod (12604)");
        Assert.Equal([("Couldn't open the folder", $"{folder} couldn't be opened. It may have been moved or deleted.")], _harness.Dialogs.Messages);
    }

    [Fact]
    public async Task SettingsThatCantBeReadAreToldOf()
    {
        LibraryModViewModel row = await ShowAsync(TestData.LibraryMod(SkyUi, "SkyUI", watched: true, TestData.LibraryFile(SkyUi, "1", "a.7z")));
        _harness.Settings.Refusal = new InvalidDataException("settings.json isn't valid JSON.");

        await row.OpenFolderCommand.ExecuteAsync(null);

        Assert.Equal([("Couldn't open the folder", "settings.json isn't valid JSON.")], _harness.Dialogs.Messages);
        Assert.Empty(_harness.Folders.Opened);
    }

    [Fact]
    public async Task AModWithoutFilesHasNoFolderToOpen()
    {
        LibraryModViewModel row = await ShowAsync(TestData.LibraryMod(SkyUi, "SkyUI"));

        Assert.False(row.OpenFolderCommand.CanExecute(null));
    }

    private static LibraryUpdate Update(string id)
        => new(new ModFileRef(SkyUi, id), $"SkyUI {id}", $"skyui-{id}.7z", id, ModFileCategory.Main, TestData.Now, new ModFileRef(SkyUi, "1"), TestData.Now, null);

    private async Task<LibraryModViewModel> ShowAsync(LibraryMod mod)
    {
        await _harness.StartAsync(mod);
        return _harness.Row(mod);
    }
}
