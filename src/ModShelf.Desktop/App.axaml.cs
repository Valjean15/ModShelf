using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

using ModShelf.Desktop.Views;
using ModShelf.Desktop.Views.Dialogs;
using ModShelf.Presentation.ViewModels;

namespace ModShelf.Desktop;

// Qualified because the ModShelf.Application namespace would otherwise hide Avalonia's Application class.
public sealed partial class App : Avalonia.Application
{
    private readonly Func<MainWindowViewModel>? _createMainWindowViewModel;
    private readonly MessageDialogContent? _startupProblem;

    // Used by the XAML previewer, which runs without the host.
    public App()
    {
    }

    public App(Func<MainWindowViewModel> createMainWindowViewModel)
    {
        _createMainWindowViewModel = createMainWindowViewModel;
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
        }

        base.OnFrameworkInitializationCompleted();
    }
}
