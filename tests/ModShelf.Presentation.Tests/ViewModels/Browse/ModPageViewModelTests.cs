using System.Net;

using ModShelf.Application.Catalog;
using ModShelf.Application.Downloads;
using ModShelf.Application.Library;
using ModShelf.Domain.Downloads;
using ModShelf.Domain.Mods;
using ModShelf.Presentation.Tests.Common;
using ModShelf.Presentation.ViewModels.Browse;
using ModShelf.Sources.Abstractions.Accounts;
using ModShelf.Sources.Abstractions.Catalog;

namespace ModShelf.Presentation.Tests.ViewModels.Browse;

public sealed class ModPageViewModelTests : IDisposable
{
    private static readonly ModRef SkyUi = TestData.Mod("12604");

    private readonly BrowseHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task ShowsTheModAsItsSiteDescribesIt()
    {
        using CultureScope culture = new("en-US");
        await _harness.StartAsync();

        ModPageViewModel mod = await _harness.OpenAsync(BrowseHarness.Page(BrowseHarness.Info(SkyUi, "SkyUI")));

        Assert.Equal(
            ("SkyUI", "by An Author", "Skyrim Special Edition", "All about SkyUI.", "Version 1.2", "Updated 10/24/2026", "2,134,567 downloads", "1 endorsement"),
            (mod.Name, mod.AuthorText, mod.GameName, mod.Summary, mod.VersionText, mod.UpdatedText, mod.DownloadsText, mod.EndorsementsText));
        Assert.Equal((false, false, true), (mod.HasAdultContent, mod.IsUnavailable, mod.HasPage));
        ModChangelog version = Assert.Single(mod.Changelog);
        Assert.Equal("1.2", version.Version);
        Assert.Equal(["Fixed the menus.", "Faster."], version.Changes);
        Assert.True(mod.HasChangelog);
    }

    [Fact]
    public async Task ListsTheCurrentSectionsNewestFirstAndTheRestWhenAsked()
    {
        await _harness.StartAsync();
        ModPage page = BrowseHarness.Page(
            BrowseHarness.Info(SkyUi, "SkyUI"),
            BrowseHarness.File(SkyUi, "1", daysAgo: 30),
            BrowseHarness.File(SkyUi, "2", ModFileCategory.Optional),
            BrowseHarness.File(SkyUi, "3", daysAgo: 2),
            BrowseHarness.File(SkyUi, "4", ModFileCategory.OldVersion),
            BrowseHarness.File(SkyUi, "5", ModFileCategory.Archived),
            BrowseHarness.File(SkyUi, "6", ModFileCategory.Unknown));

        ModPageViewModel mod = await _harness.OpenAsync(page);
        IReadOnlyList<string> before = Sections(mod);
        string moreText = mod.MoreFilesText;
        mod.ToggleMoreFilesCommand.Execute(null);

        Assert.Equal(["Main files: 3 1", "Optional files: 2"], before);
        Assert.Equal("Show 3 more files: old versions, archived, or in no section", moreText);
        Assert.Equal(["Main files: 3 1", "Optional files: 2", "Old versions: 4", "Archived files: 5", "Files in no section: 6"], Sections(mod));
        Assert.Equal("Hide old versions, archived files and files in no section", mod.MoreFilesText);
        Assert.True(mod.HasMoreFiles);
        Assert.True(mod.HasFiles);
    }

    [Fact]
    public async Task TheSectionOfTheLinksFileShowsEvenWhenOld()
    {
        await _harness.StartAsync();
        ModPage page = BrowseHarness.Page(
            BrowseHarness.Info(SkyUi, "SkyUI"),
            BrowseHarness.File(SkyUi, "1"),
            BrowseHarness.File(SkyUi, "4", ModFileCategory.OldVersion),
            BrowseHarness.File(SkyUi, "5", ModFileCategory.Archived)) with
        {
            LinkedFile = new ModFileRef(SkyUi, "4"),
        };

        ModPageViewModel mod = await _harness.OpenAsync(page);

        Assert.Equal(["Main files: 1", "Old versions: 4"], Sections(mod));
        Assert.Equal([false, true], mod.Files.Take(2).Select(file => file.IsLinked));
        Assert.Equal("Show 1 more file: an old version, archived, or in no section", mod.MoreFilesText);
    }

