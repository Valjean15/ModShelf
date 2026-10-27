using Microsoft.Extensions.Time.Testing;

using ModShelf.Application.Downloads;
using ModShelf.Presentation.Tests.Fakes;
using ModShelf.Presentation.ViewModels;
using ModShelf.Testing;

namespace ModShelf.Presentation.Tests.ViewModels;

/// <summary>
/// The Downloads screen over fakes, with a Nexus Mods source. Its work starts with <see cref="StartAsync"/> and stops
/// when the harness is disposed.
/// </summary>
internal sealed class DownloadsHarness : IDisposable
{
    private readonly CancellationTokenSource _stopping = new();
    private bool _disposed;

    public DownloadsHarness()
    {
        Screen = new DownloadsViewModel(
            Downloads,
            Library,
            Links,
            Settings,
            Browser,
            Dialogs,
            new FakeSourceRegistry(new FakeSource(TestData.Nexus, "Nexus Mods")),
            UiThread,
            Time,
            Logger);
    }

    public InMemoryDownloadService Downloads { get; } = new();

    public ScriptedLibraryDownloads Library { get; } = new();

    public ScriptedLinkDownloads Links { get; } = new();

    public FakeSettingsService Settings { get; } = new();

    public ScriptedBrowser Browser { get; } = new();

    public ScriptedDialogs Dialogs { get; } = new();

    public QueuedUiThread UiThread { get; } = new();

    public FakeTimeProvider Time { get; } = new(TestData.Now);

    public RecordingLogger<DownloadsViewModel> Logger { get; } = new();

    public DownloadsViewModel Screen { get; }

    /// <summary>
    /// The screen's work, once started.
    /// </summary>
    public Task Work { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// Starts the screen's work, as the main window does, and waits until it has listed the queue and follows it. From
    /// then on, each change the queue publishes reaches the screen before the change returns.
    /// </summary>
    public async Task StartAsync()
    {
        // Off the test's thread, so the work continues wherever the queue publishes, as with no UI in the way.
        Work = Task.Run(() => Screen.StartAsync(_stopping.Token), TestContext.Current.CancellationToken);
        await Downloads.WaitedOn.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Adds <paramref name="jobs"/> to the queue and starts the screen's work.
    /// </summary>
    public async Task StartWithAsync(params DownloadJobSnapshot[] jobs)
    {
        Downloads.Add(jobs);
        await StartAsync();
    }

    /// <summary>
    /// The screen's row for <paramref name="job"/>.
    /// </summary>
    public DownloadJobViewModel Row(DownloadJobSnapshot job) => Assert.Single(Screen.Jobs, row => row.Id == job.Id);

    /// <summary>
    /// Stops the screen's work, as closing the app does. Once is enough.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _stopping.Cancel();
        _stopping.Dispose();
    }
}
