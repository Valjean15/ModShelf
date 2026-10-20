using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

using ModShelf.Presentation.ViewModels;

namespace ModShelf.Desktop;

public sealed partial class App : Application
{
    private readonly Func<MainWindowViewModel>? _createMainWindowViewModel;

    // Used by the XAML previewer, which runs without the host.
    public App()
    {
    }

    public App(Func<MainWindowViewModel> createMainWindowViewModel)
    {
        _createMainWindowViewModel = createMainWindowViewModel;
    }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            && _createMainWindowViewModel is not null)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = _createMainWindowViewModel(),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