    [Fact]
    public async Task AModWithoutFilesSaysSo()
    {
        await _harness.StartAsync();

        ModPageViewModel mod = await _harness.OpenAsync(BrowseHarness.Page(BrowseHarness.Info(SkyUi, "SkyUI")));

        Assert.False(mod.HasFiles);
        Assert.False(mod.HasMoreFiles);
        Assert.Empty(mod.Sections);
    }

    [Fact]
    public async Task ShowsWhatTheLibraryHasAndFollowsItsChanges()
    {
        await _harness.StartAsync(TestData.LibraryMod(SkyUi, "SkyUI", watched: true, TestData.LibraryFile(SkyUi, "1", "file-1.7z")));
        ModPageViewModel mod = await _harness.OpenAsync(BrowseHarness.Page(BrowseHarness.Info(SkyUi, "SkyUI"), BrowseHarness.File(SkyUi, "1", daysAgo: 1), BrowseHarness.File(SkyUi, "2", daysAgo: 2)));
        (bool, bool, bool[]) before = (mod.IsDownloaded, mod.IsWatched, [.. mod.Files.Select(file => file.IsInLibrary)]);

        _harness.Library.Change(SkyUi, TestData.LibraryMod(SkyUi, "SkyUI", watched: false, TestData.LibraryFile(SkyUi, "2", "file-2.7z")));

        Assert.Equal((true, true), (before.Item1, before.Item2));
        Assert.Equal([true, false], before.Item3);
        Assert.Equal((true, false), (mod.IsDownloaded, mod.IsWatched));
        Assert.Equal([false, true], mod.Files.Select(file => file.IsInLibrary));
    }

