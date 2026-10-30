using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using ModShelf.Application.Accounts.Ports;
using ModShelf.Application.Desktop.Ports;
using ModShelf.Application.Links;
using ModShelf.Application.Links.Ports;
using ModShelf.Application.Notifications.Ports;
using ModShelf.Application.Settings.Ports;
using ModShelf.Platform.Browser;
using ModShelf.Platform.Instances;
using ModShelf.Platform.Links;
using ModShelf.Platform.OAuth;
using ModShelf.Platform.Secrets;
using ModShelf.Platform.Tray;
using ModShelf.Testing;

namespace ModShelf.Platform.Tests;

/// <summary>
/// Checks the registrations by calling their factories against a stub container, since this test project
/// has the DI abstractions but not the container itself.
/// </summary>
public sealed class PlatformServiceCollectionExtensionsTests : IDisposable
{
    private readonly TempFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    [Fact]
    public void RegistersTheSecretStoreListenerAndLauncher()
    {
        ServiceCollection services = new();
        services.AddPlatformServices(new SecretStoreOptions { Folder = _folder.Path });
        StubServiceProvider provider = new();

        object store = Single<ISecretStore>(services).ImplementationFactory!(provider);
        object launcher = Single<IBrowserLauncher>(services).ImplementationFactory!(provider);

        Assert.IsAssignableFrom<ISecretStore>(store);
        Assert.IsType<SystemBrowserLauncher>(launcher);
        Assert.Equal(typeof(LoopbackRedirectListener), Single<IOAuthRedirectListener>(services).ImplementationType);
        Assert.All(services, descriptor => Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime));
    }

    [Fact]
    public void RegistersTheUsersFolders()
    {
        ServiceCollection services = new();
        services.AddPlatformServices(new SecretStoreOptions { Folder = _folder.Path });

        object folders = Single<IUserFolders>(services).ImplementationFactory!(new StubServiceProvider());

        Assert.True(Path.IsPathFullyQualified(Assert.IsAssignableFrom<IUserFolders>(folders).Downloads));
    }

    [Fact]
    public void RegistersTheNoticesOfThisSystem()
    {
        ServiceCollection services = new();
        services.AddPlatformServices(new SecretStoreOptions { Folder = _folder.Path });

        object notices = Single<INotificationService>(services).ImplementationFactory!(new StubServiceProvider());

        Assert.IsAssignableFrom<INotificationService>(notices);
    }

    [Fact]
    public async Task RegistersTheSystemsRegisterOfLinkSchemes()
    {
        ServiceCollection services = new();
        services.AddPlatformServices(
            new SecretStoreOptions { Folder = _folder.Path },
            new SchemeHandlerOptions(Path.Combine(_folder.Path, "ModShelf.Desktop"), []));

        object registration = Single<ISchemeRegistration>(services).ImplementationFactory!(new StubServiceProvider());

        // No desktop app there: on Windows and Linux the register can't take links; on macOS, a test isn't an app.
        SchemeState state = await Assert.IsAssignableFrom<ISchemeRegistration>(registration).GetAsync("modshelf-test-none", TestContext.Current.CancellationToken);
        Assert.Equal(SchemeHandlerKind.Unavailable, state.Kind);
    }

    [Fact]
    public void TheDesktopAppIsBesideThisOneByDefault()
    {
        Assert.Equal(AppContext.BaseDirectory, Path.GetDirectoryName(PlatformServiceCollectionExtensions.DefaultDesktopApp) + Path.DirectorySeparatorChar);
        Assert.StartsWith("ModShelf.Desktop", Path.GetFileName(PlatformServiceCollectionExtensions.DefaultDesktopApp), StringComparison.Ordinal);
    }

    [Fact]
    public void RegistersTheSingleInstanceForTheLibrarysFolder()
    {
        ServiceCollection services = new();
        services.AddSingleInstance(_folder.Path);

        using PipeSingleInstance instance = Assert.IsType<PipeSingleInstance>(Single<ISingleInstance>(services).ImplementationFactory!(new StubServiceProvider()));

        Assert.True(instance.TryClaim([]));
        Assert.True(File.Exists(Path.Combine(_folder.Path, "desktop.lock")));
        Assert.Equal(ServiceLifetime.Singleton, Single<ISingleInstance>(services).Lifetime);
    }

    [Fact]
    public void RegistersTheTrayHost()
    {
        ServiceCollection services = new();
        services.AddTrayHost();

        object tray = Single<ITrayHost>(services).ImplementationFactory!(new StubServiceProvider());

        Assert.IsType<SystemTrayHost>(tray);
        Assert.Equal(ServiceLifetime.Singleton, Single<ITrayHost>(services).Lifetime);
    }

    [Fact]
    public void ABlankScopeIsRefused()
    {
        Assert.Throws<ArgumentException>(() => new ServiceCollection().AddPlatformServices(new SecretStoreOptions { Scope = " " }));
    }

        private static ServiceDescriptor Single<TService>(ServiceCollection services)
        => Assert.Single(services, descriptor => descriptor.ServiceType == typeof(TService));

    private sealed class StubServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => serviceType == typeof(ILoggerFactory) ? NullLoggerFactory.Instance : null;
    }
}
