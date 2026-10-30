using System.ComponentModel;

using ModShelf.Application.Downloads;
using ModShelf.Application.Links;
using ModShelf.Presentation.Services;
using ModShelf.Presentation.Tests.Fakes;
using ModShelf.Presentation.Tests.ViewModels.Downloads;
using ModShelf.Presentation.ViewModels;
using ModShelf.Testing;

namespace ModShelf.Presentation.Tests.ViewModels;

public sealed class MainWindowViewModelTests : IDisposable
{
    private readonly NavigationService _navigation = new();
    private readonly QueuedUiThread _uiThread = new();
    private readonly RecordingLogger<MainWindowViewModel> _logger = new();
    private readonly MainWindowViewModel _window;

    public MainWindowViewModelTests()
    {
        _window = Window(TestScreen.OneOfEach().Reverse());
    }

    public void Dispose() => _window.Dispose();

    [Fact]
    public void TitleIsTheProductNameFromResources()
    {
        Assert.Equal("ModShelf", _window.Title);
    }

    [Fact]
    public void ListsTheScreensInTheSidebarsOrderAndShowsTheLibraryFirst()
    {
        Assert.Equal(["Library", "Updates", "Browse", "Downloads", "Accounts", "Settings"], _window.Screens.Select(screen => screen.Title));
        Assert.Equal(Screen.Library, _window.CurrentScreen.Screen);
        Assert.True(_window.IsSidebarOpen);
    }

    [Fact]
    public void TheNavigateCommandShowsTheScreen()
    {
        _window.NavigateCommand.Execute(Screen.Downloads);

        Assert.Equal((Screen.Downloads, Screen.Downloads), (_navigation.Current, _window.CurrentScreen.Screen));
    }

    [Fact]
    public void SelectingAScreenGoesToIt()
    {
        _window.CurrentScreen = _window.Screens[(int)Screen.Accounts];

        Assert.Equal(Screen.Accounts, _navigation.Current);
    }

    [Fact]
    public void GoingToAScreenFromElsewhereShowsItOnTheUiThread()
    {
        _uiThread.Inline = false;
        List<string?> changed = [];
        _window.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        _navigation.NavigateTo(Screen.Updates);
        Screen beforeTheUiThreadRan = _window.CurrentScreen.Screen;
        _uiThread.RunPending();

        Assert.Equal(Screen.Library, beforeTheUiThreadRan);
        Assert.Equal(Screen.Updates, _window.CurrentScreen.Screen);
        Assert.Equal([nameof(MainWindowViewModel.CurrentScreen)], changed);
    }

    [Fact]
    public void ASidebarWithNothingSelectedKeepsTheScreen()
    {
        _window.NavigateCommand.Execute(Screen.Browse);

        _window.CurrentScreen = null!;

        Assert.Equal((Screen.Browse, Screen.Browse), (_navigation.Current, _window.CurrentScreen.Screen));
    }

    [Fact]
    public void ToggleSidebarHidesAndShowsTheScreensNames()
    {
        _window.ToggleSidebarCommand.Execute(null);
        bool hidden = _window.IsSidebarOpen;
        _window.ToggleSidebarCommand.Execute(null);

        Assert.False(hidden);
        Assert.True(_window.IsSidebarOpen);
    }

    [Fact]
    public void NeedsOneOfEachScreen()
    {
        Assert.Throws<ArgumentException>(() => Window(TestScreen.OneOfEach().Skip(1)));
        Assert.Throws<ArgumentException>(() => Window([.. TestScreen.OneOfEach(), new TestScreen(Screen.Library)]));
    }

    [Fact]
    public void StopsFollowingNavigationOnceDisposed()
    {
        _window.Dispose();

        _navigation.NavigateTo(Screen.Settings);

        Assert.Equal(Screen.Library, _window.CurrentScreen.Screen);
    }

    [Fact]
    public void AScreensBadgeShowsWhenThereIsSomethingToCount()
    {
        TestScreen screen = new(Screen.Downloads);
        List<string?> changed = [];
        ((INotifyPropertyChanged)screen).PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        screen.Count(2);
        bool shownAtTwo = screen.HasBadge;
        screen.Count(0);

        Assert.True(shownAtTwo);
        Assert.False(screen.HasBadge);
        Assert.Equal(["Badge", "HasBadge", "Badge", "HasBadge"], changed);
    }

    [Fact]
    public void StartsEveryScreensWorkOnce()
    {
        _window.Start();

        Assert.All(_window.Screens, screen => Assert.Equal(1, ((TestScreen)screen).Starts));
        Assert.Throws<InvalidOperationException>(_window.Start);
        Assert.False(_window.ScreensWork.IsCompleted);
    }

    [Fact]
    public async Task DisposingStopsTheScreensWork()
    {
        _window.Start();

        _window.Dispose();
        await _window.ScreensWork.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Empty(_logger.Entries);
        Assert.Throws<ObjectDisposedException>(_window.Start);
    }

    [Fact]
    public async Task WorkThatFailsIsLoggedAndTheOtherScreensGoOn()
    {
        using MainWindowViewModel window = Window(
            [.. TestScreen.OneOfEach().Where(screen => screen.Screen != Screen.Browse),
                new TestScreen(Screen.Browse, _ => Task.FromException(new InvalidOperationException("The site is down.")))]);

        window.Start();
        bool othersGoOn = !window.ScreensWork.IsCompleted;
        window.Dispose();
        await window.ScreensWork.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.True(othersGoOn);
        string entry = Assert.Single(_logger.Entries);
        Assert.StartsWith("Error: The Browse screen's work stopped", entry, StringComparison.Ordinal);
        Assert.Contains("The site is down.", entry, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OpensALaunchsLinksOnTheDownloadsScreen()
    {
        using DownloadsHarness downloads = new();
        DownloadJobSnapshot job = TestData.Job();
        downloads.Opened.Answer = _ => new OpenedLink(new LinkDownload(job, AlreadyQueued: false), Path.Combine(job.TargetDirectory, job.FileName));
        using MainWindowViewModel window = Window([.. TestScreen.OneOfEach().Where(screen => screen.Screen != Screen.Downloads), downloads.Screen]);
        string folder = Path.Combine(Path.GetTempPath(), "Mods");

        await window.OpenLinksAsync(["--ModShelf:DatabasePath=C:\\Test\\library.db", folder, TestData.HandoffLink.OriginalString, "not a link"]);

        Assert.Equal(Screen.Downloads, window.CurrentScreen.Screen);
        Assert.Equal([TestData.HandoffLink], downloads.Opened.Opened);
        Assert.NotNull(downloads.Screen.Message);
    }

    [Fact]
    public async Task ALaunchWithoutLinksStaysWhereItIs()
    {
        using DownloadsHarness downloads = new();
        using MainWindowViewModel window = Window([.. TestScreen.OneOfEach().Where(screen => screen.Screen != Screen.Downloads), downloads.Screen]);

        await window.OpenLinksAsync(["--ModShelf:DatabasePath=/tmp/library.db", "file:///tmp/mod.zip"]);

        Assert.Equal(Screen.Library, window.CurrentScreen.Screen);
        Assert.Empty(downloads.Opened.Opened);
    }

    private MainWindowViewModel Window(IEnumerable<ScreenViewModel> screens) => new(_navigation, _uiThread, screens, _logger);
}
