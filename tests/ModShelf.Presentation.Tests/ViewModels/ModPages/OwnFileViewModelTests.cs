using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;
using ModShelf.Presentation.Tests.Common;
using ModShelf.Presentation.Tests.ViewModels.Browse;
using ModShelf.Presentation.ViewModels.ModPages;

namespace ModShelf.Presentation.Tests.ViewModels.ModPages;

/// <summary>
/// One of the library's files of a mod, on its page: what it says of the file, and deleting it, which the Library's rows
/// did before the mod had a page.
/// </summary>
public sealed class OwnFileViewModelTests : IDisposable
{
    private static readonly ModRef SkyUi = TestData.Mod("12604");
    private static readonly LibraryFile File = TestData.LibraryFile(SkyUi, "35407", "SkyUI_5_2_SE.7z", "5.2", size: 3 * 1024 * 1024, daysAgo: 6);

    private readonly BrowseHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task ShowsTheFile()
    {
        using CultureScope culture = new("en-US");

        OwnFileViewModel row = await ShowAsync(File);

        Assert.Equal(
            ("SkyUI_5_2_SE.7z", "SkyUI_5_2_SE.7z file", "Main", "3.00 MB", "Downloaded 10/21/2026"),
            (row.FileName, row.Name, row.CategoryText, row.SizeText, row.DownloadedText));
        Assert.Equal((false, false, false), (row.HasSha256, row.IsArchived, row.IsMissing));
    }

    [Fact]
    public async Task TheDateIsTheUsers()
    {
        using CultureScope culture = new("es-ES");
        _harness.Time.SetLocalTimeZone(TimeZoneInfo.CreateCustomTimeZone("UTC+14", TimeSpan.FromHours(14), "UTC+14", "UTC+14"));
        LibraryFile late = new(File.File, File.Name, null, File.Category, File.UploadedAt, File.RelativePath, File.SizeInBytes, new DateTimeOffset(2026, 10, 21, 23, 0, 0, TimeSpan.Zero));

        OwnFileViewModel row = await ShowAsync(late);

        // Spanish writes the day first; the 21st at 23:00 UTC is the 22nd fourteen hours ahead.
        Assert.Equal("Downloaded 22/10/2026", row.DownloadedText);
    }

    [Fact]
    public async Task DeletesTheFileOnceTheUserConfirms()
    {
        using CultureScope culture = new("en-US");
        OwnFileViewModel row = await ShowAsync(File);
        _harness.Dialogs.Confirm.Enqueue(true);

        await row.DeleteCommand.ExecuteAsync(null);

        Assert.Equal(
            [("Delete this file?", "SkyUI_5_2_SE.7z (3.00 MB) is deleted from disk for good, and the library forgets it.", "Delete")],
            _harness.Dialogs.Confirmations);
        Assert.Equal(["Delete 35407"], _harness.Library.Asked);
        Assert.Empty(_harness.Screen.Mod!.OwnFiles);
    }

    [Fact]
    public async Task NotConfirmingKeepsTheFile()
    {
        OwnFileViewModel row = await ShowAsync(File);

        await row.DeleteCommand.ExecuteAsync(null);

        Assert.Empty(_harness.Library.Asked);
        Assert.Single(_harness.Screen.Mod!.OwnFiles);
    }

    [Theory]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(UnauthorizedAccessException))]
    public async Task AFileThatCantBeDeletedSaysWhy(Type failure)
    {
        OwnFileViewModel row = await ShowAsync(File);
        _harness.Dialogs.Confirm.Enqueue(true);
        _harness.Library.Refusal = (Exception)Activator.CreateInstance(failure, "The file is open in another program.")!;

        await row.DeleteCommand.ExecuteAsync(null);

        Assert.Equal(
            [("The file wasn't deleted", "SkyUI_5_2_SE.7z couldn't be deleted: The file is open in another program. It stays in the library.")],
            _harness.Dialogs.Messages);
        Assert.DoesNotContain(_harness.Logger.Entries, entry => entry.Contains("DeleteFile", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SettingsThatCantBeReadAreToldOf()
    {
        OwnFileViewModel row = await ShowAsync(File);
        _harness.Dialogs.Confirm.Enqueue(true);
        _harness.Library.Refusal = new InvalidDataException("settings.json isn't valid JSON.");

        await row.DeleteCommand.ExecuteAsync(null);

        Assert.Equal([("The file wasn't deleted", "settings.json isn't valid JSON.")], _harness.Dialogs.Messages);
    }

    [Fact]
    public async Task AFileThatLeftMeanwhileIsLeftAlone()
    {
        OwnFileViewModel row = await ShowAsync(File);
        _harness.Dialogs.Confirm.Enqueue(true);
        _harness.Library.Refusal = new KeyNotFoundException();

        await row.DeleteCommand.ExecuteAsync(null);

        Assert.Empty(_harness.Dialogs.Messages);
        Assert.DoesNotContain(_harness.Logger.Entries, entry => entry.Contains("DeleteFile", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WhatFailsUnexpectedlyIsLoggedAndTold()
    {
        OwnFileViewModel row = await ShowAsync(File);
        _harness.Dialogs.Confirm.Enqueue(true);
        _harness.Library.Refusal = new InvalidOperationException("The database is locked.");

        await row.DeleteCommand.ExecuteAsync(null);

        Assert.Equal([("That didn't work", "Something went wrong. The log has the details.")], _harness.Dialogs.Messages);
        Assert.Contains(
            _harness.Logger.Entries,
            entry => entry.StartsWith("Error: DeleteFile of mod 12604 of skyrimspecialedition failed System.InvalidOperationException", StringComparison.Ordinal));
    }

    private async Task<OwnFileViewModel> ShowAsync(LibraryFile file)
    {
        _harness.Storage.Files.Add(LibraryPaths.Resolve(TestData.LibraryRoot, file.RelativePath));
        await _harness.StartAsync(TestData.LibraryMod(SkyUi, "SkyUI", watched: true, file));
        ModPageViewModel page = await _harness.OpenAsync(BrowseHarness.Page(BrowseHarness.Info(SkyUi, "SkyUI")));
        return Assert.Single(page.OwnFiles);
    }
}
