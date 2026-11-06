using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

using ModShelf.Application.State;
using ModShelf.Presentation.ViewModels;

namespace ModShelf.Desktop.Views;

/// <summary>
/// The main window. It opens where it was left, kept on a screen that's there now, and tells its view model where it is
/// once it's been still for a moment, as it's hidden, and as it closes.
/// </summary>
public sealed partial class MainWindow : Window
{
    // What a window's title bar and borders add to it, which isn't known before it's shown: about what Windows, macOS
    // and the Linux desktops add, so a window that fills its screen keeps its title bar on it.
    private const double FrameWidth = 16;
    private const double FrameHeight = 40;

    private readonly DispatcherTimer _still = new() { Interval = TimeSpan.FromMilliseconds(600) };

    // The window's place while neither maximized nor minimized: where a maximized window goes back to.
    private PixelPoint _normalPosition;
    private Size _normalSize;
    private bool _maximized;
    private bool _placed;

    public MainWindow()
    {
        InitializeComponent();
        _still.Tick += (_, _) => Remember();
        PositionChanged += (_, _) => Moved();
        Resized += (_, _) => Moved();
        Opened += (_, _) =>
        {
            _placed = true;
            Moved();
        };
    }

    /// <summary>
    /// Opens the window where it was left, before it's shown: on the screen it was on, or, with that screen gone, centred
    /// on the main one, and no larger than the screen. Nothing remembered leaves it as a new window.
    /// </summary>
    public void Place(WindowPlacement? remembered)
    {
        ScreenArea[] screens =
        [
            .. Screens.All.Select(screen => new ScreenArea(
                screen.WorkingArea.X, screen.WorkingArea.Y, screen.WorkingArea.Width, screen.WorkingArea.Height, screen.Scaling, screen.IsPrimary)),
        ];
        if (WindowFit.Fit(remembered, screens, MinWidth, MinHeight, FrameWidth, FrameHeight) is not { } placement)
        {
            return;
        }

        WindowStartupLocation = WindowStartupLocation.Manual;
        Width = placement.Width;
        Height = placement.Height;
        Position = new PixelPoint(placement.X, placement.Y);
        _normalPosition = Position;
        _normalSize = new Size(placement.Width, placement.Height);
        _maximized = placement.Maximized;
        if (placement.Maximized)
        {
            WindowState = WindowState.Maximized;
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == WindowStateProperty)
        {
            Moved();
        }
        else if (change.Property == IsVisibleProperty && change.NewValue is false)
        {
            // Hidden in the tray: remembered at once.
            Remember();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        Remember();
        _still.Stop();
        base.OnClosed(e);
    }

    /// <summary>
    /// Keeps the window's place, and remembers it once the window has been still for a moment.
    /// </summary>
    private void Moved()
    {
        if (!_placed)
        {
            return;
        }

        switch (WindowState)
        {
            case WindowState.Normal:
                _normalPosition = Position;
                _normalSize = ClientSize;
                _maximized = false;
                break;
            case WindowState.Maximized:
                _maximized = true;
                break;
            default:
                // Minimized, or full screen: the place to come back to stays.
                break;
        }

        _still.Stop();
        _still.Start();
    }

    private void Remember()
    {
        _still.Stop();
        if (_placed && DataContext is MainWindowViewModel window && _normalSize.Width > 0 && _normalSize.Height > 0)
        {
            window.RememberWindow(new WindowPlacement(_normalPosition.X, _normalPosition.Y, _normalSize.Width, _normalSize.Height, _maximized));
        }
    }
}
