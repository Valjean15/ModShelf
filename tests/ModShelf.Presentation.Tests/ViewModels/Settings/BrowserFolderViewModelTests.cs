using ModShelf.Application.Settings;
using ModShelf.Presentation.Tests.Fakes;
using ModShelf.Presentation.ViewModels.Settings;
using ModShelf.Testing;

namespace ModShelf.Presentation.Tests.ViewModels.Settings;

/// <summary>
/// The browser's downloads folder on Settings › Library (ADR 0091).
/// </summary>
public sealed class BrowserFolderViewModelTests
{
    private readonly InMemorySettingsService _settings = new();
    private readonly FixedUserFolders _folders = new();
    private readonly ScriptedDialogs _dialogs = new();
    private readonly RecordingLogger<BrowserFolderViewModel> _logger = new();
    private readonly BrowserFolderViewModel _card;

    public BrowserFolderViewModelTests()
    {
        _card = new BrowserFolderViewModel(_settings, _folders, _dialogs, _logger);
    }

    [Fact]
    public void ShowsTheSystemsDownloadsFolderUntilAnotherIsChosen()
    {
        bool before = _card.UseSystemsCommand.CanExecute(null);
        string folder = Path.Combine(Path.GetTempPath(), "Elsewhere");

        _card.Show(new AppSettings { BrowserDownloadsFolder = folder });

        Assert.False(before);
        Assert.Equal((folder, false), (_card.Folder, _card.IsSystems));
        Assert.True(_card.UseSystemsCommand.CanExecute(null));

        _card.Show(new AppSettings());

        Assert.Equal((_folders.Downloads, true), (_card.Folder, _card.IsSystems));
    }

    [Fact]
    public async Task ChoosingAFolderSavesItAndTheSystemsGoesBack()
    {
        string folder = Path.Combine(Path.GetTempPath(), "Elsewhere");
        _dialogs.Folders.Enqueue(folder);

        await _card.ChooseCommand.ExecuteAsync(null);
        (string, bool) chosen = (_card.Folder, _card.IsSystems);
        await _card.UseSystemsCommand.ExecuteAsync(null);

        Assert.Equal((folder, false), chosen);
        Assert.Equal(["Choose your browser's downloads folder"], _dialogs.FolderPickers);
        Assert.Equal([$"browserDownloadsFolder {folder}", "browserDownloadsFolder null"], _settings.Changes);
        Assert.Equal((_folders.Downloads, true), (_card.Folder, _card.IsSystems));
        Assert.Null(_card.Problem);
    }

    [Fact]
    public async Task CancellingThePickerChangesNothing()
    {
        _dialogs.Folders.Enqueue(null);

        await _card.ChooseCommand.ExecuteAsync(null);

        Assert.Empty(_settings.Changes);
    }

    [Fact]
    public async Task SaysWhenTheFolderCantBeSaved()
    {
        _settings.SaveRefusal = new IOException("The disk is full.");
        _dialogs.Folders.Enqueue(Path.Combine(Path.GetTempPath(), "Elsewhere"));

        await _card.ChooseCommand.ExecuteAsync(null);

        Assert.Equal("Something went wrong. The log has the details.", _card.Problem);
        Assert.True(_card.IsSystems);
        Assert.Contains(_logger.Entries, entry => entry.Contains("Couldn't save the browser's downloads folder", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AFolderSavedAfterOneThatWasntSaysNothingWrong()
    {
        string folder = Path.Combine(Path.GetTempPath(), "Elsewhere");
        _settings.SaveRefusal = new IOException("The disk is full.");
        _dialogs.Folders.Enqueue(folder);
        _dialogs.Folders.Enqueue(folder);
        await _card.ChooseCommand.ExecuteAsync(null);
        string? problem = _card.Problem;
        _settings.SaveRefusal = null;

        await _card.ChooseCommand.ExecuteAsync(null);

        Assert.NotNull(problem);
        Assert.Null(_card.Problem);
        Assert.Equal((folder, false), (_card.Folder, _card.IsSystems));
    }
}
