using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

using ModShelf.Application.Accounts;
using ModShelf.Application.Catalog;
using ModShelf.Application.Downloads;
using ModShelf.Application.Sources;
using ModShelf.Application.Tests.Accounts;
using ModShelf.Application.Tests.Fakes;
using ModShelf.Application.Updates;
using ModShelf.Application.Watchlist;

namespace ModShelf.Application.Tests;

/// <summary>
/// Checks the registrations by calling their factories against a stub container, since this test project
/// has the DI abstractions but not the container itself.
/// </summary>
public sealed class ApplicationServiceCollectionExtensionsTests
{
    [Fact]
    public void TheDownloadServiceIsTheScheduler()
    {
        ServiceCollection services = new();
        services.AddDownloadEngine();
        DownloadScheduler scheduler = NewScheduler();

        object service = Single<IDownloadService>(services).ImplementationFactory!(new StubServiceProvider(scheduler));

        Assert.Same(scheduler, service);
        Assert.Equal(typeof(DownloadScheduler), Single<DownloadScheduler>(services).ImplementationType);
        Assert.Equal(typeof(LinkDownloads), Single<ILinkDownloads>(services).ImplementationType);
        Assert.Equal(typeof(SourceRegistry), Single<ISourceRegistry>(services).ImplementationType);
        Assert.All(services, descriptor => Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime));
    }

    [Fact]
    public void TheEngineAloneDownloadsNothing()
    {
        ServiceCollection services = new();
        services.AddDownloadEngine();

        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(IHostedService));
    }

    [Fact]
    public void BackgroundDownloadsRunTheSameScheduler()
    {
        ServiceCollection services = new();
        services.AddDownloadEngine();
        services.AddBackgroundDownloads();
        services.AddBackgroundDownloads();
        DownloadScheduler scheduler = NewScheduler();

        object hosted = Single<IHostedService>(services).ImplementationFactory!(new StubServiceProvider(scheduler));

        Assert.Same(scheduler, hosted);
    }

    [Fact]
    public void OneAccountServiceIsBothTheAccountServiceAndTheCredentialProvider()
    {
        ServiceCollection services = new();
        services.AddDownloadEngine();
        services.AddAccounts();
        AccountService accounts = new AccountHarness().Service;
        StubServiceProvider provider = new(NewScheduler(), accounts);

        Assert.Same(accounts, Single<IAccountService>(services).ImplementationFactory!(provider));
        Assert.Same(accounts, Single<ICredentialProvider>(services).ImplementationFactory!(provider));
        Assert.Equal(typeof(AccountService), Single<AccountService>(services).ImplementationType);
        Assert.Equal(typeof(SourceRegistry), Single<ISourceRegistry>(services).ImplementationType);
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(TimeProvider));
    }

    [Fact]
    public void TheModLookupSharesTheSourceRegistry()
    {
        ServiceCollection services = new();
        services.AddDownloadEngine();
        services.AddModLookup();
        services.AddModLookup();

        Assert.Equal(typeof(ModLookup), Single<IModLookup>(services).ImplementationType);
        Assert.Equal(typeof(SourceRegistry), Single<ISourceRegistry>(services).ImplementationType);
        Assert.All(services, descriptor => Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime));
    }

    [Fact]
    public void TheModUpdatesShareTheSourceRegistry()
    {
        ServiceCollection services = new();
        services.AddModLookup();
        services.AddModUpdates();
        services.AddModUpdates();

        Assert.Equal(typeof(ModUpdates), Single<IModUpdates>(services).ImplementationType);
        Assert.Equal(typeof(SourceRegistry), Single<ISourceRegistry>(services).ImplementationType);
        Assert.All(services, descriptor => Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime));
    }

    [Fact]
    public void TheWatchlistsShareTheSourceRegistry()
    {
        ServiceCollection services = new();
        services.AddModLookup();
        services.AddWatchlists();
        services.AddWatchlists();

        Assert.Equal(typeof(Watchlists), Single<IWatchlists>(services).ImplementationType);
        Assert.Equal(typeof(SourceRegistry), Single<ISourceRegistry>(services).ImplementationType);
        Assert.All(services, descriptor => Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime));
    }

    private static ServiceDescriptor Single<TService>(ServiceCollection services)
        => Assert.Single(services, descriptor => descriptor.ServiceType == typeof(TService));

    private static DownloadScheduler NewScheduler()
        => new(
            new InMemoryDownloadJobRepository([]),
            new SourceRegistry([]),
            new ControlledFileTransfer(),
            TimeProvider.System,
            new DownloadSchedulerOptions(),
            NullLogger<DownloadScheduler>.Instance);

    private sealed class StubServiceProvider(DownloadScheduler scheduler, AccountService? accounts = null) : IServiceProvider
    {
        public object? GetService(Type serviceType)
            => serviceType == typeof(DownloadScheduler) ? scheduler
                : serviceType == typeof(AccountService) ? accounts
                : null;
    }
}
