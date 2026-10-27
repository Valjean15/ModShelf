using System.Net;

using ModShelf.Application.Downloads;
using ModShelf.Application.Library;
using ModShelf.Domain.Downloads;
using ModShelf.Presentation.ViewModels;
using ModShelf.Sources.Abstractions.Accounts;

namespace ModShelf.Presentation.Tests.ViewModels;

public sealed class DownloadsViewModelTests : IDisposable
{
    private readonly DownloadsHarness _harness = new();

    private DownloadsViewModel Screen => _harness.Screen;

    public static TheoryData<Exception, string> LibraryRefusals => new()
    {
        {
            new UnsupportedLinkException("This link isn't to a mod on a site whose mods ModShelf keeps in a library."),
            "Only the files of mods on sites such as Nexus Mods go into the library. To download this file into a folder of your own, choose Save to a folder."
        },
        { new UnsupportedLinkException("No source knows this link.", new UnsupportedLinkException()), "No source can download that link." },
        { new ArgumentException("The link is to a collection, not a mod or a file.", "link"), "The link is to a collection, not a mod or a file." },
        {
            new AuthenticationFailedException(),
            "The site needs a linked account to look that link up, or refused the one linked. Link it on the Accounts screen, then try again."
        },
        { new HttpRequestException("Not found.", null, HttpStatusCode.NotFound), "The site has no such mod or file." },
        { new HttpRequestException("Gone.", null, HttpStatusCode.Gone), "The site has no such mod or file." },
        { new HttpRequestException("No such host is known."), "The site couldn't look that link up: No such host is known." },
        { new PathTooLongException("The path is too long for this system."), "The path is too long for this system. See where files go on the Settings screen." },
        { new InvalidDataException("settings.json isn't valid JSON."), "settings.json isn't valid JSON." },
    };

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task ListsTheQueueNewestFirst()
    {
        DownloadJobSnapshot oldest = TestData.Job("a.7z", createdAt: TestData.Now);
        DownloadJobSnapshot newest = TestData.Job("c.7z", createdAt: TestData.Now.AddMinutes(2));
        DownloadJobSnapshot middle = TestData.Job("b.7z", createdAt: TestData.Now.AddMinutes(1));

        await _harness.StartWithAsync(oldest, newest, middle);

        Assert.Equal(["c.7z", "b.7z", "a.7z"], Screen.Jobs.Select(job => job.FileName));
        Assert.True(Screen.HasJobs);
    }

    [Fact]
    public async Task WithNothingQueuedThereAreNoJobs()
    {
        await _harness.StartAsync();

        Assert.Empty(Screen.Jobs);
        Assert.False(Screen.HasJobs);
        Assert.Equal(0, Screen.Badge);
    }

    [Fact]
    public async Task ShowsEachChangeAsTheQueueMakesIt()
    {
        DownloadJobSnapshot job = TestData.Job();
        await _harness.StartWithAsync(job);

        _harness.Downloads.Publish(job with { Status = DownloadJobStatus.Downloading, BytesDownloaded = 10, TotalBytes = 100 });

        DownloadJobViewModel row = _harness.Row(job);
        Assert.Equal(DownloadJobStatus.Downloading, row.Status);
        Assert.Equal(10, row.Progress);
    }

    [Fact]
    public async Task ChangesShowOnlyOnTheUiThread()
    {
        DownloadJobSnapshot job = TestData.Job();
        _harness.UiThread.Inline = false;
        await _harness.StartWithAsync(job);
        bool listedBeforeTheUiThreadRan = Screen.HasJobs;
        _harness.UiThread.RunPending();

        _harness.Downloads.Publish(job with { Status = DownloadJobStatus.Downloading });
        DownloadJobStatus beforeTheUiThreadRan = _harness.Row(job).Status;
        _harness.UiThread.RunPending();

        Assert.False(listedBeforeTheUiThreadRan);
        Assert.Equal(DownloadJobStatus.Queued, beforeTheUiThreadRan);
        Assert.Equal(DownloadJobStatus.Downloading, _harness.Row(job).Status);
    }

