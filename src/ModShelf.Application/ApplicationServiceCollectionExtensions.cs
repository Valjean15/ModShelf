using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using ModShelf.Application.Accounts;
using ModShelf.Application.Downloads;
using ModShelf.Application.Sources;

namespace ModShelf.Application;

/// <summary>
/// Registers Application's services. The hosts call these from their composition roots.
/// </summary>
public static class ApplicationServiceCollectionExtensions
{
    /// <summary>
    /// Registers the download engine: the <see cref="ISourceRegistry"/>, the <see cref="DownloadScheduler"/> as the
    /// <see cref="IDownloadService"/>, and <see cref="ILinkDownloads"/>. Nothing downloads until
    /// <see cref="AddBackgroundDownloads"/> is also called.
    /// </summary>
    public static IServiceCollection AddDownloadEngine(this IServiceCollection services, DownloadSchedulerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton(options ?? new DownloadSchedulerOptions());
        services.TryAddSingleton<ISourceRegistry, SourceRegistry>();
        services.AddSingleton<DownloadScheduler>();
        services.AddSingleton<IDownloadService>(provider => provider.GetRequiredService<DownloadScheduler>());
        services.AddSingleton<ILinkDownloads, LinkDownloads>();

        return services;
    }

    /// <summary>
    /// Registers account linking: one <see cref="AccountService"/> as both the <see cref="IAccountService"/> and the
    /// <see cref="ICredentialProvider"/>. Needs the ports from Platform (<see cref="ISecretStore"/>,
    /// <see cref="IOAuthRedirectListener"/>, <see cref="IBrowserLauncher"/>) and Infrastructure
    /// (<see cref="ILinkedAccountRepository"/>).
    /// </summary>
    public static IServiceCollection AddAccounts(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ISourceRegistry, SourceRegistry>();
        services.AddSingleton<AccountService>();
        services.AddSingleton<IAccountService>(provider => provider.GetRequiredService<AccountService>());
        services.AddSingleton<ICredentialProvider>(provider => provider.GetRequiredService<AccountService>());

        return services;
    }

    /// <summary>
    /// Runs the <see cref="DownloadScheduler"/> as a hosted service, so queued jobs download while the host runs.
    /// A host that only reads the queue leaves this out.
    /// </summary>
    public static IServiceCollection AddBackgroundDownloads(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHostedService(provider => provider.GetRequiredService<DownloadScheduler>());

        return services;
    }
}
