using ModShelf.Application.Downloads;
using ModShelf.Application.Library;
using ModShelf.Domain.Downloads;
using ModShelf.Domain.Mods;
using ModShelf.Presentation.Tests.Common;
using ModShelf.Presentation.ViewModels;
using ModShelf.Sources.Abstractions.Accounts;

namespace ModShelf.Presentation.Tests.ViewModels;

public sealed class DownloadJobViewModelTests : IDisposable
{
    private const long MiB = 1024 * 1024;

    private readonly DownloadsHarness _harness = new();

    public static TheoryData<DownloadJobStatus, string> Statuses => new()
    {
        { DownloadJobStatus.Queued, "Waiting to start" },
        { DownloadJobStatus.Resolving, "Getting the download link" },
        { DownloadJobStatus.AwaitingHandoff, "Waiting for you: start the download on Nexus Mods, then paste the link it hands over" },
        { DownloadJobStatus.Downloading, "Downloading" },
        { DownloadJobStatus.Verifying, "Checking the file" },
        { DownloadJobStatus.Completed, "Done" },
        { DownloadJobStatus.Paused, "Paused" },
        { DownloadJobStatus.Cancelled, "Cancelled" },
    };

    public static TheoryData<DownloadFailureReason, string> Failures => new()
    {
        { DownloadFailureReason.LinkUnavailable, "Failed: the link no longer works, or the server refused it." },
        { DownloadFailureReason.HandoffExpired, "Failed: it wasn't started on the site in time." },
        { DownloadFailureReason.Network, "Failed: the connection kept failing." },
        {
            DownloadFailureReason.Storage,
            "Failed: the file couldn't be saved. The folder may not be writable, the disk may be full, or a file with that name may exist already."
        },
        { DownloadFailureReason.HashMismatch, "Failed: the file doesn't match the expected hash, so it was deleted." },
        {
            DownloadFailureReason.AccountNeeded,
            "Failed: the site needs a linked account, or refused the one linked. Link it on the Accounts screen, then retry."
        },
        { DownloadFailureReason.Unexpected, "Failed unexpectedly. The log has the details." },
        {
            DownloadFailureReason.RateLimited,
            "Failed: the site's rate limit for this account is used up. Retry once it resets; the Accounts screen says when."
        },
    };

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task ShowsTheFileTheFolderAndTheSite()
    {
        DownloadJobSnapshot job = TestData.Job();

        DownloadJobViewModel row = await ShowAsync(job);

        Assert.Equal((job.Id, TestData.FileName, TestData.Folder), (row.Id, row.FileName, row.Folder));
        Assert.Equal(("Nexus Mods", "Open on Nexus Mods"), (row.SiteName, row.OpenPageLabel));
    }

    [Fact]
    public async Task ASiteThatIsntRegisteredGoesByItsId()
    {
        DownloadJobSnapshot job = TestData.Job() with { File = new ModFileRef(new ModRef(new GameRef(new SourceId("modio"), "game"), "1"), "2") };

        DownloadJobViewModel row = await ShowAsync(job);

        Assert.Equal("modio", row.SiteName);
    }

    [Theory]
    [MemberData(nameof(Statuses))]
    public async Task SaysWhereTheDownloadStands(DownloadJobStatus status, string text)
    {
        DownloadJobViewModel row = await ShowAsync(TestData.Job(status: status));

        Assert.Equal((status, text, false), (row.Status, row.StatusText, row.IsFailed));
    }

    [Theory]
    [MemberData(nameof(Failures))]
    public async Task AFailedDownloadSaysWhy(DownloadFailureReason reason, string text)
    {
        DownloadJobViewModel row = await ShowAsync(TestData.Job(status: DownloadJobStatus.Failed) with { FailureReason = reason });

        Assert.Equal((text, true), (row.StatusText, row.IsFailed));
    }

