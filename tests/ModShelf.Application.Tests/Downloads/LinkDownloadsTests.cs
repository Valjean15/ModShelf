using ModShelf.Application.Downloads;
using ModShelf.Application.Sources;
using ModShelf.Application.Tests.Fakes;
using ModShelf.Domain.Downloads;
using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions;

using static ModShelf.Domain.Downloads.DownloadJobStatus;

namespace ModShelf.Application.Tests.Downloads;

public sealed class LinkDownloadsTests
{
    private const string FileName = "SkyUI_5_2_SE.7z";

    private static readonly Uri Link = new("https://www.example.com/files/SkyUI_5_2_SE.7z?token=s3cr3t");
    private static readonly ModFileRef NexusFile = TestData.File(TestData.Nexus, "35407");
    private static readonly ModFileRef DirectFile = TestData.File(new SourceId("direct"), Link.AbsoluteUri);

    public static TheoryData<DownloadJobStatus> UnfinishedStatuses
        => new(Enum.GetValues<DownloadJobStatus>().Where(status => status is not (Completed or Cancelled)));

    [Fact]
    public async Task QueuesTheFileTheSourceRecognized()
    {
        RecordingDownloadService downloads = new();

        LinkDownload result = await AddAsync(downloads, [ScriptedLinkRecognizer.Always(NexusFile)]);

        DownloadJobSnapshot added = Assert.Single(downloads.Enqueued);
        Assert.Equal(NexusFile, added.File);
        Assert.Equal(TestData.TargetDirectory, added.TargetDirectory);
        Assert.Equal(FileName, added.FileName);
        Assert.Same(added, result.Job);
        Assert.False(result.AlreadyQueued);
    }

    [Fact]
    public async Task KeepsAnOrdinaryLinkFromTheQueuesHandoffs()
    {
        RecordingDownloadService downloads = new();

        await AddAsync(downloads, [ScriptedLinkRecognizer.Always(NexusFile)]);

        Assert.Empty(downloads.HandedOff);
    }

    [Fact]
    public async Task HandsALinkThatCompletesAHandoffToTheQueueBeforeAddingTheJob()
    {
        RecordingDownloadService downloads = new();

        LinkDownload result = await AddAsync(downloads, [ScriptedLinkRecognizer.Always(NexusFile, isHandoff: true)]);

        Assert.Equal((NexusFile, Link), Assert.Single(downloads.HandedOff));
        string[] changes = [nameof(IDownloadService.HandOffAsync), nameof(IDownloadService.EnqueueAsync)];
        Assert.Equal(changes, downloads.Changes);
        Assert.False(result.AlreadyQueued);
    }

    [Fact]
    public async Task HandsALinkThatCompletesAHandoffToTheQueueWhenTheJobIsWaitingForIt()
    {
        DownloadJobSnapshot waiting = RecordingDownloadService.Job(NexusFile, TestData.TargetDirectory, FileName, AwaitingHandoff);
        RecordingDownloadService downloads = new(waiting);

        LinkDownload result = await AddAsync(downloads, [ScriptedLinkRecognizer.Always(NexusFile, isHandoff: true)]);

        Assert.Equal((NexusFile, Link), Assert.Single(downloads.HandedOff));
        Assert.True(result.AlreadyQueued);
        Assert.Same(waiting, result.Job);
        Assert.Empty(downloads.Enqueued);
    }

    [Fact]
    public async Task HandsNothingOverWhenTheHandoffLinkCantBeQueued()
    {
        RecordingDownloadService downloads = new();

        await Assert.ThrowsAsync<ArgumentException>(() => AddAsync(
            downloads,
            [ScriptedLinkRecognizer.Always(NexusFile, suggestedFileName: null, isHandoff: true)]));

        Assert.Empty(downloads.Changes);
    }

    [Fact]
    public async Task TheGivenFileNameWinsOverTheSuggestion()
    {
        RecordingDownloadService downloads = new();

        await AddAsync(downloads, [ScriptedLinkRecognizer.Always(NexusFile)], fileName: "SkyUI.7z");

        Assert.Equal("SkyUI.7z", Assert.Single(downloads.Enqueued).FileName);
    }

    [Theory]
    [InlineData("nul.zip", "_nul.zip")]
    [InlineData(" SkyUI.7z.", "SkyUI.7z")]
    public async Task AFileNameIsMadeToWorkOnEverySystem(string given, string saved)
    {
        // As the library's are: a name Windows keeps for a device would write to the device, and one with a trailing
        // dot wouldn't be found again.
        RecordingDownloadService downloads = new();

        await AddAsync(downloads, [ScriptedLinkRecognizer.Always(NexusFile)], fileName: given);

        Assert.Equal(saved, Assert.Single(downloads.Enqueued).FileName);
    }

