using ModShelf.Application.Downloads;
using ModShelf.Domain.Downloads;
using ModShelf.Domain.Mods;
using ModShelf.Presentation.Tests.Fakes;
using ModShelf.Presentation.ViewModels.Downloads;

namespace ModShelf.Presentation.Tests.ViewModels.Downloads;

/// <summary>
/// Downloads waiting on a page: each site's words for them, and handing one the file the browser downloaded (ADR 0091).
/// </summary>
public sealed class DownloadsFilesFromTheBrowserTests : IDisposable
{
    private static readonly SourceId DegMods = new("degmods");
    private static readonly SourceId OtherPages = new("pages");

    private readonly DownloadsHarness _harness = new(new PageSite(DegMods, "DEG Mods"), new PageSite(OtherPages, "Page Mods"));

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task ADownloadOnAPageSaysTheBrowserDownloadsItAndOffersNoLinkToPaste()
    {
        DownloadJobSnapshot job = OnPage(DegMods, "Main File.zip");

        DownloadJobViewModel row = await ShowAsync(job);

        Assert.Equal("Waiting for you: download it from its page, and ModShelf takes the file from your downloads folder", row.StatusText);
        Assert.Equal("Open the page", row.OpenPageLabel);
        Assert.False(row.HandsOverLinks);
        Assert.False(row.PasteLinkCommand.CanExecute(null));
        Assert.True(row.ChooseFileCommand.CanExecute(null));
    }

    [Fact]
    public async Task ANexusModsDownloadOffersAFileToo()
    {
        DownloadJobViewModel row = await ShowAsync(TestData.Job(status: DownloadJobStatus.AwaitingHandoff));

        Assert.True(row.HandsOverLinks);
        Assert.True(row.PasteLinkCommand.CanExecute(null));
        Assert.True(row.ChooseFileCommand.CanExecute(null));
        Assert.Equal("Open on Nexus Mods", row.OpenPageLabel);
    }

    [Fact]
    public async Task OnlyAWaitingDownloadTakesAFile()
    {
        DownloadJobViewModel row = await ShowAsync(OnPage(DegMods, "Main File.zip") with { Status = DownloadJobStatus.Downloading });

        Assert.False(row.ChooseFileCommand.CanExecute(null));
    }

    [Fact]
    public async Task ChoosingTheFileHandsItToTheQueueStartingInTheBrowsersFolder()
    {
        DownloadJobSnapshot job = OnPage(DegMods, "Main File.zip");
        DownloadJobViewModel row = await ShowAsync(job);
        string path = Path.Combine(_harness.Folders.Downloads, "Main File.zip");
        _harness.Dialogs.Files.Enqueue(path);

        await row.ChooseFileCommand.ExecuteAsync(null);

        Assert.Equal([("Choose the file you downloaded: Main File.zip", (string?)_harness.Folders.Downloads)], _harness.Dialogs.FilePickers);
        Assert.Equal([(job.Id, path)], _harness.Downloads.TakenFiles);
        Assert.Empty(_harness.Dialogs.Messages);
        Assert.Equal(DownloadJobStatus.Completed, row.Status);
    }

    [Fact]
    public async Task StartsInTheFolderTheSettingsNameOrNoneWhenTheyCantBeRead()
    {
        string folder = Path.Combine(Path.GetTempPath(), "Elsewhere");
        _harness.Settings.Stored = new Application.Settings.AppSettings { BrowserDownloadsFolder = folder };
        DownloadJobViewModel row = await ShowAsync(OnPage(DegMods, "Main File.zip"));

        await row.ChooseFileCommand.ExecuteAsync(null);
        _harness.Settings.ReadRefusal = new InvalidDataException("Not JSON.");
        await row.ChooseFileCommand.ExecuteAsync(null);

        Assert.Equal([folder, null], _harness.Dialogs.FilePickers.Select(picker => picker.Folder));
        Assert.Empty(_harness.Downloads.TakenFiles);
    }