    [Theory]
    [InlineData(DownloadJobStatus.Queued, true, false, false, true)]
    [InlineData(DownloadJobStatus.Resolving, true, false, false, true)]
    [InlineData(DownloadJobStatus.AwaitingHandoff, true, false, false, true)]
    [InlineData(DownloadJobStatus.Downloading, true, false, false, true)]
    [InlineData(DownloadJobStatus.Verifying, false, false, false, true)]
    [InlineData(DownloadJobStatus.Completed, false, false, false, false)]
    [InlineData(DownloadJobStatus.Paused, false, true, false, true)]
    [InlineData(DownloadJobStatus.Failed, false, false, true, true)]
    [InlineData(DownloadJobStatus.Cancelled, false, false, false, false)]
    public async Task OffersWhatTheDownloadAllowsNow(DownloadJobStatus status, bool pause, bool resume, bool retry, bool cancel)
    {
        DownloadJobViewModel row = await ShowAsync(TestData.Job(status: status));

        Assert.Equal((pause, resume, retry, cancel), (row.CanPause, row.CanResume, row.CanRetry, row.CanCancel));
        Assert.Equal(
            (pause, resume, retry, cancel),
            (row.PauseCommand.CanExecute(null), row.ResumeCommand.CanExecute(null), row.RetryCommand.CanExecute(null), row.CancelCommand.CanExecute(null)));
        Assert.Equal(!cancel, row.IsFinished);
        bool waiting = status == DownloadJobStatus.AwaitingHandoff;
        Assert.Equal((waiting, waiting, waiting), (row.IsAwaitingHandoff, row.PasteLinkCommand.CanExecute(null), row.OpenPageCommand.CanExecute(null)));
    }

    [Fact]
    public async Task ShowsHowMuchIsDownloaded()
    {
        using CultureScope culture = new("en-US");

        DownloadJobViewModel row = await ShowAsync(Downloading(TestData.Job(), bytes: (long)(12.3 * MiB), total: (long)(45.6 * MiB)));

        Assert.Equal("12.3 MB of 45.6 MB", row.SizeText);
        Assert.Equal(26.97, row.Progress, precision: 2);
        Assert.False(row.IsProgressIndeterminate);
    }

    [Fact]
    public async Task SizesAreWrittenAsTheCultureWritesNumbers()
    {
        using CultureScope culture = new("es-ES");

        DownloadJobViewModel row = await ShowAsync(Downloading(TestData.Job(), bytes: (long)(1.5 * MiB), total: 3 * MiB));

        Assert.Equal("1,50 MB of 3,00 MB", row.SizeText);
    }

    [Fact]
    public async Task WithoutTheSizeTheProgressCantBeTold()
    {
        using CultureScope culture = new("en-US");
        DownloadJobSnapshot job = TestData.Job(status: DownloadJobStatus.Downloading);
        DownloadJobViewModel row = await ShowAsync(job);
        (string, bool) nothingYet = (row.SizeText, row.IsProgressIndeterminate);

        _harness.Downloads.Publish(Downloading(job, bytes: 512, total: null));

        Assert.Equal((string.Empty, true), nothingYet);
        Assert.Equal(("512 B", 0.0, true), (row.SizeText, row.Progress, row.IsProgressIndeterminate));
    }

    [Theory]
    [InlineData(DownloadJobStatus.Queued, false)]
    [InlineData(DownloadJobStatus.Resolving, true)]
    [InlineData(DownloadJobStatus.Verifying, true)]
    [InlineData(DownloadJobStatus.Paused, false)]
    public async Task TheProgressCantBeToldWhileTheDownloadIsWorkedOnWithoutBytes(DownloadJobStatus status, bool indeterminate)
    {
        DownloadJobViewModel row = await ShowAsync(TestData.Job(status: status) with { BytesDownloaded = MiB, TotalBytes = 2 * MiB });

        Assert.Equal(indeterminate, row.IsProgressIndeterminate);
    }

    [Fact]
    public async Task ADoneDownloadShowsItsSize()
    {
        using CultureScope culture = new("en-US");

        DownloadJobViewModel row = await ShowAsync(TestData.Job(status: DownloadJobStatus.Completed) with { BytesDownloaded = 3 * MiB, TotalBytes = 3 * MiB });

        Assert.Equal(("3.00 MB", 100.0, false), (row.SizeText, row.Progress, row.IsProgressIndeterminate));
    }

    [Fact]
    public async Task ADoneDownloadOfAnUnknownSizeShowsWhatWasDownloaded()
    {
        using CultureScope culture = new("en-US");

        DownloadJobViewModel row = await ShowAsync(TestData.Job(status: DownloadJobStatus.Completed) with { BytesDownloaded = 512 });

        Assert.Equal(("512 B", 100.0), (row.SizeText, row.Progress));
    }