    [Fact]
    public async Task AFileNameTooLongIsShortenedKeepingItsExtension()
    {
        // A name the system takes can still be too long once ".part" is added while it downloads.
        RecordingDownloadService downloads = new();

        await AddAsync(downloads, [ScriptedLinkRecognizer.Always(NexusFile)], fileName: new string('a', 252) + ".7z");

        string saved = Assert.Single(downloads.Enqueued).FileName;
        Assert.EndsWith(".7z", saved, StringComparison.Ordinal);
        Assert.True(saved.Length <= ModShelf.Domain.Library.LibraryNames.MaxFileBytes);
    }

    [Fact]
    public async Task ASiteSpecificMatchWinsOverACatchAll()
    {
        RecordingDownloadService downloads = new();

        await AddAsync(
            downloads,
            [ScriptedLinkRecognizer.Always(DirectFile, isCatchAll: true), ScriptedLinkRecognizer.Always(NexusFile)]);

        Assert.Equal(NexusFile, Assert.Single(downloads.Enqueued).File);
    }

    [Fact]
    public async Task ACatchAllMatchIsUsedWhenNoSiteRecognizesTheLink()
    {
        RecordingDownloadService downloads = new();

        await AddAsync(
            downloads,
            [ScriptedLinkRecognizer.Never(TestData.Nexus), ScriptedLinkRecognizer.Always(DirectFile, isCatchAll: true)]);

        Assert.Equal(DirectFile, Assert.Single(downloads.Enqueued).File);
    }

    [Fact]
    public async Task ThrowsWhenNoSourceRecognizesTheLink()
    {
        RecordingDownloadService downloads = new();

        UnsupportedLinkException exception = await Assert.ThrowsAsync<UnsupportedLinkException>(
            () => AddAsync(downloads, [ScriptedLinkRecognizer.Never(TestData.Nexus)]));

        Assert.DoesNotContain("s3cr3t", exception.Message, StringComparison.Ordinal);
        Assert.Empty(downloads.Enqueued);
    }

    [Fact]
    public async Task ThrowsWhenTwoSitesClaimTheLink()
    {
        RecordingDownloadService downloads = new();

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() => AddAsync(
            downloads,
            [ScriptedLinkRecognizer.Always(NexusFile), ScriptedLinkRecognizer.Always(TestData.File(TestData.ModIo, "1"))]));

