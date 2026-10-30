using System.Net;

using ModShelf.Application.Downloads;
using ModShelf.Application.Library;
using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;
using ModShelf.Presentation.Tests.Common;
using ModShelf.Presentation.ViewModels.Updates;
using ModShelf.Sources.Abstractions.Accounts;
using ModShelf.Sources.Abstractions.Catalog;

namespace ModShelf.Presentation.Tests.ViewModels.Updates;

public sealed class UpdateViewModelTests : IDisposable
{
    private static readonly ModRef SkyUi = TestData.Mod("12604");

    private readonly UpdatesHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task ShowsTheUpdateAndTheFileItReplaces()
    {
        using CultureScope culture = new("en-US");

        UpdateViewModel row = await ShowAsync("53");

        Assert.Equal(("SkyUI", "Update 53", "update-53.7z", "Version 53"), (row.ModName, row.Name, row.FileName, row.VersionText));
        Assert.Equal(("Uploaded 10/26/2026", "Replaces SkyUI-1.7z"), (row.UploadedText, row.ReplacesText));
        Assert.True(row.DownloadCommand.CanExecute(null));
        Assert.Null(row.Note);
    }

    [Fact]
    public async Task AnUpdateOfAFileTheLibraryHasntAndWithoutAVersionOrLinkSaysNeither()
    {
        LibraryUpdate update = new(new ModFileRef(SkyUi, "53"), "Update 53", "update-53.7z", null, ModFileCategory.Main, TestData.Now, null, TestData.Now);
        LibraryMod mod = TestData.LibraryMod(SkyUi, "SkyUI", watched: true, TestData.LibraryFile(SkyUi, "1", "a.7z")).WithUpdates([update]);
        await _harness.StartAsync(mod);

        UpdateViewModel row = _harness.Row("53");

        Assert.Equal((string.Empty, string.Empty), (row.VersionText, row.ReplacesText));
        Assert.False(row.DownloadCommand.CanExecute(null));
    }

    [Fact]
    public async Task DownloadsTheUpdateIntoTheLibrary()
    {
        UpdateViewModel row = await ShowAsync("53");
        string folder = Path.Combine(TestData.LibraryRoot, "nexus", "skyrimspecialedition", "SkyUI (12604)");
        _harness.Downloads.Answer = _ => new LibraryDownload(
            Path.Combine(folder, "update-53.7z"),
            new LinkDownload(TestData.Job("update-53.7z", folder: folder), AlreadyQueued: false));

        await row.DownloadCommand.ExecuteAsync(null);

        Assert.Equal([new Uri("https://www.nexusmods.com/skyrimspecialedition/mods/12604?tab=files&file_id=53")], _harness.Downloads.Added);
        Assert.Equal(($"Downloading into {folder}. It shows on the Downloads screen.", false), (row.Note, row.IsNoteAProblem));
    }

    [Fact]
    public async Task AnUpdateDownloadingAlreadySaysSo()
    {
        UpdateViewModel row = await ShowAsync("53");
        _harness.Downloads.Answer = _ => new LibraryDownload(Path.Combine(TestData.LibraryRoot, "update-53.7z"), new LinkDownload(TestData.Job(), AlreadyQueued: true));

        await row.DownloadCommand.ExecuteAsync(null);

        Assert.Equal("It's on the Downloads screen already.", row.Note);
    }

    [Fact]
    public async Task AnUpdateInTheLibraryAlreadySaysSo()
    {
        UpdateViewModel row = await ShowAsync("53");
        string path = Path.Combine(TestData.LibraryRoot, "update-53.7z");
        _harness.Downloads.Answer = _ => new LibraryDownload(path, null);

        await row.DownloadCommand.ExecuteAsync(null);

        Assert.Equal($"{path} is in the library already.", row.Note);
    }

    [Fact]
    public async Task ADownloadTheSiteRefusesSaysWhy()
    {
        UpdateViewModel row = await ShowAsync("53");
        _harness.Downloads.Refusals.Enqueue(new AuthenticationFailedException());

        await row.DownloadCommand.ExecuteAsync(null);

        Assert.Equal(
            ("The site needs a linked account to look that link up, or refused the one linked. Link it on the Accounts screen, then try again.", true),
            (row.Note, row.IsNoteAProblem));
        Assert.Empty(_harness.Logger.Entries);
    }