    [Fact]
    public async Task ShowsTheSpeedAndTheTimeLeftOnceThereIsASecondToMeasure()
    {
        using CultureScope culture = new("en-US");
        DownloadJobSnapshot job = Downloading(TestData.Job(), bytes: 16 * MiB, total: 400 * MiB);
        DownloadJobViewModel row = await ShowAsync(job);
        string atFirst = row.SpeedText;

        _harness.Time.Advance(TimeSpan.FromMilliseconds(500));
        _harness.Downloads.Publish(job with { BytesDownloaded = 17 * MiB });
        string afterHalfASecond = row.SpeedText;
        _harness.Time.Advance(TimeSpan.FromMilliseconds(1500));
        _harness.Downloads.Publish(job with { BytesDownloaded = 20 * MiB });

        Assert.Equal((string.Empty, string.Empty), (atFirst, afterHalfASecond));
        Assert.Equal("2.00 MB/s, about 4 min left", row.SpeedText);
    }

    [Theory]
    [InlineData(120, "2.00 MB/s, less than a minute left")]
    [InlineData(20 + (1.536 * 3600 * 2), "2.00 MB/s, about 1.5 h left")]
    [InlineData(20 + 120, "2.00 MB/s, about 1 min left")]
    public async Task TheTimeLeftIsRoundedToWhatMatters(double totalMiB, string text)
    {
        using CultureScope culture = new("en-US");
        DownloadJobSnapshot job = Downloading(TestData.Job(), bytes: 16 * MiB, total: (long)(totalMiB * MiB));
        DownloadJobViewModel row = await ShowAsync(job);

        _harness.Time.Advance(TimeSpan.FromSeconds(2));
        _harness.Downloads.Publish(job with { BytesDownloaded = 20 * MiB });

        Assert.Equal(text, row.SpeedText);
    }

    [Fact]
    public async Task WithoutTheSizeOnlyTheSpeedShows()
    {
        using CultureScope culture = new("en-US");
        DownloadJobSnapshot job = Downloading(TestData.Job(), bytes: 0, total: null);
        DownloadJobViewModel row = await ShowAsync(job);

        _harness.Time.Advance(TimeSpan.FromSeconds(4));
        _harness.Downloads.Publish(job with { BytesDownloaded = 2 * MiB });

        Assert.Equal("512 KB/s", row.SpeedText);
    }

    [Fact]
    public async Task TheSpeedIsOfTheLastFewSeconds()
    {
        using CultureScope culture = new("en-US");
        DownloadJobSnapshot job = Downloading(TestData.Job(), bytes: 0, total: null);
        DownloadJobViewModel row = await ShowAsync(job);

        _harness.Time.Advance(TimeSpan.FromSeconds(10));
        _harness.Downloads.Publish(job with { BytesDownloaded = 10 * MiB });
        _harness.Time.Advance(TimeSpan.FromSeconds(2));
        _harness.Downloads.Publish(job with { BytesDownloaded = 14 * MiB });

        Assert.Equal("2.00 MB/s", row.SpeedText);
    }

    [Fact]
    public async Task ADownloadThatStartsOverIsMeasuredAfresh()
    {
        DownloadJobSnapshot job = Downloading(TestData.Job(), bytes: 50 * MiB, total: null);
        DownloadJobViewModel row = await ShowAsync(job);

        _harness.Time.Advance(TimeSpan.FromSeconds(2));
        _harness.Downloads.Publish(job with { BytesDownloaded = MiB });

        Assert.Equal(string.Empty, row.SpeedText);
    }

    [Fact]
    public async Task TheSpeedShowsOnlyWhileDownloading()
    {
        DownloadJobSnapshot job = Downloading(TestData.Job(), bytes: 0, total: null);
        DownloadJobViewModel row = await ShowAsync(job);
        _harness.Time.Advance(TimeSpan.FromSeconds(2));
        _harness.Downloads.Publish(job with { BytesDownloaded = MiB });
        string downloading = row.SpeedText;

        _harness.Downloads.Publish(job with { Status = DownloadJobStatus.Paused, BytesDownloaded = MiB });
        string paused = row.SpeedText;
        _harness.Time.Advance(TimeSpan.FromSeconds(60));
        _harness.Downloads.Publish(job with { BytesDownloaded = 2 * MiB });

        Assert.NotEqual(string.Empty, downloading);
        Assert.Equal(string.Empty, paused);
        Assert.Equal(string.Empty, row.SpeedText);
    }