        Assert.Contains("nexus", exception.Message, StringComparison.Ordinal);
        Assert.Contains("mod-io", exception.Message, StringComparison.Ordinal);
        Assert.Empty(downloads.Enqueued);
    }

    [Fact]
    public async Task NeedsAFileNameWhenTheLinkSuggestsNone()
    {
        RecordingDownloadService downloads = new();

        await Assert.ThrowsAsync<ArgumentException>(
            () => AddAsync(downloads, [ScriptedLinkRecognizer.Always(NexusFile, suggestedFileName: null)]));

        Assert.Empty(downloads.Enqueued);
    }

    [Fact]
    public async Task RejectsARelativeLink()
    {
        LinkDownloads links = new(new SourceRegistry([ScriptedLinkRecognizer.Always(NexusFile)]), new RecordingDownloadService(), new FakeLibraryStorage());

        await Assert.ThrowsAsync<ArgumentException>(() => links.AddAsync(
            new Uri("files/SkyUI.7z", UriKind.Relative), TestData.TargetDirectory, null, TestContext.Current.CancellationToken));
    }

    [Theory]
    [MemberData(nameof(UnfinishedStatuses))]
    public async Task ReturnsTheUnfinishedJobInsteadOfAddingItAgain(DownloadJobStatus status)
    {
        DownloadJobSnapshot earlier = RecordingDownloadService.Job(NexusFile, TestData.TargetDirectory, FileName, status);
        RecordingDownloadService downloads = new(earlier);

        LinkDownload result = await AddAsync(downloads, [ScriptedLinkRecognizer.Always(NexusFile)]);

        Assert.True(result.AlreadyQueued);
        Assert.Equal(earlier.Id, result.Job.Id);
        Assert.Empty(downloads.Enqueued);
    }

    [Theory]
    [InlineData(Paused, nameof(IDownloadService.ResumeAsync))]
    [InlineData(Failed, nameof(IDownloadService.RetryAsync))]
    public async Task AddingTheLinkAgainGoesOnWithAJobThatStopped(DownloadJobStatus status, string control)
    {
        // Wherever the link is added again, as the CLI, the Downloads screen, Browse and Updates all do.
        DownloadJobSnapshot earlier = RecordingDownloadService.Job(NexusFile, TestData.TargetDirectory, FileName, status);
        RecordingDownloadService downloads = new(earlier);

        LinkDownload result = await AddAsync(downloads, [ScriptedLinkRecognizer.Always(NexusFile)]);

        Assert.Equal([control], downloads.Changes);
        Assert.Equal(Queued, result.Job.Status);
        Assert.True(result.AlreadyQueued);
    }

    [Fact]
    public async Task AJobThatWentOnMeanwhileIsReturnedAsItIs()
    {
        DownloadJobSnapshot earlier = RecordingDownloadService.Job(NexusFile, TestData.TargetDirectory, FileName, Paused);
        RecordingDownloadService downloads = new(earlier) { ControlRefusal = new InvalidOperationException("It's Downloading already.") };

        LinkDownload result = await AddAsync(downloads, [ScriptedLinkRecognizer.Always(NexusFile)]);

        Assert.Equal(earlier.Id, result.Job.Id);
        Assert.True(result.AlreadyQueued);
    }

    [Theory]
    [InlineData(Queued)]
    [InlineData(Downloading)]
    [InlineData(AwaitingHandoff)]
    public async Task AJobUnderWayIsLeftAsItIs(DownloadJobStatus status)
    {
        DownloadJobSnapshot earlier = RecordingDownloadService.Job(NexusFile, TestData.TargetDirectory, FileName, status);
        RecordingDownloadService downloads = new(earlier);

        await AddAsync(downloads, [ScriptedLinkRecognizer.Always(NexusFile)]);

        Assert.Empty(downloads.Changes);
    }

    [Fact]
    public async Task AHandoffLinkForTheWaitingDownloadsFileIsTaken()
    {
        DownloadJobSnapshot waiting = RecordingDownloadService.Job(NexusFile, TestData.TargetDirectory, FileName, AwaitingHandoff);
        RecordingDownloadService downloads = new(waiting);

        HandoffResult result = await HandOverAsync(downloads, waiting.Id, ScriptedLinkRecognizer.Always(NexusFile, isHandoff: true));

        Assert.Equal(new HandoffResult(HandoffAnswer.Taken), result);
        Assert.Equal((NexusFile, Link), Assert.Single(downloads.HandedOff));
        Assert.Empty(downloads.Enqueued);
    }

    [Fact]
    public async Task ALinkToTheFileThatIsntTheSitesHandoffIsSaidToBeSo()
    {
        // Such as the file's page, pasted where the link the site hands over goes.
        DownloadJobSnapshot waiting = RecordingDownloadService.Job(NexusFile, TestData.TargetDirectory, FileName, AwaitingHandoff);
        RecordingDownloadService downloads = new(waiting);

        HandoffResult result = await HandOverAsync(downloads, waiting.Id, ScriptedLinkRecognizer.Always(NexusFile));

        Assert.Equal(new HandoffResult(HandoffAnswer.NotAHandoff), result);
        Assert.Empty(downloads.Changes);
    }

    [Fact]
    public async Task ALinkToAnotherFileAddsNothingAndNamesIt()
    {
        DownloadJobSnapshot waiting = RecordingDownloadService.Job(NexusFile, TestData.TargetDirectory, FileName, AwaitingHandoff);
        RecordingDownloadService downloads = new(waiting);
        ScriptedLinkRecognizer other = ScriptedLinkRecognizer.Always(TestData.File(TestData.Nexus, "99"), isHandoff: true);

        HandoffResult result = await HandOverAsync(downloads, waiting.Id, other);

        Assert.Equal(HandoffAnswer.OtherFile, result.Answer);
        Assert.Equal(FileName, result.OtherFileName);
        Assert.Empty(downloads.Changes);
    }

    [Fact]
    public async Task HandingOverToADownloadThatIsntThereSaysSo()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => HandOverAsync(new RecordingDownloadService(), DownloadJobId.New(), ScriptedLinkRecognizer.Always(NexusFile, isHandoff: true)));
    }

    [Fact]
    public async Task TheSameFolderWrittenAnotherWayFindsTheUnfinishedJob()
    {
        // Typed with a trailing separator; and where case doesn't count, in another case.
        string folder = TestData.TargetDirectory + Path.DirectorySeparatorChar;
        if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
        {
            folder = folder.ToUpperInvariant();
        }

        DownloadJobSnapshot earlier = RecordingDownloadService.Job(NexusFile, folder, FileName, Queued);
        RecordingDownloadService downloads = new(earlier);

        LinkDownload result = await AddAsync(downloads, [ScriptedLinkRecognizer.Always(NexusFile)]);

        Assert.True(result.AlreadyQueued);
        Assert.Empty(downloads.Enqueued);
    }

    [Theory]
    [InlineData(Completed)]
    [InlineData(Cancelled)]
    public async Task AddsTheLinkAgainOnceTheEarlierJobIsFinished(DownloadJobStatus status)
    {
        RecordingDownloadService downloads = new(
            RecordingDownloadService.Job(NexusFile, TestData.TargetDirectory, FileName, status));

        LinkDownload result = await AddAsync(downloads, [ScriptedLinkRecognizer.Always(NexusFile)]);

        Assert.False(result.AlreadyQueued);
        Assert.Single(downloads.Enqueued);
    }

    [Theory]
    [InlineData("Elsewhere", FileName)]
    [InlineData(null, "SkyUI-copy.7z")]
    public async Task AddsTheSameFileToAnotherFolderOrUnderAnotherName(string? otherFolder, string otherName)
    {
        string folder = otherFolder is null ? TestData.TargetDirectory : Path.Combine(TestData.TargetDirectory, otherFolder);
        RecordingDownloadService downloads = new(RecordingDownloadService.Job(NexusFile, folder, otherName, Queued));

        LinkDownload result = await AddAsync(downloads, [ScriptedLinkRecognizer.Always(NexusFile)]);

        Assert.False(result.AlreadyQueued);
        Assert.Single(downloads.Enqueued);
    }

    [Fact]
    public async Task AFileAFinishedJobSavedThereIsntDownloadedAgain()
    {
        DownloadJobSnapshot finished = RecordingDownloadService.Job(NexusFile, TestData.TargetDirectory, FileName, Completed);
        RecordingDownloadService downloads = new(finished);
        FakeLibraryStorage storage = new();
        storage.Add(Path.Combine(TestData.TargetDirectory, FileName));

        LinkDownload result = await AddAsync(downloads, [ScriptedLinkRecognizer.Always(NexusFile, isHandoff: true)], storage: storage);

        Assert.Equal(new LinkDownload(finished, AlreadyQueued: false, AlreadySaved: true), result);
        Assert.Empty(downloads.Changes);
    }

    [Fact]
    public async Task AFileAFinishedJobSavedButThatsGoneSinceIsDownloadedAgain()
    {
        RecordingDownloadService downloads = new(RecordingDownloadService.Job(NexusFile, TestData.TargetDirectory, FileName, Completed));

        LinkDownload result = await AddAsync(downloads, [ScriptedLinkRecognizer.Always(NexusFile)]);

        Assert.False(result.AlreadySaved);
        Assert.Same(Assert.Single(downloads.Enqueued), result.Job);
    }

    [Theory]
    [InlineData("Elsewhere", FileName)]
    [InlineData(null, "SkyUI-copy.7z")]
    public async Task AFileSavedInAnotherFolderOrUnderAnotherNameIsDownloadedHere(string? otherFolder, string otherName)
    {
        string folder = otherFolder is null ? TestData.TargetDirectory : Path.Combine(TestData.TargetDirectory, otherFolder);
        RecordingDownloadService downloads = new(RecordingDownloadService.Job(NexusFile, folder, otherName, Completed));
        FakeLibraryStorage storage = new();
        storage.Add(Path.Combine(folder, otherName));
        storage.Add(Path.Combine(TestData.TargetDirectory, FileName));

        LinkDownload result = await AddAsync(downloads, [ScriptedLinkRecognizer.Always(NexusFile)], storage: storage);

        Assert.False(result.AlreadySaved);
        Assert.Single(downloads.Enqueued);
    }

    private static Task<HandoffResult> HandOverAsync(RecordingDownloadService downloads, DownloadJobId waiting, IModSource source)
        => new LinkDownloads(new SourceRegistry([source]), downloads, new FakeLibraryStorage())
            .HandOverAsync(waiting, Link, TestContext.Current.CancellationToken);

    private static Task<LinkDownload> AddAsync(
        RecordingDownloadService downloads,
        IModSource[] sources,
        string? fileName = null,
        FakeLibraryStorage? storage = null)
        => new LinkDownloads(new SourceRegistry(sources), downloads, storage ?? new FakeLibraryStorage())
            .AddAsync(Link, TestData.TargetDirectory, fileName, TestContext.Current.CancellationToken);
}
