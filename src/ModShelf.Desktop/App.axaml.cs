using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

using ModShelf.Application.Desktop.Ports;

using ModShelf.Desktop.Views;
using ModShelf.Desktop.Views.Dialogs;
using ModShelf.Presentation.ViewModels;

namespace ModShelf.Desktop;

// Qualified because the ModShelf.Application namespace would otherwise hide Avalonia's Application class.
public sealed partial class App : Avalonia.Application
{
    private readonly Func<MainWindowViewModel>? _createMainWindowViewModel;
    private readonly ISingleInstance? _instance;
    private readonly MessageDialogContent? _startupProblem;

    // Used by the XAML previewer, which runs without the host.
    public App()
    {
    }

    /// <param name="instance">The app's claim to the library, which tells it of each launch after it.</param>
    internal App(Func<MainWindowViewModel> createMainWindowViewModel, ISingleInstance instance)
    {
        _createMainWindowViewModel = createMainWindowViewModel;
        _instance = instance;
    }

    /// <summary>
    /// The app when it can't start: a window that says why, and closes it.
    /// </summary>
    internal App(MessageDialogContent startupProblem)
    {
        _startupProblem = startupProblem;
    }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime problem && _startupProblem is not null)
        {
            problem.MainWindow = new MessageDialog
            {
                DataContext = _startupProblem,
                ShowInTaskbar = true,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
            };
        }
        else if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            && _createMainWindowViewModel is not null)
        {
            MainWindowViewModel viewModel = _createMainWindowViewModel();
            desktop.MainWindow = new MainWindow
            {
                DataContext = viewModel,
            };

            // The screens' own work, such as watching the download queue, runs while the app does.
            viewModel.Start();

            // Launching ModShelf again shows this app.
            if (_instance is not null)
            {
                _instance.LaunchedAgain += (_, _) => Dispatcher.UIThread.Post(() => ShowMainWindow(desktop));
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Brings the main window to the front: shown, restored if minimized, and active.
    /// </summary>
    private static void ShowMainWindow(IClassicDesktopStyleApplicationLifetime desktop)
    {
        if (desktop.MainWindow is not { } window)
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
}
