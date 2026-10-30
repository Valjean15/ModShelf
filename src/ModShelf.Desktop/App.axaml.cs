using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

using ModShelf.Application.Desktop.Ports;
using ModShelf.Desktop.Hosting;
using ModShelf.Desktop.Views;
using ModShelf.Desktop.Views.Dialogs;
using ModShelf.Presentation.ViewModels;

namespace ModShelf.Desktop;

// Qualified because the ModShelf.Application namespace would otherwise hide Avalonia's Application class.
public sealed partial class App : Avalonia.Application
{
    private readonly Func<MainWindowViewModel>? _createMainWindowViewModel;
    private readonly Func<TrayViewModel>? _createTrayViewModel;
    private readonly ISingleInstance? _instance;
    private readonly IReadOnlyList<string> _arguments = [];
    private readonly MessageDialogContent? _startupProblem;

    // Used by the XAML previewer, which runs without the host.
    public App()
    {
    }

    /// <param name="instance">The app's claim to the library, which tells it of each launch after it.</param>
    /// <param name="arguments">The app's command-line arguments, such as a link the system started it with.</param>
    internal App(
        Func<MainWindowViewModel> createMainWindowViewModel,
        Func<TrayViewModel> createTrayViewModel,
        ISingleInstance instance,
        IReadOnlyList<string> arguments)
    {
        _createMainWindowViewModel = createMainWindowViewModel;
        _createTrayViewModel = createTrayViewModel;
        _instance = instance;
        _arguments = arguments;
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
            && _createMainWindowViewModel is not null
            && _createTrayViewModel is not null
            && _instance is not null)
        {
            MainWindowViewModel viewModel = _createMainWindowViewModel();
            MainWindow window = new() { DataContext = viewModel };
            desktop.MainWindow = window;

            // The screens' own work, such as watching the download queue, runs while the app does, with or without
            // its window.
            viewModel.Start();
            AppTray tray = new(this, desktop, _createTrayViewModel());
            tray.Attach(window);

            // Launching ModShelf again shows this app, and opens the links it was launched with, such as an nxm:// link
            // the system opened with ModShelf while it ran. On macOS, the system hands a link to the app running instead.
            _instance.LaunchedAgain += (_, launch) => Dispatcher.UIThread.Post(() => Open(launch.Arguments));
            if (TryGetFeature(typeof(IActivatableLifetime)) is IActivatableLifetime activatable)
            {
                activatable.Activated += (_, activated) =>
                {
                    if (activated is ProtocolActivatedEventArgs { Kind: ActivationKind.OpenUri } opened)
                    {
                        Dispatcher.UIThread.Post(() => Open([opened.Uri.OriginalString]));
                    }
                };
            }

            // The links the system started it with.
            _ = viewModel.OpenLinksAsync(_arguments);

            void Open(IReadOnlyList<string> arguments)
            {
                tray.ShowMainWindow();
                _ = viewModel.OpenLinksAsync(arguments);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
