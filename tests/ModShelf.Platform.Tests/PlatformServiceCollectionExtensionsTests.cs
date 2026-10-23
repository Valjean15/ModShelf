using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using ModShelf.Application.Accounts.Ports;
using ModShelf.Platform.Browser;
using ModShelf.Platform.OAuth;
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
        services.AddPlatformServices(_folder.Path);
        StubServiceProvider provider = new();

        object store = Single<ISecretStore>(services).ImplementationFactory!(provider);
        object launcher = Single<IBrowserLauncher>(services).ImplementationFactory!(provider);

        Assert.IsAssignableFrom<ISecretStore>(store);
        Assert.IsType<SystemBrowserLauncher>(launcher);
        Assert.Equal(typeof(LoopbackRedirectListener), Single<IOAuthRedirectListener>(services).ImplementationType);
        Assert.All(services, descriptor => Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime));
    }

    private static ServiceDescriptor Single<TService>(ServiceCollection services)
        => Assert.Single(services, descriptor => descriptor.ServiceType == typeof(TService));

    private sealed class StubServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => serviceType == typeof(ILoggerFactory) ? NullLoggerFactory.Instance : null;
    }
}
