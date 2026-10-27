using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using ModShelf.Presentation.Resources;
using ModShelf.Presentation.Services;

namespace ModShelf.Presentation.ViewModels;

/// <summary>
/// The main window: the sidebar of screens, and the screen it shows, which follows <see cref="INavigationService"/>.
/// </summary>
public sealed partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly INavigationService _navigation;
    private readonly IUiThread _uiThread;

    /// <param name="screens">One of each <see cref="Services.Screen"/>, in any order.</param>
    public MainWindowViewModel(INavigationService navigation, IUiThread uiThread, IEnumerable<ScreenViewModel> screens)
    {
        ArgumentNullException.ThrowIfNull(navigation);
        ArgumentNullException.ThrowIfNull(uiThread);
        ArgumentNullException.ThrowIfNull(screens);

        _navigation = navigation;
        _uiThread = uiThread;
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

    public void Dispose() => _navigation.Navigated -= OnNavigated;

    [RelayCommand]
    private void Navigate(Screen screen) => _navigation.NavigateTo(screen);

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
}
