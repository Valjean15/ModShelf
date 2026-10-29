using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using ModShelf.Presentation.Services;
using ModShelf.Presentation.ViewModels;

namespace ModShelf.Presentation;

/// <summary>
/// Registers Presentation's services. The desktop app calls it from its composition root.
/// </summary>
public static class PresentationServiceCollectionExtensions
{
    /// <summary>
    /// Registers navigation, every screen's ViewModel, the main window's and the tray's, each once for the app's
    /// lifetime, so a screen keeps what it shows while another is shown. Needs an <see cref="IUiThread"/>, an
    /// <see cref="IDialogService"/>, an <see cref="IFolderOpener"/> and an <see cref="IThemeSwitcher"/>, which Desktop
    /// registers, and Platform's tray host and notices.
    /// </summary>
    public static IServiceCollection AddPresentation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<INavigationService, NavigationService>();
        services.TryAddEnumerable(
        [
            ServiceDescriptor.Singleton<ScreenViewModel, LibraryViewModel>(),
            ServiceDescriptor.Singleton<ScreenViewModel, UpdatesViewModel>(),
            ServiceDescriptor.Singleton<ScreenViewModel, BrowseViewModel>(),
            ServiceDescriptor.Singleton<ScreenViewModel, DownloadsViewModel>(),
            ServiceDescriptor.Singleton<ScreenViewModel, AccountsViewModel>(),
            ServiceDescriptor.Singleton<ScreenViewModel, SettingsViewModel>(),
        ]);
        services.TryAddSingleton<MainWindowViewModel>();
        services.TryAddSingleton<TrayViewModel>();

        return services;
    }
}
