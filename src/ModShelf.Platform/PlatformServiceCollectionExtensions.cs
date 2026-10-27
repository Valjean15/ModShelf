using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using ModShelf.Application.Accounts.Ports;
using ModShelf.Application.Settings.Ports;
using ModShelf.Platform.Browser;
using ModShelf.Platform.Folders;
using ModShelf.Platform.OAuth;
using ModShelf.Platform.Secrets;

namespace ModShelf.Platform;

/// <summary>
/// Registers Platform's services. The hosts call these from their composition roots.
/// </summary>
public static class PlatformServiceCollectionExtensions
{
    /// <summary>
    /// Registers the secret store for this system, the OAuth loopback redirect listener, the browser launcher, and the
    /// user's folders as this system keeps them.
    /// </summary>
    /// <param name="secrets">Where the secret store keeps its secrets; by default the user's own ModShelf secrets.</param>
    public static IServiceCollection AddPlatformServices(this IServiceCollection services, SecretStoreOptions? secrets = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        SecretStoreOptions settings = secrets ?? new SecretStoreOptions();
        ArgumentException.ThrowIfNullOrWhiteSpace(settings.Scope, nameof(secrets));

        services.AddSingleton(provider => SecretStores.ForThisSystem(
            settings.Folder ?? SecretStores.GetDefaultFolder(),
            settings.Scope,
            provider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(SecretStores).FullName!)));
        services.AddSingleton<IOAuthRedirectListener, LoopbackRedirectListener>();
        services.AddSingleton<IBrowserLauncher>(_ => new SystemBrowserLauncher());
        services.AddSingleton(_ => UserFolders.ForThisSystem());

        return services;
    }
}
