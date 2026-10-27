using System.ComponentModel;

using ModShelf.Presentation.Services;
using ModShelf.Presentation.Tests.Fakes;
using ModShelf.Presentation.ViewModels;

namespace ModShelf.Presentation.Tests.ViewModels;

public sealed class MainWindowViewModelTests : IDisposable
{
    private readonly NavigationService _navigation = new();
    private readonly QueuedUiThread _uiThread = new();
    private readonly MainWindowViewModel _window;

    public MainWindowViewModelTests()
    {
        _window = new MainWindowViewModel(_navigation, _uiThread, AllScreens().Reverse());
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
        Assert.Throws<ArgumentException>(() => new MainWindowViewModel(_navigation, _uiThread, AllScreens().Skip(1)));
        Assert.Throws<ArgumentException>(() => new MainWindowViewModel(_navigation, _uiThread, [.. AllScreens(), new LibraryViewModel()]));
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
        CountingScreen screen = new();
        List<string?> changed = [];
        ((INotifyPropertyChanged)screen).PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        screen.Count(2);
        bool shownAtTwo = screen.HasBadge;
        screen.Count(0);

        Assert.True(shownAtTwo);
        Assert.False(screen.HasBadge);
        Assert.Equal(["Badge", "HasBadge", "Badge", "HasBadge"], changed);
    }

    private static IEnumerable<ScreenViewModel> AllScreens() =>
    [
        new LibraryViewModel(),
        new UpdatesViewModel(),
        new BrowseViewModel(),
        new DownloadsViewModel(),
        new AccountsViewModel(),
        new SettingsViewModel(),
    ];

    private sealed class CountingScreen : ScreenViewModel
    {
        public override Screen Screen => Screen.Downloads;

        public override string Title => "Counting";

        public void Count(int count) => Badge = count;
    }
}
