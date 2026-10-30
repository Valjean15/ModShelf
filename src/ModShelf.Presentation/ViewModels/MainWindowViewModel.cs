using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Microsoft.Extensions.Logging;

using ModShelf.Presentation.Resources;
using ModShelf.Presentation.Services;
using ModShelf.Presentation.ViewModels.Downloads;

namespace ModShelf.Presentation.ViewModels;

/// <summary>
/// The main window: the sidebar of screens, and the screen it shows, which follows <see cref="INavigationService"/>. It
/// starts every screen's own work, and stops it when disposed.
/// </summary>
public sealed partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly INavigationService _navigation;
    private readonly IUiThread _uiThread;
    private readonly ILogger<MainWindowViewModel> _logger;
    private readonly CancellationTokenSource _stopping = new();
    private readonly List<Task> _screensWork = [];

    /// <param name="screens">One of each <see cref="Services.Screen"/>, in any order.</param>
    public MainWindowViewModel(
        INavigationService navigation,
        IUiThread uiThread,
        IEnumerable<ScreenViewModel> screens,
        ILogger<MainWindowViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(navigation);
        ArgumentNullException.ThrowIfNull(uiThread);
        ArgumentNullException.ThrowIfNull(screens);
        ArgumentNullException.ThrowIfNull(logger);

        _navigation = navigation;
        _uiThread = uiThread;
        _logger = logger;
        Screens = [.. screens.OrderBy(screen => screen.Screen)];
        if (!Screens.Select(screen => screen.Screen).SequenceEqual(Enum.GetValues<Screen>()))
        {
            throw new ArgumentException("The main window needs one of each screen.", nameof(screens));
        }

        CurrentScreen = ScreenFor(navigation.Current);
        navigation.Navigated += OnNavigated;
    }

    public string Title { get; } = Strings.AppTitle;

    /// <summary>
    /// Every screen, in the sidebar's order.
    /// </summary>
    public IReadOnlyList<ScreenViewModel> Screens { get; }

    /// <summary>
    /// The screen shown. Setting it, as selecting it in the sidebar does, goes to it.
    /// </summary>
    [ObservableProperty]
    public partial ScreenViewModel CurrentScreen { get; set; }

    /// <summary>
    /// Whether the sidebar shows the screens' names as well as their icons.
    /// </summary>
    [ObservableProperty]
    public partial bool IsSidebarOpen { get; set; } = true;

    /// <summary>
    /// The screens' work, while it runs: done once <see cref="Dispose"/> has stopped it.
    /// </summary>
    public Task ScreensWork => Task.WhenAll(_screensWork);

    /// <summary>
    /// Starts every screen's work (<see cref="ScreenViewModel.StartAsync"/>), once, on the UI thread. Work that fails is
    /// logged, and the other screens' work goes on.
    /// </summary>
    /// <exception cref="InvalidOperationException">The screens have started already.</exception>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(_stopping.IsCancellationRequested, this);
        if (_screensWork.Count > 0)
        {
            throw new InvalidOperationException("The screens have started already.");
        }

        foreach (ScreenViewModel screen in Screens)
        {
            _screensWork.Add(RunAsync(screen));
        }
    }

    /// <summary>
    /// Opens the links among a launch's arguments, such as the <c>nxm://</c> link the system started ModShelf with, or
    /// handed a second launch: on the Downloads screen, which says what became of each. Other arguments, such as a
    /// file's path or a setting, are passed over. Runs on the UI thread.
    /// </summary>
    public async Task OpenLinksAsync(IEnumerable<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        Uri[] links = [.. arguments.Select(LinkIn).OfType<Uri>()];
        if (links.Length == 0)
        {
            return;
        }

        _navigation.NavigateTo(Screen.Downloads);
        DownloadsViewModel downloads = Screens.OfType<DownloadsViewModel>().Single();
        foreach (Uri link in links)
        {
            await downloads.OpenLinkAsync(link, _stopping.Token);
        }
    }

    public void Dispose()
    {
        _navigation.Navigated -= OnNavigated;
        if (!_stopping.IsCancellationRequested)
        {
            _stopping.Cancel();
        }
    }

    [RelayCommand]
    private void Navigate(Screen screen) => _navigation.NavigateTo(screen);

    private async Task RunAsync(ScreenViewModel screen)
    {
        try
        {
            await screen.StartAsync(_stopping.Token);
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
        {
            // The app is closing.
        }
        catch (Exception exception)
        {
            LogScreenFailed(_logger, exception, screen.Screen);
        }
    }

    [RelayCommand]
    private void ToggleSidebar() => IsSidebarOpen = !IsSidebarOpen;

    partial void OnCurrentScreenChanged(ScreenViewModel value)
    {
        // The sidebar can clear its selection; the screen shown stays.
        if (value is null)
        {
            _uiThread.Post(() => CurrentScreen = ScreenFor(_navigation.Current));
            return;
        }

        _navigation.NavigateTo(value.Screen);
    }

    private void OnNavigated(object? sender, Screen screen) => _uiThread.Post(() => CurrentScreen = ScreenFor(_navigation.Current));

    private ScreenViewModel ScreenFor(Screen screen) => Screens[(int)screen];

    // An absolute link, but not to a file: an argument like C:\Mods or /home/jean/mods reads as a file:// link.
    private static Uri? LinkIn(string argument)
        => Uri.TryCreate(argument, UriKind.Absolute, out Uri? link) && !link.IsFile && !link.IsUnc ? link : null;
}
