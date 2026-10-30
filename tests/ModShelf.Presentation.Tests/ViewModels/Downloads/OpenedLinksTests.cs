using ModShelf.Application.Downloads;
using ModShelf.Application.Links;
using ModShelf.Domain.Downloads;
using ModShelf.Presentation.ViewModels.Downloads;

namespace ModShelf.Presentation.Tests.ViewModels.Downloads;

/// <summary>
/// A link another app hands over, such as an <c>nxm://</c> link the system opens ModShelf with, said on the Downloads
/// screen as an added link is.
/// </summary>
public sealed class OpenedLinksTests : IDisposable
{
    private readonly DownloadsHarness _harness = new();

    private DownloadsViewModel Screen => _harness.Screen;

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task ANewDownloadIsListedAndSaid()
    {
        await _harness.StartAsync();
        DownloadJobSnapshot job = TestData.Job();
        _harness.Opened.Answer = _ => new OpenedLink(new LinkDownload(job, AlreadyQueued: false), Path.Combine(job.TargetDirectory, job.FileName));
        Screen.Link = "typed, not added yet";

        await Screen.OpenLinkAsync(TestData.HandoffLink, TestContext.Current.CancellationToken);

        Assert.Equal([TestData.HandoffLink], _harness.Opened.Opened);
        Assert.Equal($"Downloading {job.FileName} into {job.TargetDirectory}.", Screen.Message);
        Assert.False(Screen.IsMessageAProblem);
        Assert.Equal([job.Id], Screen.Jobs.Select(row => row.Id));
        Assert.Equal("typed, not added yet", Screen.Link);
    }

    [Fact]
    public async Task ADownloadWaitingForItGoesOn()
    {
        DownloadJobSnapshot waiting = TestData.Job(status: DownloadJobStatus.AwaitingHandoff);
        await _harness.StartWithAsync(waiting);
        _harness.Opened.Answer = _ => new OpenedLink(new LinkDownload(waiting, AlreadyQueued: true), LibraryPath: null);

        await Screen.OpenLinkAsync(TestData.HandoffLink, TestContext.Current.CancellationToken);

        Assert.Equal($"Continuing the download of {waiting.FileName}, added earlier.", Screen.Message);
        Assert.Single(Screen.Jobs);
    }

    [Fact]
    public async Task AFileInTheLibraryAlreadyIsSaid()
    {
        await _harness.StartAsync();
        string path = Path.Combine(TestData.LibraryRoot, "nexus", TestData.FileName);
        _harness.Opened.Answer = _ => new OpenedLink(null, path);

        await Screen.OpenLinkAsync(TestData.HandoffLink, TestContext.Current.CancellationToken);

        Assert.Equal($"{path} is in the library already.", Screen.Message);
        Assert.Empty(Screen.Jobs);
    }

    [Fact]
    public async Task ARefusalIsSaidAsForAnAddedLink()
    {
        await _harness.StartAsync();
        _harness.Opened.Refusals.Enqueue(new DownloadQueueBusyException());

        await Screen.OpenLinkAsync(TestData.HandoffLink, TestContext.Current.CancellationToken);

        Assert.Equal("Another ModShelf process, such as the command line, is running the downloads. Try again once it has finished.", Screen.Message);
        Assert.True(Screen.IsMessageAProblem);
        Assert.Empty(_harness.Logger.Entries);
    }

    [Fact]
    public async Task AnUnexpectedFailureIsLoggedWithoutTheLink()
    {
        await _harness.StartAsync();
        _harness.Opened.Refusals.Enqueue(new InvalidOperationException("Something broke."));

        await Screen.OpenLinkAsync(TestData.HandoffLink, TestContext.Current.CancellationToken);

        Assert.Equal("Something went wrong. The log has the details.", Screen.Message);
        string entry = Assert.Single(_harness.Logger.Entries);
        Assert.StartsWith("Error: Opening a link another app handed over failed System.InvalidOperationException: Something broke.", entry, StringComparison.Ordinal);
        Assert.DoesNotContain("a-key", entry, StringComparison.Ordinal);
    }
}