    [Theory]
    [InlineData(TakeFileOutcome.HashMismatch, "That file isn't Main File.zip: its checksum isn't the one DEG Mods gives. It's left where it was, and the download still waits for you.")]
    [InlineData(TakeFileOutcome.NotFound, "That file isn't there any more. The download still waits for you.")]
    [InlineData(TakeFileOutcome.StorageError, "ModShelf couldn't move that file into the library, such as because another program has it open. It's left where it was, and the log has the details.")]
    public async Task SaysWhyAFileWasntTaken(TakeFileOutcome outcome, string message)
    {
        DownloadJobViewModel row = await ShowAsync(OnPage(DegMods, "Main File.zip"));
        _harness.Downloads.TakeOutcome = outcome;
        _harness.Dialogs.Files.Enqueue(Path.Combine(_harness.Folders.Downloads, "other.zip"));

        await row.ChooseFileCommand.ExecuteAsync(null);

        Assert.Equal([("That file wasn't taken", message)], _harness.Dialogs.Messages);
        Assert.Equal(DownloadJobStatus.AwaitingHandoff, row.Status);
    }

    [Fact]
    public async Task ADownloadThatStoppedWaitingMeanwhileSaysNothing()
    {
        DownloadJobViewModel row = await ShowAsync(OnPage(DegMods, "Main File.zip"));
        _harness.Downloads.Refusal = new InvalidOperationException("It's downloading.");
        _harness.Dialogs.Files.Enqueue(Path.Combine(_harness.Folders.Downloads, "Main File.zip"));

        await row.ChooseFileCommand.ExecuteAsync(null);

        Assert.Empty(_harness.Dialogs.Messages);
    }

    [Fact]
    public async Task CancellingThePickerHandsNothingOver()
    {
        DownloadJobViewModel row = await ShowAsync(OnPage(DegMods, "Main File.zip"));
        _harness.Dialogs.Files.Enqueue(null);

        await row.ChooseFileCommand.ExecuteAsync(null);

        Assert.Empty(_harness.Downloads.TakenFiles);
        Assert.Empty(_harness.Dialogs.Messages);
    }

    [Fact]
    public async Task SaysHowToFinishTheDownloadsWaitingOnPages()
    {
        await _harness.StartWithAsync([OnPage(DegMods, "a.zip")]);
        string one = _harness.Screen.WaitingText;
        _harness.Downloads.Publish(OnPage(DegMods, "b.zip"));
        string two = _harness.Screen.WaitingText;
        _harness.Downloads.Publish(OnPage(OtherPages, "c.zip"));
        string sites = _harness.Screen.WaitingText;
        _harness.Downloads.Publish(TestData.Job(status: DownloadJobStatus.AwaitingHandoff));
        string mixed = _harness.Screen.WaitingText;

        Assert.Equal("1 file waits for you on DEG Mods. Download it from its page there, and ModShelf takes it from your downloads folder.", one);
        Assert.Equal("2 files wait for you on DEG Mods. Download each from its page there, and ModShelf takes them from your downloads folder.", two);
        Assert.Equal("3 files wait for you on their sites. Download each from its page there, and ModShelf takes them from your downloads folder.", sites);
        Assert.Equal(
            "4 files wait for you on their sites. Start or download each from its page there: ModShelf goes on with the link a site hands over, or takes the file from your downloads folder.",
            mixed);
    }

    private static DownloadJobSnapshot OnPage(SourceId site, string fileName)
        => TestData.Job(fileName, DownloadJobStatus.AwaitingHandoff) with
        {
            File = new ModFileRef(new ModRef(new GameRef(site, "fallout: new vegas"), "author:" + fileName), "0123456789abcdef"),
            HandoffPage = new Uri("https://www.mediafire.com/file/abc/" + fileName),
        };

    private async Task<DownloadJobViewModel> ShowAsync(DownloadJobSnapshot job)
    {
        await _harness.StartWithAsync([job]);
        return _harness.Row(job);
    }
}
