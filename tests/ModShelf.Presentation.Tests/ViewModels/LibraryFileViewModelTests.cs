using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;
using ModShelf.Presentation.Tests.Common;
using ModShelf.Presentation.ViewModels;

namespace ModShelf.Presentation.Tests.ViewModels;

public sealed class LibraryFileViewModelTests : IDisposable
{
    private static readonly ModRef SkyUi = TestData.Mod("12604");
    private static readonly LibraryFile File = TestData.LibraryFile(SkyUi, "35407", "SkyUI_5_2_SE.7z", "5.2", size: 3 * 1024 * 1024, daysAgo: 6);

    private readonly LibraryHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task ShowsTheFile()
    {
        using CultureScope culture = new("en-US");

        LibraryFileViewModel row = await ShowAsync(File);

        Assert.Equal(
            ("SkyUI_5_2_SE.7z", "SkyUI_5_2_SE.7z file", "Version 5.2", "3.00 MB", "Downloaded 10/21/2026"),
            (row.FileName, row.Name, row.VersionText, row.SizeText, row.DownloadedText));
    }

    [Fact]
    public async Task TheDateIsTheUsersAndAFileWithoutAVersionSaysNone()
    {
        using CultureScope culture = new("es-ES");
        _harness.Time.SetLocalTimeZone(TimeZoneInfo.CreateCustomTimeZone("UTC+14", TimeSpan.FromHours(14), "UTC+14", "UTC+14"));
        LibraryFile late = new(File.File, File.Name, null, File.Category, File.UploadedAt, File.RelativePath, File.SizeInBytes, new DateTimeOffset(2026, 10, 21, 23, 0, 0, TimeSpan.Zero));

        LibraryFileViewModel row = await ShowAsync(late);

        Assert.Equal(("Downloaded 22/10/2026", string.Empty), (row.DownloadedText, row.VersionText));
    }

    [Fact]
    public async Task DeletesTheFileOnceTheUserConfirms()
    {
        using CultureScope culture = new("en-US");
        LibraryFileViewModel row = await ShowAsync(File);
        _harness.Dialogs.Confirm.Enqueue(true);

        await row.DeleteCommand.ExecuteAsync(null);

        Assert.Equal(
            [("Delete this file?", "SkyUI_5_2_SE.7z (3.00 MB) is deleted from disk for good, and the library forgets it.", "Delete")],
            _harness.Dialogs.Confirmations);
        Assert.Equal(["Delete 35407"], _harness.Library.Asked);
        Assert.Empty(_harness.Screen.Rows.OfType<LibraryModViewModel>().Single().Files);
    }

    [Fact]
    public async Task NotConfirmingKeepsTheFile()
    {
        LibraryFileViewModel row = await ShowAsync(File);

        await row.DeleteCommand.ExecuteAsync(null);

        Assert.Empty(_harness.Library.Asked);
    }

    [Theory]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(UnauthorizedAccessException))]
    public async Task AFileThatCantBeDeletedSaysWhy(Type failure)
    {
        LibraryFileViewModel row = await ShowAsync(File);
        _harness.Dialogs.Confirm.Enqueue(true);
        _harness.Library.Refusal = (Exception)Activator.CreateInstance(failure, "The file is open in another program.")!;

        await row.DeleteCommand.ExecuteAsync(null);

        Assert.Equal(
            [("The file wasn't deleted", "SkyUI_5_2_SE.7z couldn't be deleted: The file is open in another program. It stays in the library.")],
            _harness.Dialogs.Messages);
        Assert.Empty(_harness.Logger.Entries);
    }

    [Fact]
    public async Task SettingsThatCantBeReadAreToldOf()
    {
        LibraryFileViewModel row = await ShowAsync(File);
        _harness.Dialogs.Confirm.Enqueue(true);
        _harness.Library.Refusal = new InvalidDataException("settings.json isn't valid JSON.");

        await row.DeleteCommand.ExecuteAsync(null);

        Assert.Equal([("The file wasn't deleted", "settings.json isn't valid JSON.")], _harness.Dialogs.Messages);
    }

    [Fact]
    public async Task AFileThatLeftMeanwhileIsLeftAlone()
    {
        LibraryFileViewModel row = await ShowAsync(File);
        _harness.Dialogs.Confirm.Enqueue(true);
        _harness.Library.Refusal = new KeyNotFoundException();

        await row.DeleteCommand.ExecuteAsync(null);

        Assert.Empty(_harness.Dialogs.Messages);
        Assert.Empty(_harness.Logger.Entries);
    }

    [Fact]
    public async Task WhatFailsUnexpectedlyIsLoggedAndTold()
    {
        LibraryFileViewModel row = await ShowAsync(File);
        _harness.Dialogs.Confirm.Enqueue(true);
        _harness.Library.Refusal = new InvalidOperationException("The database is locked.");

        await row.DeleteCommand.ExecuteAsync(null);

        Assert.Equal([("That didn't work", "Something went wrong. The log has the details.")], _harness.Dialogs.Messages);
        Assert.StartsWith(
            "Error: Deleting file 35407 of mod 12604 failed System.InvalidOperationException",
            Assert.Single(_harness.Logger.Entries),
            StringComparison.Ordinal);
    }

    private async Task<LibraryFileViewModel> ShowAsync(LibraryFile file)
    {
        LibraryMod mod = TestData.LibraryMod(SkyUi, "SkyUI", watched: true, file);
        await _harness.StartAsync(mod);
        return Assert.Single(_harness.Row(mod).Files);
    }
}
