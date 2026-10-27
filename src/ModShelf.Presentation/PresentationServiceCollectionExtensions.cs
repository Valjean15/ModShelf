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
    /// Registers navigation, every screen's ViewModel and the main window's, each once for the app's lifetime, so a
    /// screen keeps what it shows while another is shown. Needs an <see cref="IUiThread"/> and an
    /// <see cref="IDialogService"/>, which Desktop registers.
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

        return services;
    }
}