    [Fact]
    public async Task AJobAddedElsewhereGoesOnTop()
    {
        await _harness.StartWithAsync(TestData.Job("old.7z"));

        _harness.Downloads.Publish(TestData.Job("new.7z", createdAt: TestData.Now.AddSeconds(1)));

        Assert.Equal(["new.7z", "old.7z"], Screen.Jobs.Select(job => job.FileName));
    }

    [Fact]
    public async Task TheBadgeCountsTheDownloadsWaitingForTheUser()
    {
        DownloadJobSnapshot waiting = TestData.Job("waiting.7z", DownloadJobStatus.AwaitingHandoff);
        DownloadJobSnapshot failed = TestData.Job("failed.7z", DownloadJobStatus.Failed);
        DownloadJobSnapshot downloading = TestData.Job("downloading.7z", DownloadJobStatus.Downloading);
        DownloadJobSnapshot paused = TestData.Job("paused.7z", DownloadJobStatus.Paused);
        await _harness.StartWithAsync(waiting, failed, downloading, paused);
        int before = Screen.Badge;

        _harness.Downloads.Publish(waiting with { Status = DownloadJobStatus.Resolving, HandoffPage = null });
        _harness.Downloads.Publish(downloading with { Status = DownloadJobStatus.Failed, FailureReason = DownloadFailureReason.Storage });
        _harness.Downloads.Publish(failed with { Status = DownloadJobStatus.Queued, FailureReason = null });

        Assert.Equal(2, before);
        Assert.Equal(1, Screen.Badge);
        Assert.True(Screen.HasBadge);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AddingNeedsALink(string link)
    {
        await _harness.StartAsync();

        Screen.Link = link;

        Assert.False(Screen.AddCommand.CanExecute(null));
        Assert.False(Screen.AddToFolderCommand.CanExecute(null));
    }

    [Fact]
    public async Task AddDownloadsIntoTheLibrary()
    {
        await _harness.StartAsync();
        string folder = Path.Combine(TestData.LibraryRoot, "nexus", "skyrimspecialedition", "SkyUI (12604)");
        DownloadJobSnapshot job = TestData.Job(folder: folder);
        _harness.Library.Answer = _ => new LibraryDownload(Path.Combine(folder, job.FileName), new LinkDownload(job, AlreadyQueued: false));
        Screen.Link = $"  {TestData.ModPage}  ";

        Assert.True(Screen.AddCommand.CanExecute(null));
        await Screen.AddCommand.ExecuteAsync(null);

        Assert.Equal([TestData.ModPage], _harness.Library.Added);
        Assert.Equal($"Downloading {job.FileName} into {folder}.", Screen.Message);
        Assert.False(Screen.IsMessageAProblem);
        Assert.Equal(string.Empty, Screen.Link);
        Assert.Equal([job.Id], Screen.Jobs.Select(row => row.Id));
        Assert.False(Screen.IsAdding);
    }

    [Fact]
    public async Task AJobTheQueueShowedFirstKeepsWhatItShowed()
    {
        await _harness.StartAsync();
        DownloadJobSnapshot job = TestData.Job();
        _harness.Library.Answer = _ =>
        {
            // The queue started the job before the add returned.
            _harness.Downloads.Publish(job with { Status = DownloadJobStatus.Resolving });
            return new LibraryDownload(Path.Combine(job.TargetDirectory, job.FileName), new LinkDownload(job, AlreadyQueued: false));
        };
        Screen.Link = TestData.ModPage.AbsoluteUri;

        await Screen.AddCommand.ExecuteAsync(null);

        Assert.Equal(DownloadJobStatus.Resolving, _harness.Row(job).Status);
    }

    [Fact]
    public async Task AddSaysWhenTheFileIsInTheLibraryAlready()
    {
        await _harness.StartAsync();
        string path = Path.Combine(TestData.LibraryRoot, "nexus", TestData.FileName);
        _harness.Library.Answer = _ => new LibraryDownload(path, null);
        Screen.Link = TestData.ModPage.AbsoluteUri;

        await Screen.AddCommand.ExecuteAsync(null);

        Assert.Equal($"{path} is in the library already.", Screen.Message);
        Assert.False(Screen.IsMessageAProblem);
        Assert.Equal(string.Empty, Screen.Link);
        Assert.Empty(Screen.Jobs);
    }

    [Theory]
    [InlineData(DownloadJobStatus.Paused, "Resume")]
    [InlineData(DownloadJobStatus.Failed, "Retry")]
    public async Task AddingAJobAddedEarlierGoesOnWithIt(DownloadJobStatus status, string action)
    {
        DownloadJobSnapshot job = TestData.Job(status: status);
        await _harness.StartWithAsync(job);
        _harness.Library.Answer = _ => new LibraryDownload(Path.Combine(job.TargetDirectory, job.FileName), new LinkDownload(job, AlreadyQueued: true));
        Screen.Link = TestData.ModPage.AbsoluteUri;

        await Screen.AddCommand.ExecuteAsync(null);

        Assert.Equal([(action, job.Id)], _harness.Downloads.Controls);
        Assert.Equal($"Continuing the download of {job.FileName}, added earlier.", Screen.Message);
        Assert.Equal(DownloadJobStatus.Queued, Assert.Single(Screen.Jobs).Status);
    }

    [Fact]
    public async Task AddingAJobThatGoesOnAlreadyLeavesIt()
    {
        DownloadJobSnapshot job = TestData.Job(status: DownloadJobStatus.Downloading);
        await _harness.StartWithAsync(job);
        _harness.Library.Answer = _ => new LibraryDownload(Path.Combine(job.TargetDirectory, job.FileName), new LinkDownload(job, AlreadyQueued: true));
        Screen.Link = TestData.ModPage.AbsoluteUri;

        await Screen.AddCommand.ExecuteAsync(null);

        Assert.Empty(_harness.Downloads.Controls);
        Assert.Equal($"Continuing the download of {job.FileName}, added earlier.", Screen.Message);
    }

    [Theory]
    [InlineData("not a link")]
    [InlineData("www.nexusmods.com/skyrimspecialedition/mods/12604")]
    public async Task AddRefusesTextThatIsntALink(string text)
    {
        await _harness.StartAsync();
        Screen.Link = text;

        await Screen.AddCommand.ExecuteAsync(null);

        Assert.Empty(_harness.Library.Added);
        Assert.Equal(
            "That isn't a complete web link, such as https://www.nexusmods.com/skyrimspecialedition/mods/12604.",
            Screen.Message);
        Assert.True(Screen.IsMessageAProblem);
        Assert.Equal(text, Screen.Link);
    }

    [Theory]
    [MemberData(nameof(LibraryRefusals))]
    public async Task AddSaysWhyTheLinkWasRefused(Exception refusal, string message)
    {
        await _harness.StartAsync();
        _harness.Library.Refusals.Enqueue(refusal);
        Screen.Link = TestData.ModPage.AbsoluteUri;

        await Screen.AddCommand.ExecuteAsync(null);

        Assert.Equal(message, Screen.Message);
        Assert.True(Screen.IsMessageAProblem);
        Assert.Equal(TestData.ModPage.AbsoluteUri, Screen.Link);
        Assert.Empty(_harness.Logger.Entries);
    }

    [Fact]
    public async Task AnUnexpectedFailureIsLoggedWithoutTheLink()
    {
        await _harness.StartAsync();
        _harness.Library.Refusals.Enqueue(new InvalidOperationException("Something broke."));
        Screen.Link = TestData.HandoffLink.AbsoluteUri;

        await Screen.AddCommand.ExecuteAsync(null);

        Assert.Equal("Something went wrong. The log has the details.", Screen.Message);
        Assert.True(Screen.IsMessageAProblem);
        string entry = Assert.Single(_harness.Logger.Entries);
        Assert.StartsWith("Error: Adding a download failed System.InvalidOperationException: Something broke.", entry, StringComparison.Ordinal);
        Assert.DoesNotContain("a-key", entry, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AddingAgainClearsWhatTheLastAddSaid()
    {
        await _harness.StartAsync();
        _harness.Library.Refusals.Enqueue(new AuthenticationFailedException());
        Screen.Link = TestData.ModPage.AbsoluteUri;
        await Screen.AddCommand.ExecuteAsync(null);
        _harness.Dialogs.Folders.Enqueue(null);

        await Screen.AddToFolderCommand.ExecuteAsync(null);

        Assert.Null(Screen.Message);
    }

    [Fact]
    public async Task SaveToAFolderDownloadsIntoTheFolderPicked()
    {
        await _harness.StartAsync();
        _harness.Dialogs.Folders.Enqueue(TestData.Folder + Path.DirectorySeparatorChar);
        DownloadJobSnapshot job = TestData.Job();
        _harness.Links.Answer = (_, _, _) => new LinkDownload(job, AlreadyQueued: false);
        Screen.Link = TestData.ModPage.AbsoluteUri;

        await Screen.AddToFolderCommand.ExecuteAsync(null);

        Assert.Equal(["Choose where to save the file"], _harness.Dialogs.FolderPickers);
        Assert.Equal([(TestData.ModPage, TestData.Folder, (string?)null)], _harness.Links.Added);
        Assert.Equal($"Downloading {job.FileName} into {job.TargetDirectory}.", Screen.Message);
        Assert.Equal(string.Empty, Screen.Link);
        Assert.Equal([job.Id], Screen.Jobs.Select(row => row.Id));
    }

    [Fact]
    public async Task NotPickingAFolderAddsNothing()
    {
        await _harness.StartAsync();
        Screen.Link = TestData.ModPage.AbsoluteUri;

        await Screen.AddToFolderCommand.ExecuteAsync(null);

        Assert.Empty(_harness.Links.Added);
        Assert.Null(Screen.Message);
        Assert.Equal(TestData.ModPage.AbsoluteUri, Screen.Link);
    }

    [Fact]
    public async Task SaveToAFolderAsksForANameWhenTheLinkGivesNone()
    {
        await _harness.StartAsync();
        Uri link = new("https://example.com/download?id=5");
        _harness.Dialogs.Folders.Enqueue(TestData.Folder);
        _harness.Dialogs.Answers.Enqueue("  mod.zip ");
        _harness.Links.Refusals.Enqueue(FileNameRefused("The link doesn't say what the file is called, so a file name is needed."));
        DownloadJobSnapshot job = TestData.Job("mod.zip");
        _harness.Links.Answer = (_, _, _) => new LinkDownload(job, AlreadyQueued: false);
        Screen.Link = link.AbsoluteUri;

        await Screen.AddToFolderCommand.ExecuteAsync(null);

        Assert.Equal(
            [(link, TestData.Folder, (string?)null), (link, TestData.Folder, "mod.zip")],
            _harness.Links.Added);
        Assert.Equal(
            "Name the file|The link doesn't say what the file is called. What should it be saved as?|file.zip|Download",
            string.Join('|', _harness.Dialogs.Questions.Select(question => $"{question.Title}|{question.Message}|{question.Hint}|{question.ConfirmLabel}")));
        Assert.Equal($"Downloading mod.zip into {TestData.Folder}.", Screen.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public async Task NotNamingTheFileAddsNothing(string? answer)
    {
        await _harness.StartAsync();
        _harness.Dialogs.Folders.Enqueue(TestData.Folder);
        _harness.Dialogs.Answers.Enqueue(answer);
        _harness.Links.Refusals.Enqueue(FileNameRefused("A file name is needed."));
        Screen.Link = "https://example.com/download?id=5";

        await Screen.AddToFolderCommand.ExecuteAsync(null);

        Assert.Single(_harness.Links.Added);
        Assert.Null(Screen.Message);
        Assert.Empty(Screen.Jobs);
    }

    [Fact]
    public async Task ANameThatsRefusedSaysWhy()
    {
        await _harness.StartAsync();
        _harness.Dialogs.Folders.Enqueue(TestData.Folder);
        _harness.Dialogs.Answers.Enqueue("a/b.zip");
        _harness.Links.Refusals.Enqueue(FileNameRefused("A file name is needed."));
        _harness.Links.Refusals.Enqueue(FileNameRefused("The file name can't contain '/'."));
        Screen.Link = "https://example.com/download?id=5";

        await Screen.AddToFolderCommand.ExecuteAsync(null);

        Assert.Equal("The file name can't contain '/'.", Screen.Message);
        Assert.True(Screen.IsMessageAProblem);
    }

    [Fact]
    public async Task SaveToAFolderSaysWhenNoSourceKnowsTheLink()
    {
        await _harness.StartAsync();
        _harness.Dialogs.Folders.Enqueue(TestData.Folder);
        _harness.Links.Refusals.Enqueue(new UnsupportedLinkException());
        Screen.Link = "ftp://example.com/mod.zip";

        await Screen.AddToFolderCommand.ExecuteAsync(null);

        Assert.Equal("No source can download that link.", Screen.Message);
    }

    [Fact]
    public async Task ClearFinishedRemovesTheDoneAndCancelledDownloads()
    {
        DownloadJobSnapshot done = TestData.Job("done.7z", DownloadJobStatus.Completed);
        DownloadJobSnapshot cancelled = TestData.Job("cancelled.7z", DownloadJobStatus.Cancelled);
        DownloadJobSnapshot failed = TestData.Job("failed.7z", DownloadJobStatus.Failed);
        await _harness.StartWithAsync(done, cancelled, failed);
        bool couldClear = Screen.ClearFinishedCommand.CanExecute(null);

        await Screen.ClearFinishedCommand.ExecuteAsync(null);

        Assert.True(couldClear);
        Assert.Equal(["failed.7z"], Screen.Jobs.Select(job => job.FileName));
        Assert.Equal([failed.Id], _harness.Downloads.Jobs.Select(job => job.Id));
        Assert.False(Screen.HasFinished);
        Assert.False(Screen.ClearFinishedCommand.CanExecute(null));
    }

    [Theory]
    [InlineData(DownloadJobStatus.Completed)]
    [InlineData(DownloadJobStatus.Cancelled)]
    public async Task ADownloadThatEndsCanBeCleared(DownloadJobStatus status)
    {
        DownloadJobSnapshot job = TestData.Job(status: DownloadJobStatus.Verifying);
        await _harness.StartWithAsync(job);
        bool before = Screen.ClearFinishedCommand.CanExecute(null);

        _harness.Downloads.Publish(job with { Status = status });

        Assert.False(before);
        Assert.True(Screen.ClearFinishedCommand.CanExecute(null));
    }

    [Fact]
    public async Task ClearFinishedKeepsADownloadThatFinishedAfterTheQueueCleared()
    {
        DownloadJobSnapshot done = TestData.Job("done.7z", DownloadJobStatus.Completed);
        DownloadJobSnapshot verifying = TestData.Job("verifying.7z", DownloadJobStatus.Verifying);
        await _harness.StartWithAsync(done, verifying);
        _harness.UiThread.Inline = false;

        await Screen.ClearFinishedCommand.ExecuteAsync(null);
        _harness.Downloads.Publish(verifying with { Status = DownloadJobStatus.Completed });
        _harness.UiThread.RunPending();

        Assert.Equal(["verifying.7z"], Screen.Jobs.Select(job => job.FileName));
    }

    [Fact]
    public async Task ClearFinishedKeepsADownloadAddedWhileItCleared()
    {
        await _harness.StartWithAsync(TestData.Job("done.7z", DownloadJobStatus.Completed));
        _harness.Downloads.AfterListing = () =>
        {
            _harness.Downloads.AfterListing = null;
            _harness.Downloads.Publish(TestData.Job("new.7z", createdAt: TestData.Now.AddMinutes(1)));
        };

        await Screen.ClearFinishedCommand.ExecuteAsync(null);

        Assert.Equal(["new.7z"], Screen.Jobs.Select(job => job.FileName));
    }

    [Fact]
    public async Task ClearFinishedThatFailsIsLogged()
    {
        await _harness.StartWithAsync(TestData.Job(status: DownloadJobStatus.Completed));
        _harness.Downloads.Refusal = new IOException("The database is locked.");

        await Screen.ClearFinishedCommand.ExecuteAsync(null);

        Assert.Single(Screen.Jobs);
        Assert.Equal("Something went wrong. The log has the details.", Screen.Message);
        Assert.StartsWith("Error: Clearing the finished downloads failed System.IO.IOException", Assert.Single(_harness.Logger.Entries), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheWorkEndsWhenTheAppCloses()
    {
        await _harness.StartAsync();

        _harness.Dispose();

        await _harness.Work.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// What <see cref="ILinkDownloads.AddAsync"/> throws for its file name, such as when the link gives none.
    /// </summary>
    private static ArgumentException FileNameRefused(string message, string? fileName = null) => new(message, nameof(fileName));
}