    [Fact]
    public async Task DownloadsAFileIntoTheLibrary()
    {
        await _harness.StartAsync();
        string path = Path.Combine(TestData.LibraryRoot, "SkyUI", "file-1.7z");
        _harness.LibraryDownloads.Answer = _ => new LibraryDownload(path, new LinkDownload(TestData.Job("file-1.7z"), AlreadyQueued: false));
        ModFileViewModel file = await OpenFileAsync();

        await file.DownloadCommand.ExecuteAsync(null);

        Assert.Equal([new Uri("https://www.nexusmods.com/skyrimspecialedition/mods/12604?tab=files&file_id=1")], _harness.LibraryDownloads.Added);
        Assert.Equal($"Downloading into {Path.GetDirectoryName(path)}. It shows on the Downloads screen.", file.Note);
        Assert.False(file.IsNoteAProblem);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task SaysWhenAFileIsQueuedOrInTheLibraryAlready(bool queued, bool alreadyQueued)
    {
        await _harness.StartAsync();
        string path = Path.Combine(TestData.LibraryRoot, "file-1.7z");
        _harness.LibraryDownloads.Answer = _ => new LibraryDownload(path, queued ? new LinkDownload(TestData.Job("file-1.7z"), alreadyQueued) : null);
        ModFileViewModel file = await OpenFileAsync();

        await file.DownloadCommand.ExecuteAsync(null);

        Assert.Equal(queued ? "It's on the Downloads screen already." : $"{path} is in the library already.", file.Note);
    }

    [Fact]
    public async Task ADownloadTheSiteRefusesSaysWhy()
    {
        await _harness.StartAsync();
        _harness.LibraryDownloads.Refusals.Enqueue(new AuthenticationFailedException("No account is linked."));
        ModFileViewModel file = await OpenFileAsync();

        await file.DownloadCommand.ExecuteAsync(null);

        Assert.Equal("The site needs a linked account to look that link up, or refused the one linked. Link it on the Accounts screen, then try again.", file.Note);
        Assert.True(file.IsNoteAProblem);
    }

    [Fact]
    public async Task SavesAFileToAFolderThePickerGives()
    {
        await _harness.StartAsync();
        _harness.Dialogs.Folders.Enqueue(TestData.Folder);
        _harness.Links.Answer = (_, folder, _) => new LinkDownload(TestData.Job("file-1.7z", folder: folder), AlreadyQueued: false);
        ModFileViewModel file = await OpenFileAsync();

        await file.SaveToFolderCommand.ExecuteAsync(null);
        string? saved = file.Note;
        await file.SaveToFolderCommand.ExecuteAsync(null);

        Assert.Equal($"Downloading file-1.7z into {TestData.Folder}.", saved);
        Assert.Single(_harness.Links.Added);
        Assert.Equal(2, _harness.Dialogs.FolderPickers.Count);
    }

    [Fact]
    public async Task SavingAFileSavedThereAlreadySaysSo()
    {
        await _harness.StartAsync();
        _harness.Dialogs.Folders.Enqueue(TestData.Folder);
        _harness.Links.Answer = (_, folder, _) => new LinkDownload(TestData.Job("file-1.7z", folder: folder), AlreadyQueued: false, AlreadySaved: true);
        ModFileViewModel file = await OpenFileAsync();

        await file.SaveToFolderCommand.ExecuteAsync(null);

        Assert.Equal($"file-1.7z is saved in {TestData.Folder} already.", file.Note);
    }

    [Fact]
    public async Task FilesOfAHiddenModOrWithoutALinkCantBeDownloaded()
    {
        await _harness.StartAsync();
        ModPageViewModel hidden = await _harness.OpenAsync(BrowseHarness.Page(BrowseHarness.Info(SkyUi, "SkyUI", isAvailable: false), BrowseHarness.File(SkyUi, "1")));
        ModRef other = TestData.Mod("3863");
        ModPageViewModel linkless = await _harness.OpenAsync(BrowseHarness.Page(BrowseHarness.Info(other, "Other"), BrowseHarness.File(other, "1", hasPage: false)));

        Assert.True(hidden.IsUnavailable);
        Assert.All(
            [hidden.Files[0], linkless.Files[0]],
            file => Assert.Equal((false, false, false), (file.CanDownload, file.DownloadCommand.CanExecute(null), file.SaveToFolderCommand.CanExecute(null))));
    }

    [Fact]
    public async Task WatchingAModNotInTheLibraryAddsItWithItsDetailsFromItsSite()
    {
        await _harness.StartAsync();
        _harness.Library.SiteNames["12604"] = "SkyUI";
        ModPageViewModel mod = await _harness.OpenAsync(BrowseHarness.Page(BrowseHarness.Info(SkyUi, "SkyUI")));

        await mod.ToggleWatchCommand.ExecuteAsync(null);

        Assert.Equal(["Watch from the site 12604"], _harness.Library.Asked);
        Assert.True(mod.IsWatched);
        Assert.Equal("Watched for updates, in the library.", mod.Note);
        Assert.Equal("SkyUI", Assert.Single(_harness.Library.Mods).Name);
    }

    [Fact]
    public async Task UnwatchingAModStopsWatchingItInTheLibrary()
    {
        await _harness.StartAsync(TestData.LibraryMod(SkyUi, "SkyUI", watched: true, TestData.LibraryFile(SkyUi, "1", "a.7z")));
        ModPageViewModel mod = await _harness.OpenAsync(BrowseHarness.Page(BrowseHarness.Info(SkyUi, "SkyUI")));

        await mod.ToggleWatchCommand.ExecuteAsync(null);

        Assert.Equal(["Unwatch 12604"], _harness.Library.Asked);
        Assert.Equal((false, true), (mod.IsWatched, mod.IsDownloaded));
        Assert.Equal("No longer watched for updates.", mod.Note);
    }

    [Fact]
    public async Task WatchingAModTheSiteWontDescribeSaysWhy()
    {
        await _harness.StartAsync();
        ModPageViewModel mod = await _harness.OpenAsync(BrowseHarness.Page(BrowseHarness.Info(SkyUi, "SkyUI")));
        _harness.Library.Refusal = new HttpRequestException("Service unavailable.", null, HttpStatusCode.ServiceUnavailable);

        await mod.ToggleWatchCommand.ExecuteAsync(null);

        Assert.Equal("Nexus Mods couldn't give the mod's details to watch it: Service unavailable.", mod.Note);
        Assert.True(mod.IsNoteAProblem);
        Assert.False(mod.IsWatched);
    }

    [Fact]
    public async Task ShowsWhetherTheModIsTrackedReadingTheSitesListOnce()
    {
        _harness.Watchlists.Tracked.Add(SkyUi);
        await _harness.StartAsync();

        ModPageViewModel tracked = await _harness.OpenAsync(BrowseHarness.Page(BrowseHarness.Info(SkyUi, "SkyUI")));
        ModPageViewModel other = await _harness.OpenAsync(BrowseHarness.Page(BrowseHarness.Info(TestData.Mod("3863"), "Other")));

        Assert.Equal((true, true, false), (tracked.CanTrack, tracked.IsTracked, other.IsTracked));
        Assert.Equal(("Track on Nexus Mods", "Untrack on Nexus Mods"), (tracked.TrackText, tracked.UntrackText));
        Assert.Equal(["GetWatchedAsync nexus"], _harness.Watchlists.Asked);
    }

    [Fact]
    public async Task TracksAndUntracksTheModOnItsSite()
    {
        await _harness.StartAsync();
        ModPageViewModel mod = await _harness.OpenAsync(BrowseHarness.Page(BrowseHarness.Info(SkyUi, "SkyUI")));

        await mod.ToggleTrackCommand.ExecuteAsync(null);
        (bool, string?) tracking = (mod.IsTracked, mod.Note);
        ModPageViewModel again = await _harness.OpenAsync(BrowseHarness.Page(BrowseHarness.Info(SkyUi, "SkyUI")));
        await again.ToggleTrackCommand.ExecuteAsync(null);

        Assert.Equal((true, "Tracked on Nexus Mods."), tracking);
        Assert.Equal((false, "No longer tracked on Nexus Mods."), (again.IsTracked, again.Note));
        Assert.Equal(["GetWatchedAsync nexus", "WatchAsync 12604", "UnwatchAsync 12604"], _harness.Watchlists.Asked);
    }

    [Fact]
    public async Task TrackingWithoutAnAccountSaysSo()
    {
        await _harness.StartAsync();
        ModPageViewModel mod = await _harness.OpenAsync(BrowseHarness.Page(BrowseHarness.Info(SkyUi, "SkyUI")));
        _harness.Watchlists.Refusals["WatchAsync"] = new AuthenticationFailedException("No account is linked.");

        await mod.ToggleTrackCommand.ExecuteAsync(null);

        Assert.Equal("Nexus Mods needs your account linked, or refused the one linked. Link it on the Accounts screen, then try again.", mod.Note);
        Assert.Equal((true, false), (mod.IsNoteAProblem, mod.IsTracked));
    }

    [Fact]
    public async Task ATrackedListThatCantBeReadIsLoggedAndTrackingIsStillOffered()
    {
        _harness.Watchlists.Refusals["GetWatchedAsync"] = new AuthenticationFailedException("No account is linked.");
        await _harness.StartAsync();

        ModPageViewModel mod = await _harness.OpenAsync(BrowseHarness.Page(BrowseHarness.Info(SkyUi, "SkyUI")));

        Assert.Equal((true, false), (mod.CanTrack, mod.IsTracked));
        Assert.Equal(
            ["Information: Couldn't read the tracked mods on nexus, so whether a mod is tracked isn't known: No account is linked. "],
            _harness.Logger.Entries);
        Assert.Null(_harness.Screen.Problem);
    }

    [Fact]
    public async Task AModOfASiteWithoutTrackedModsOffersNoTracking()
    {
        _harness.Watchlists.Tracking.Clear();
        await _harness.StartAsync();

        ModPageViewModel mod = await _harness.OpenAsync(BrowseHarness.Page(BrowseHarness.Info(SkyUi, "SkyUI")));

        Assert.False(mod.CanTrack);
        Assert.False(mod.ToggleTrackCommand.CanExecute(null));
        Assert.Empty(_harness.Watchlists.Asked);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OpensTheModsPageOrSaysItsAddress(bool canOpen)
    {
        await _harness.StartAsync();
        ModPageViewModel mod = await _harness.OpenAsync(BrowseHarness.Page(BrowseHarness.Info(SkyUi, "SkyUI")));
        _harness.Browser.CanOpen = canOpen;

        await mod.OpenPageCommand.ExecuteAsync(null);

        Assert.Equal([TestData.ModPage], _harness.Browser.Opened);
        Assert.Equal(canOpen ? 0 : 1, _harness.Dialogs.Messages.Count);
    }

    private static IReadOnlyList<string> Sections(ModPageViewModel mod)
        => [.. mod.Sections.Select(section => $"{section.Title}: {string.Join(' ', section.Files.Select(file => file.File.Id))}")];

    private async Task<ModFileViewModel> OpenFileAsync()
    {
        ModPageViewModel mod = await _harness.OpenAsync(BrowseHarness.Page(BrowseHarness.Info(SkyUi, "SkyUI"), BrowseHarness.File(SkyUi, "1")));
        return mod.Files[0];
    }
}