    [Fact]
    public async Task WhatFailsUnexpectedlyIsLoggedAndTold()
    {
        UpdateViewModel row = await ShowAsync("53");
        _harness.Downloads.Refusals.Enqueue(new InvalidOperationException("Something broke."));

        await row.DownloadCommand.ExecuteAsync(null);

        Assert.Equal([("That didn't work", "Something went wrong. The log has the details.")], _harness.Dialogs.Messages);
        Assert.StartsWith(
            "Error: Download of update 53 of mod 12604 failed System.InvalidOperationException",
            Assert.Single(_harness.Logger.Entries),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShowsItsVersionsChangesReadOncePerMod()
    {
        _harness.Catalog.Changelogs["12604"] = [new ModChangelog("53", ["Fixed the map.", "Faster."]), new ModChangelog("52", ["Older."])];
        await _harness.StartAsync(UpdatesHarness.ModWithUpdates(SkyUi, "SkyUI", TestData.Now, "52", "53"));
        UpdateViewModel newer = _harness.Row("53");
        UpdateViewModel older = _harness.Row("52");

        await newer.ToggleChangesCommand.ExecuteAsync(null);
        await older.ToggleChangesCommand.ExecuteAsync(null);

        Assert.Equal((true, string.Empty), (newer.AreChangesShown, newer.ChangesNote));
        Assert.Equal(["Fixed the map.", "Faster."], newer.Changes);
        Assert.Equal(["Older."], older.Changes);
        Assert.Equal(["GetChangelogsAsync 12604"], _harness.Catalog.Asked);
    }

    [Fact]
    public async Task ShowingTheChangesAgainHidesThem()
    {
        _harness.Catalog.Changelogs["12604"] = [new ModChangelog("53", ["Fixed the map."])];
        UpdateViewModel row = await ShowAsync("53");
        await row.ToggleChangesCommand.ExecuteAsync(null);

        await row.ToggleChangesCommand.ExecuteAsync(null);
        bool hidden = !row.AreChangesShown;
        await row.ToggleChangesCommand.ExecuteAsync(null);

        Assert.True(hidden);
        Assert.True(row.AreChangesShown);
        Assert.Single(_harness.Catalog.Asked);
    }

    [Fact]
    public async Task AChangelogWithoutTheVersionSaysSo()
    {
        _harness.Catalog.Changelogs["12604"] = [new ModChangelog("52", ["Older."])];
        UpdateViewModel row = await ShowAsync("53");

        await row.ToggleChangesCommand.ExecuteAsync(null);

        Assert.Equal(("The changelog says nothing about this version.", 0), (row.ChangesNote, row.Changes.Count));
    }

    [Fact]
    public async Task AnUpdateWithoutAVersionHasNoChangesToFind()
    {
        LibraryUpdate update = new(new ModFileRef(SkyUi, "53"), "Update 53", "update-53.7z", null, ModFileCategory.Main, TestData.Now, null, TestData.Now);
        await _harness.StartAsync(TestData.LibraryMod(SkyUi, "SkyUI", watched: true, TestData.LibraryFile(SkyUi, "1", "a.7z")).WithUpdates([update]));
        UpdateViewModel row = _harness.Row("53");

        await row.ToggleChangesCommand.ExecuteAsync(null);

        Assert.Equal("The file has no version, so its changes can't be found in the changelog.", row.ChangesNote);
        Assert.Empty(_harness.Catalog.Asked);
    }

    [Fact]
    public async Task ASiteWithoutChangelogsSaysSo()
    {
        ModRef plain = new(new GameRef(UpdatesHarness.Plain, "game"), "1");
        await _harness.StartAsync(UpdatesHarness.ModWithUpdates(plain, "Plain mod", TestData.Now, "2"));
        UpdateViewModel row = _harness.Row("2");

        await row.ToggleChangesCommand.ExecuteAsync(null);

        Assert.Equal("Plain Site doesn't give changelogs.", row.ChangesNote);
    }

    [Fact]
    public async Task AChangelogTheSiteRefusesSaysWhyAndIsAskedForAgainNextTime()
    {
        UpdateViewModel row = await ShowAsync("53");
        _harness.Catalog.Refusal = new AuthenticationFailedException();
        await row.ToggleChangesCommand.ExecuteAsync(null);
        string refused = row.ChangesNote;
        _harness.Catalog.Refusal = new HttpRequestException("Service unavailable.", null, HttpStatusCode.ServiceUnavailable);
        await row.ToggleChangesCommand.ExecuteAsync(null);

        await row.ToggleChangesCommand.ExecuteAsync(null);

        Assert.Equal(
            "The site needs a linked account to give the changelog, or refused the one linked. Link it on the Accounts screen.",
            refused);
        Assert.Equal("The site couldn't give the changelog: Service unavailable.", row.ChangesNote);
        Assert.Equal(2, _harness.Catalog.Asked.Count);
    }

    [Fact]
    public async Task AChangelogThatFailsUnexpectedlyIsHiddenAndAskedForAgainNextTime()
    {
        _harness.Catalog.Changelogs["12604"] = [new ModChangelog("53", ["Fixed the map."])];
        UpdateViewModel row = await ShowAsync("53");
        _harness.Catalog.Refusal = new InvalidOperationException("Something broke.");
        await row.ToggleChangesCommand.ExecuteAsync(null);
        (bool Shown, string Note) failed = (row.AreChangesShown, row.ChangesNote);
        _harness.Catalog.Refusal = null;

        await row.ToggleChangesCommand.ExecuteAsync(null);

        Assert.Equal((false, string.Empty), failed);
        Assert.Equal([("That didn't work", "Something went wrong. The log has the details.")], _harness.Dialogs.Messages);
        Assert.Single(_harness.Logger.Entries);
        Assert.Equal((true, string.Empty), (row.AreChangesShown, row.ChangesNote));
        Assert.Equal(["Fixed the map."], row.Changes);
        Assert.Equal(2, _harness.Catalog.Asked.Count);
    }

    private async Task<UpdateViewModel> ShowAsync(string update)
    {
        await _harness.StartAsync(UpdatesHarness.ModWithUpdates(SkyUi, "SkyUI", TestData.Now, update));
        return _harness.Row(update);
    }
}