    [Theory]
    [InlineData(DownloadJobStatus.Downloading, "Pause", DownloadJobStatus.Paused)]
    [InlineData(DownloadJobStatus.Paused, "Resume", DownloadJobStatus.Queued)]
    [InlineData(DownloadJobStatus.Failed, "Retry", DownloadJobStatus.Queued)]
    public async Task TellsTheQueueWhatTheUserDid(DownloadJobStatus status, string action, DownloadJobStatus after)
    {
        DownloadJobSnapshot job = TestData.Job(status: status);
        DownloadJobViewModel row = await ShowAsync(job);

        await (action switch
        {
            "Pause" => row.PauseCommand,
            "Resume" => row.ResumeCommand,
            _ => row.RetryCommand,
        }).ExecuteAsync(null);

        Assert.Equal([(action, job.Id)], _harness.Downloads.Controls);
        Assert.Equal(after, row.Status);
        Assert.Empty(_harness.Dialogs.Messages);
    }

    [Theory]
    [InlineData(typeof(InvalidOperationException))]
    [InlineData(typeof(KeyNotFoundException))]
    public async Task ADownloadThatChangedMeanwhileIsLeftAsItIs(Type refusal)
    {
        DownloadJobViewModel row = await ShowAsync(TestData.Job(status: DownloadJobStatus.Downloading));
        _harness.Downloads.Refusal = (Exception)Activator.CreateInstance(refusal)!;

        await row.PauseCommand.ExecuteAsync(null);

        Assert.Empty(_harness.Dialogs.Messages);
        Assert.Empty(_harness.Logger.Entries);
    }

