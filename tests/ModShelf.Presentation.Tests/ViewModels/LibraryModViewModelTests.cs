using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;
using ModShelf.Presentation.ViewModels;

namespace ModShelf.Presentation.Tests.ViewModels;

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
    public async Task ListsItsFilesTheLatestDownloadedFirstAndKeepsTheirRows()
    {
        LibraryFile older = TestData.LibraryFile(SkyUi, "1", "older.7z", daysAgo: 9);
        LibraryFile newer = TestData.LibraryFile(SkyUi, "2", "newer.7z", daysAgo: 2);
        LibraryModViewModel row = await ShowAsync(TestData.LibraryMod(SkyUi, "SkyUI", watched: true, older, newer));
        LibraryFileViewModel olderRow = row.Files[1];

        LibraryFile newest = TestData.LibraryFile(SkyUi, "3", "newest.7z", daysAgo: 0);
        _harness.Library.Change(SkyUi, TestData.LibraryMod(SkyUi, "SkyUI", watched: true, older, newest));

        Assert.Equal(["newest.7z", "older.7z"], row.Files.Select(file => file.FileName));
        Assert.Same(olderRow, row.Files[1]);
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

        Assert.Empty(_harness.Screen.Rows);
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
