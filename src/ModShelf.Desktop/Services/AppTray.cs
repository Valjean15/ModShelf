using System.ComponentModel;

using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Avalonia.Threading;

using ModShelf.Presentation.Resources;
using ModShelf.Presentation.ViewModels;

namespace ModShelf.Desktop.Services;

/// <summary>
/// The tray icon and its menu, and what closing the main window does: <see cref="TrayViewModel"/> decides, this carries
/// it out. The app runs until it's told to quit, from the tray, the close question, or the setting.
/// </summary>
internal sealed class AppTray : IDisposable
{
    private static readonly Uri IconAsset = new("avares://ModShelf.Desktop/Assets/modshelf-tray.png");

    private readonly Avalonia.Application _app;
    private readonly IClassicDesktopStyleApplicationLifetime _desktop;
    private readonly TrayViewModel _tray;
    private TrayIcon? _icon;
    private bool _quitting;
    private bool _deciding;

    public AppTray(Avalonia.Application app, IClassicDesktopStyleApplicationLifetime desktop, TrayViewModel tray)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(desktop);
        ArgumentNullException.ThrowIfNull(tray);

        _app = app;
        _desktop = desktop;
        _tray = tray;
    }

    /// <summary>
    /// Puts the icon in the tray, and takes over closing <paramref name="window"/>. Runs on the UI thread.
    /// </summary>
    public void Attach(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        _desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        window.Closing += OnClosing;

        NativeMenuItem open = new(Strings.TrayOpen);
        open.Click += (_, _) => ShowMainWindow();
        NativeMenuItem check = new(Strings.TrayCheckNow) { Command = _tray.CheckNowCommand };
        NativeMenuItem quit = new(Strings.QuitModShelf);
        quit.Click += (_, _) => Quit();

        _icon = new TrayIcon
        {
            Icon = new WindowIcon(AssetLoader.Open(IconAsset)),
            ToolTipText = _tray.ToolTip,
            Menu = [open, check, new NativeMenuItemSeparator(), quit],
        };
        _icon.Clicked += (_, _) => ShowMainWindow();
        _tray.PropertyChanged += OnTrayChanged;
        TrayIcon.SetIcons(_app, [_icon]);
    }

    /// <summary>
    /// Brings the main window to the front: shown, restored if minimized, and active.
    /// </summary>
    public void ShowMainWindow()
    {
        if (_desktop.MainWindow is not { } window)
        {
            return;
        }

        window.Show();
        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        window.Activate();
    }

    /// <summary>
    /// Takes the icon out of the tray.
    /// </summary>
    public void Dispose()
    {
        _tray.PropertyChanged -= OnTrayChanged;
        _icon?.Dispose();
        _icon = null;
    }

    /// <summary>
    /// Ends the app: the icon leaves the tray at once, the windows close, and the host stops what runs in the background.
    /// </summary>
    private void Quit()
    {
        _quitting = true;
        Dispose();
        _desktop.Shutdown();
    }

    /// <summary>
    /// Keeps the window open while the tray decides what closing it does, then hides it or quits. Closing for the app or
    /// the system ending goes ahead.
    /// </summary>
    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_quitting || e.CloseReason is WindowCloseReason.ApplicationShutdown or WindowCloseReason.OSShutdown)
        {
            return;
        }

        e.Cancel = true;
        if (sender is Window window && !_deciding)
        {
            _deciding = true;
            _ = CloseAsync(window);
        }
    }

    private async Task CloseAsync(Window window)
    {
        WindowClose close;
        try
        {
            close = await _tray.DecideCloseAsync(CancellationToken.None);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // The user asked to close; quitting does that.
            close = WindowClose.Quit;
        }
        finally
        {
            _deciding = false;
        }

        switch (close)
        {
            case WindowClose.KeepRunningInTray:
                window.Hide();
                break;
            case WindowClose.Quit:
                Quit();
                break;
            default:
                break;
        }
    }

    private void OnTrayChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TrayViewModel.ToolTip))
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (_icon is not null)
                {
                    _icon.ToolTipText = _tray.ToolTip;
                }
            });
        }
    }
}
