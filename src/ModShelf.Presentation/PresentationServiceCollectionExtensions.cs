using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using ModShelf.Presentation.Services;
using ModShelf.Presentation.ViewModels;
using ModShelf.Presentation.ViewModels.Accounts;
using ModShelf.Presentation.ViewModels.Browse;
using ModShelf.Presentation.ViewModels.Downloads;
using ModShelf.Presentation.ViewModels.FirstRun;
using ModShelf.Presentation.ViewModels.Library;
using ModShelf.Presentation.ViewModels.Pictures;
using ModShelf.Presentation.ViewModels.Quota;
using ModShelf.Presentation.ViewModels.QuickSearch;
using ModShelf.Presentation.ViewModels.Settings;
using ModShelf.Presentation.ViewModels.Undo;
using ModShelf.Presentation.ViewModels.Updates;

namespace ModShelf.Presentation;

/// <summary>
/// Registers Presentation's services. The desktop app calls it from its composition root.
/// </summary>
public static class PresentationServiceCollectionExtensions
{
    /// <summary>
    /// Registers navigation, every screen's ViewModel, the main window's and the tray's, each once for the app's
    /// lifetime, so a screen keeps what it shows while another is shown. Needs an <see cref="IUiThread"/>, an
    /// <see cref="IDialogService"/>, an <see cref="IFolderOpener"/>, an <see cref="IThemeSwitcher"/>, an
    /// <see cref="IClipboard"/> and an <see cref="IAppExit"/>, which Desktop registers, Platform's tray host, notices
    /// and ModShelf's releases, and the saved pictures, which Infrastructure's <c>AddPictureCache()</c> registers.
    /// </summary>
    public static IServiceCollection AddPresentation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<INavigationService, NavigationService>();
        services.TryAddSingleton<IHiddenGames, HiddenGames>();
        services.TryAddSingleton<IGameSettingsOpener, GameSettingsOpener>();
        services.TryAddSingleton<IScreenOpener, ScreenOpener>();
        services.TryAddSingleton<ModPictures>();
        services.TryAddSingleton<AccountMenuViewModel>();
        services.TryAddSingleton<UndoBarViewModel>();
        services.TryAddSingleton<QuotaWarningViewModel>();
        services.TryAddSingleton<ISitesInUse, SitesInUse>();
        services.TryAddEnumerable(
        [
            ServiceDescriptor.Singleton<ScreenViewModel, LibraryViewModel>(),
            ServiceDescriptor.Singleton<ScreenViewModel, UpdatesViewModel>(),
            ServiceDescriptor.Singleton<ScreenViewModel, BrowseViewModel>(),
            ServiceDescriptor.Singleton<ScreenViewModel, DownloadsViewModel>(),
            ServiceDescriptor.Singleton<ScreenViewModel, AccountsViewModel>(),
            ServiceDescriptor.Singleton<ScreenViewModel, SettingsViewModel>(),
        ]);
        services.TryAddSingleton<ISettingsIndex>(provider => provider.GetServices<ScreenViewModel>().OfType<SettingsViewModel>().Single());
        services.TryAddSingleton<QuickSearchViewModel>();
        services.TryAddSingleton<PicturesViewModel>();
        services.TryAddSingleton<BrowserFolderViewModel>();
        services.TryAddSingleton<LinksViewModel>();
        services.TryAddSingleton<AgentsViewModel>();
        services.TryAddSingleton<FirstRunViewModel>();
        services.TryAddSingleton<AboutViewModel>();
        services.TryAddSingleton<AppUpdateViewModel>();
        services.TryAddSingleton<MainWindowViewModel>();
        services.TryAddSingleton<TrayViewModel>();

        return services;
    }
}