    [Fact]
    public async Task WhatFailsUnexpectedlyIsLoggedAndTold()
    {
        DownloadJobSnapshot job = TestData.Job(status: DownloadJobStatus.Paused);
        DownloadJobViewModel row = await ShowAsync(job);
        _harness.Downloads.Refusal = new IOException("The database is locked.");

        await row.ResumeCommand.ExecuteAsync(null);

        Assert.Equal([("That didn't work", "Something went wrong. The log has the details.")], _harness.Dialogs.Messages);
        Assert.StartsWith(
            $"Error: Resume of download job {job.Id} failed System.IO.IOException: The database is locked.",
            Assert.Single(_harness.Logger.Entries),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task CancellingWithNothingDownloadedDoesntAsk()
    {
        DownloadJobSnapshot job = TestData.Job();
        DownloadJobViewModel row = await ShowAsync(job);

        await row.CancelCommand.ExecuteAsync(null);

        Assert.Empty(_harness.Dialogs.Confirmations);
        Assert.Equal([("Cancel", job.Id)], _harness.Downloads.Controls);
        Assert.Equal(DownloadJobStatus.Cancelled, row.Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellingWithBytesDownloadedAsksFirst(bool confirmed)
    {
        using CultureScope culture = new("en-US");
        DownloadJobSnapshot job = TestData.Job(status: DownloadJobStatus.Paused) with { BytesDownloaded = (long)(12.3 * MiB) };
        DownloadJobViewModel row = await ShowAsync(job);
        _harness.Dialogs.Confirm.Enqueue(confirmed);

        await row.CancelCommand.ExecuteAsync(null);

        Assert.Equal(
            [
                (
                    "Stop this download for good?",
                    $"12.3 MB of {TestData.FileName} is downloaded already. Stopping the download for good deletes it. To go on later instead, pause it.",
                    "Stop and delete"
                ),
            ],
            _harness.Dialogs.Confirmations);
        (string, DownloadJobId)[] controls = confirmed ? [("Cancel", job.Id)] : [];
        Assert.Equal(controls, _harness.Downloads.Controls);
        Assert.Equal(confirmed ? DownloadJobStatus.Cancelled : DownloadJobStatus.Paused, row.Status);
    }

    [Fact]
    public async Task OpensThePageWhereTheDownloadStarts()
    {
        DownloadJobViewModel row = await ShowAsync(TestData.Job(status: DownloadJobStatus.AwaitingHandoff));

        await row.OpenPageCommand.ExecuteAsync(null);

        Assert.Equal([TestData.HandoffPage], _harness.Browser.Opened);
        Assert.Empty(_harness.Dialogs.Messages);
    }

    [Fact]
    public async Task WhenNoBrowserOpensThePagesAddressIsShown()
    {
        DownloadJobViewModel row = await ShowAsync(TestData.Job(status: DownloadJobStatus.AwaitingHandoff));
        _harness.Browser.CanOpen = false;

        await row.OpenPageCommand.ExecuteAsync(null);

        Assert.Equal(
            [("Couldn't open the browser", $"Open this page in your browser to start the download: {TestData.HandoffPage.AbsoluteUri}")],
            _harness.Dialogs.Messages);
    }

    [Fact]
    public async Task ThePageCantBeOpenedBeforeTheQueueKnowsIt()
    {
        DownloadJobSnapshot job = TestData.Job(status: DownloadJobStatus.AwaitingHandoff) with { HandoffPage = null };
        DownloadJobViewModel row = await ShowAsync(job);
        bool before = row.OpenPageCommand.CanExecute(null);

        _harness.Downloads.Publish(job with { HandoffPage = TestData.HandoffPage });

        Assert.False(before);
        Assert.True(row.OpenPageCommand.CanExecute(null));
    }

    [Fact]
    public async Task PastingTheLinkHandsItToTheQueue()
    {
        DownloadJobSnapshot job = TestData.Job(status: DownloadJobStatus.AwaitingHandoff);
        DownloadJobViewModel row = await ShowAsync(job);
        _harness.Dialogs.Answers.Enqueue($" {TestData.HandoffLink} ");
        _harness.Links.Answer = (_, _, _) => new LinkDownload(job, AlreadyQueued: true);

        await row.PasteLinkCommand.ExecuteAsync(null);

        Assert.Equal(
            "Paste the link|On Nexus Mods, start the download of " + TestData.FileName + " with the download for mod managers. Its button "
                + "holds the link to paste here, such as one starting with nxm://. Right-click the button, copy the link, and paste it "
                + "here.|nxm://…|Continue",
            string.Join('|', _harness.Dialogs.Questions.Select(question => $"{question.Title}|{question.Message}|{question.Hint}|{question.ConfirmLabel}")));
        Assert.Equal([(TestData.HandoffLink, TestData.Folder, (string?)null)], _harness.Links.Added);
        Assert.Empty(_harness.Library.Added);
        Assert.Empty(_harness.Downloads.Controls);
        Assert.Empty(_harness.Dialogs.Messages);
    }

    [Fact]
    public async Task ADownloadIntoTheLibraryIsHandedItsLinkThroughTheLibrary()
    {
        string folder = Path.Combine(TestData.LibraryRoot, "nexus", "skyrimspecialedition", "SkyUI (12604)");
        DownloadJobSnapshot job = TestData.Job(status: DownloadJobStatus.AwaitingHandoff, folder: folder);
        DownloadJobViewModel row = await ShowAsync(job);
        _harness.Dialogs.Answers.Enqueue(TestData.HandoffLink.AbsoluteUri);
        _harness.Library.Answer = _ => new LibraryDownload(Path.Combine(folder, job.FileName), new LinkDownload(job, AlreadyQueued: true));

        await row.PasteLinkCommand.ExecuteAsync(null);

        Assert.Equal([TestData.HandoffLink], _harness.Library.Added);
        Assert.Empty(_harness.Links.Added);
        Assert.Empty(_harness.Dialogs.Messages);
    }

    [Fact]
    public async Task ALinkToAnotherFileIsRefusedAndWhatItAddedCancelled()
    {
        DownloadJobSnapshot job = TestData.Job(status: DownloadJobStatus.AwaitingHandoff);
        DownloadJobViewModel row = await ShowAsync(job);
        DownloadJobSnapshot other = TestData.Job("other.7z", createdAt: TestData.Now.AddMinutes(1));
        _harness.Dialogs.Answers.Enqueue(TestData.HandoffLink.AbsoluteUri);
        _harness.Links.Answer = (_, _, _) =>
        {
            _harness.Downloads.Publish(other);
            return new LinkDownload(other, AlreadyQueued: false);
        };

        await row.PasteLinkCommand.ExecuteAsync(null);

        Assert.Equal([("Cancel", other.Id)], _harness.Downloads.Controls);
        Assert.Equal(DownloadJobStatus.Cancelled, _harness.Row(other).Status);
        Assert.Equal(
            [("That link didn't work", $"That link is to another file, other.7z. Paste the link for {TestData.FileName}.")],
            _harness.Dialogs.Messages);
    }

    [Fact]
    public async Task ALinkToAnotherFileQueuedEarlierLeavesItsDownload()
    {
        DownloadJobSnapshot job = TestData.Job(status: DownloadJobStatus.AwaitingHandoff);
        DownloadJobSnapshot other = TestData.Job("other.7z", DownloadJobStatus.Paused);
        DownloadJobViewModel row = await ShowAsync(job, other);
        _harness.Dialogs.Answers.Enqueue(TestData.HandoffLink.AbsoluteUri);
        _harness.Links.Answer = (_, _, _) => new LinkDownload(other, AlreadyQueued: true);

        await row.PasteLinkCommand.ExecuteAsync(null);

        Assert.Empty(_harness.Downloads.Controls);
        Assert.Equal("That link didn't work", Assert.Single(_harness.Dialogs.Messages).Title);
    }

    [Fact]
    public async Task ALinkToAnotherFileInTheLibraryAlreadyIsRefused()
    {
        string folder = Path.Combine(TestData.LibraryRoot, "nexus");
        DownloadJobSnapshot job = TestData.Job(status: DownloadJobStatus.AwaitingHandoff, folder: folder);
        DownloadJobViewModel row = await ShowAsync(job);
        _harness.Dialogs.Answers.Enqueue(TestData.HandoffLink.AbsoluteUri);
        _harness.Library.Answer = _ => new LibraryDownload(Path.Combine(folder, "other.7z"), null);

        await row.PasteLinkCommand.ExecuteAsync(null);

        Assert.Equal(
            [("That link didn't work", $"That link is to another file, other.7z. Paste the link for {TestData.FileName}.")],
            _harness.Dialogs.Messages);
    }

    [Fact]
    public async Task PastingWhatIsntALinkIsRefused()
    {
        DownloadJobViewModel row = await ShowAsync(TestData.Job(status: DownloadJobStatus.AwaitingHandoff));
        _harness.Dialogs.Answers.Enqueue("the link");

        await row.PasteLinkCommand.ExecuteAsync(null);

        Assert.Empty(_harness.Links.Added);
        Assert.Equal(
            [("That link didn't work", "That isn't a link. Copy the link the download button on Nexus Mods holds, and paste that.")],
            _harness.Dialogs.Messages);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public async Task PastingNothingDoesNothing(string? answer)
    {
        DownloadJobViewModel row = await ShowAsync(TestData.Job(status: DownloadJobStatus.AwaitingHandoff));
        _harness.Dialogs.Answers.Enqueue(answer);

        await row.PasteLinkCommand.ExecuteAsync(null);

        Assert.Empty(_harness.Links.Added);
        Assert.Empty(_harness.Dialogs.Messages);
    }

    [Fact]
    public async Task ALinkTheSiteRefusesSaysWhy()
    {
        DownloadJobViewModel row = await ShowAsync(TestData.Job(status: DownloadJobStatus.AwaitingHandoff));
        _harness.Dialogs.Answers.Enqueue(TestData.HandoffLink.AbsoluteUri);
        _harness.Links.Refusals.Enqueue(new AuthenticationFailedException());

        await row.PasteLinkCommand.ExecuteAsync(null);

        Assert.Equal(
            [
                (
                    "That link didn't work",
                    "The site needs a linked account to look that link up, or refused the one linked. Link it on the Accounts screen, then try again."
                ),
            ],
            _harness.Dialogs.Messages);
        Assert.Empty(_harness.Logger.Entries);
    }

    [Fact]
    public async Task APastedLinkIsNeverLogged()
    {
        DownloadJobSnapshot job = TestData.Job(status: DownloadJobStatus.AwaitingHandoff);
        DownloadJobViewModel row = await ShowAsync(job);
        _harness.Dialogs.Answers.Enqueue(TestData.HandoffLink.AbsoluteUri);
        _harness.Links.Refusals.Enqueue(new InvalidOperationException("More than one source claims this link."));

        await row.PasteLinkCommand.ExecuteAsync(null);

        string entry = Assert.Single(_harness.Logger.Entries);
        Assert.StartsWith($"Error: PasteLink of download job {job.Id} failed", entry, StringComparison.Ordinal);
        Assert.DoesNotContain("a-key", entry, StringComparison.Ordinal);
        Assert.Equal([("That didn't work", "Something went wrong. The log has the details.")], _harness.Dialogs.Messages);
    }

    private static DownloadJobSnapshot Downloading(DownloadJobSnapshot job, long bytes, long? total)
        => job with { Status = DownloadJobStatus.Downloading, BytesDownloaded = bytes, TotalBytes = total };

    /// <summary>
    /// The Downloads screen's row for <paramref name="job"/>, listed with the other jobs given.
    /// </summary>
    private async Task<DownloadJobViewModel> ShowAsync(DownloadJobSnapshot job, params DownloadJobSnapshot[] others)
    {
        await _harness.StartWithAsync([job, .. others]);
        return _harness.Row(job);
    }
}
