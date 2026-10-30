using Microsoft.Extensions.Time.Testing;

using ModShelf.Application.Downloads;
using ModShelf.Application.Library;
using ModShelf.Application.Links;
using ModShelf.Application.Settings;
using ModShelf.Application.Sources;
using ModShelf.Application.Tests.Fakes;
using ModShelf.Domain.Downloads;
using ModShelf.Testing;

using static ModShelf.Application.Tests.Fakes.FakeModSite;

namespace ModShelf.Application.Tests.Links;

/// <summary>
/// A link another app handed over, such as one from a website's "mod manager download" button: to the download of its
/// file outside the library when one is unfinished, otherwise into the library.
/// </summary>
public sealed class OpenedLinksTests : IDisposable
{
    private const string SkyUi = "12604";
    private const string SkyUi52 = "35407";

    private readonly FakeModSite _site = new();
    private readonly RecordingDownloadService _downloads = new();
    private readonly InMemoryLibraryRepository _library = new();
    private readonly InMemorySettingsStore _store = new();
    private readonly FakeLibraryStorage _storage = new();
    private readonly RecordingLogger<OpenedLinks> _logger = new();
    private readonly SettingsService _settings;
    private readonly LibraryDownloads _libraryDownloads;

    public OpenedLinksTests()
    {
        _settings = new SettingsService(_store, new FakeUserFolders(), _library, new InMemoryDownloadJobRepository([]));
        _libraryDownloads = new LibraryDownloads(
            new SourceRegistry([_site]),
            _downloads,
            _library,
            _settings,
            _storage,
            new LibraryChanges(),
            new FakeTimeProvider(TestData.Now),
            new RecordingLogger<LibraryDownloads>());
        _site.AddMod(SkyUi, "SkyUI");
        _site.AddFile(SkyUi, SkyUi52, "SkyUI_5_2_SE.7z", "SkyUI 5.2", "5.2");
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static string Elsewhere => Path.Combine(Path.GetTempPath(), "ModShelf.Tests", "Saved elsewhere");

    private string SkyUiFolder => Path.Combine(_settings.DefaultLibraryRoot, "fake-mods", "skyrimspecialedition", "SkyUI (12604)");

    public void Dispose()
    {
        _libraryDownloads.Dispose();
        _settings.Dispose();
    }

    [Fact]
    public async Task ADownloadWaitingOutsideTheLibraryGoesOnWithTheLink()
    {
        DownloadJobSnapshot waiting = RecordingDownloadService.Job(File(SkyUi, SkyUi52), Elsewhere, "skyui.7z", DownloadJobStatus.AwaitingHandoff);
        _downloads.Add(waiting);

        OpenedLink opened = await Create().OpenAsync(HandoffLink(SkyUi, SkyUi52), Cancellation);

        Assert.Equal(new OpenedLink(new LinkDownload(waiting, AlreadyQueued: true), LibraryPath: null), opened);
        Assert.Equal((File(SkyUi, SkyUi52), HandoffLink(SkyUi, SkyUi52)), Assert.Single(_downloads.HandedOff));
        Assert.Empty(_downloads.Enqueued);
    }

    [Theory]
    [InlineData(DownloadJobStatus.Paused, nameof(IDownloadService.ResumeAsync))]
    [InlineData(DownloadJobStatus.Failed, nameof(IDownloadService.RetryAsync))]
    public async Task AStoppedDownloadOutsideTheLibraryGoesOnToo(DownloadJobStatus status, string goesOn)
    {
        _downloads.Add(RecordingDownloadService.Job(File(SkyUi, SkyUi52), Elsewhere, "skyui.7z", status));

        OpenedLink opened = await Create().OpenAsync(HandoffLink(SkyUi, SkyUi52), Cancellation);

        Assert.True(opened.Download?.AlreadyQueued);
        Assert.Contains(goesOn, _downloads.Changes);
        Assert.Empty(_downloads.Enqueued);
    }

    [Fact]
    public async Task WithNothingWaitingTheFileGoesIntoTheLibrary()
    {
        OpenedLink opened = await Create().OpenAsync(HandoffLink(SkyUi, SkyUi52), Cancellation);

        string expected = Path.Combine(SkyUiFolder, "SkyUI_5_2_SE.7z");
        Assert.Equal(expected, opened.LibraryPath);
        DownloadJobSnapshot job = Assert.Single(_downloads.Enqueued);
        Assert.Equal(new LinkDownload(job, AlreadyQueued: false), opened.Download);
        Assert.Equal(expected, Path.Combine(job.TargetDirectory, job.FileName));
        Assert.Equal((File(SkyUi, SkyUi52), HandoffLink(SkyUi, SkyUi52)), Assert.Single(_downloads.HandedOff));
    }

    [Fact]
    public async Task ADownloadWaitingInTheLibraryGoesOnThere()
    {
        DownloadJobSnapshot waiting = RecordingDownloadService.Job(File(SkyUi, SkyUi52), SkyUiFolder, "SkyUI_5_2_SE.7z", DownloadJobStatus.AwaitingHandoff);
        _downloads.Add(waiting);

        OpenedLink opened = await Create().OpenAsync(HandoffLink(SkyUi, SkyUi52), Cancellation);

        Assert.Equal(new OpenedLink(new LinkDownload(waiting, AlreadyQueued: true), Path.Combine(SkyUiFolder, "SkyUI_5_2_SE.7z")), opened);
        Assert.Empty(_downloads.Enqueued);
    }

    [Theory]
    [InlineData(DownloadJobStatus.Completed)]
    [InlineData(DownloadJobStatus.Cancelled)]
    public async Task AFinishedDownloadOutsideTheLibraryDoesntCount(DownloadJobStatus status)
    {
        _downloads.Add(RecordingDownloadService.Job(File(SkyUi, SkyUi52), Elsewhere, "skyui.7z", status));

        OpenedLink opened = await Create().OpenAsync(HandoffLink(SkyUi, SkyUi52), Cancellation);

        Assert.Equal(Path.Combine(SkyUiFolder, "SkyUI_5_2_SE.7z"), opened.LibraryPath);
        Assert.Single(_downloads.Enqueued);
    }

    [Fact]
    public async Task ADownloadOfAnotherFileDoesntCount()
    {
        _site.AddFile(SkyUi, "35408", "SkyUI_5_3_SE.7z", "SkyUI 5.3", "5.3");
        _downloads.Add(RecordingDownloadService.Job(File(SkyUi, "35408"), Elsewhere, "skyui.7z", DownloadJobStatus.AwaitingHandoff));

        OpenedLink opened = await Create().OpenAsync(HandoffLink(SkyUi, SkyUi52), Cancellation);

        Assert.NotNull(opened.LibraryPath);
        Assert.Equal(File(SkyUi, SkyUi52), Assert.Single(_downloads.Enqueued).File);
    }

    [Fact]
    public async Task ALinkNoSourceKnowsIsRefused()
    {
        await Assert.ThrowsAsync<UnsupportedLinkException>(() => Create().OpenAsync(new Uri("other://somewhere/1"), Cancellation));
        Assert.Empty(_downloads.Changes);
    }

    [Fact]
    public async Task LogsWhereTheLinkWentButNeverTheLink()
    {
        _downloads.Add(RecordingDownloadService.Job(File(SkyUi, SkyUi52), Elsewhere, "skyui.7z", DownloadJobStatus.AwaitingHandoff));
        await Create().OpenAsync(HandoffLink(SkyUi, SkyUi52), Cancellation);

        Assert.StartsWith(
            "Information: A fake-mods link another app handed over went to a download outside the library",
            Assert.Single(_logger.Entries),
            StringComparison.Ordinal);
        Assert.DoesNotContain(_logger.Entries, entry => entry.Contains("key=", StringComparison.Ordinal) || entry.Contains("fakemods:", StringComparison.Ordinal));
    }

    private OpenedLinks Create()
        => new(
            new SourceRegistry([_site]),
            _downloads,
            new LinkDownloads(new SourceRegistry([_site]), _downloads, _storage),
            _libraryDownloads,
            _settings,
            _logger);
}
