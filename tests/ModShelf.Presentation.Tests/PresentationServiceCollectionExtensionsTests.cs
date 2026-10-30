using Microsoft.Extensions.DependencyInjection;

using ModShelf.Presentation.Services;
using ModShelf.Presentation.ViewModels;
using ModShelf.Presentation.ViewModels.Accounts;
using ModShelf.Presentation.ViewModels.Browse;
using ModShelf.Presentation.ViewModels.Downloads;
using ModShelf.Presentation.ViewModels.Library;
using ModShelf.Presentation.ViewModels.Settings;
using ModShelf.Presentation.ViewModels.Updates;

namespace ModShelf.Presentation.Tests;

public sealed class PresentationServiceCollectionExtensionsTests
{
    [Fact]
    public void RegistersNavigationTheScreensTheMainWindowAndTheTrayOnce()
    {
        ServiceCollection services = new();
        services.AddPresentation();
        services.AddPresentation();

        Assert.Equal(typeof(NavigationService), Assert.Single(services, descriptor => descriptor.ServiceType == typeof(INavigationService)).ImplementationType);
        Assert.Equal(
            [typeof(LibraryViewModel), typeof(UpdatesViewModel), typeof(BrowseViewModel), typeof(DownloadsViewModel), typeof(AccountsViewModel), typeof(SettingsViewModel)],
            services.Where(descriptor => descriptor.ServiceType == typeof(ScreenViewModel)).Select(descriptor => descriptor.ImplementationType));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(MainWindowViewModel));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(TrayViewModel));
        Assert.All(services, descriptor => Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime));
    }
}
