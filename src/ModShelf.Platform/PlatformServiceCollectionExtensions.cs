using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using ModShelf.Application.Accounts.Ports;
using ModShelf.Application.Desktop.Ports;
using ModShelf.Application.Notifications.Ports;
using ModShelf.Application.Settings.Ports;
using ModShelf.Platform.Browser;
using ModShelf.Platform.Folders;
using ModShelf.Platform.Instances;
using ModShelf.Platform.Notifications;
using ModShelf.Platform.OAuth;
using ModShelf.Platform.Secrets;
using ModShelf.Platform.Tray;

namespace ModShelf.Platform;

/// <summary>
/// Registers Platform's services. The hosts call these from their composition roots.
/// </summary>
public static class PlatformServiceCollectionExtensions
{
    /// <summary>
    /// Registers the secret store for this system, the OAuth loopback redirect listener, the browser launcher, the
    /// user's folders as this system keeps them, and the way this system shows notices.
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
        services.AddSingleton(provider => NotificationServices.ForThisSystem(
            provider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(NotificationServices).FullName!)));

        return services;
    }

    /// <summary>
    /// Registers the desktop app's <see cref="ISingleInstance"/>: one app at a time for the library in
    /// <paramref name="folder"/>, the library database's. A second launch waits up to 5 seconds for the running app to
    /// answer.
    /// </summary>
    /// <param name="folder">The folder of the library database, an absolute path.</param>
    public static IServiceCollection AddSingleInstance(this IServiceCollection services, string folder)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);

        services.AddSingleton<ISingleInstance>(provider => new PipeSingleInstance(
            folder,
            TimeSpan.FromSeconds(5),
            provider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(PipeSingleInstance).FullName!)));

        return services;
    }

    /// <summary>
    /// Registers the desktop app's <see cref="ITrayHost"/>, which says whether this system shows tray icons now.
    /// </summary>
    public static IServiceCollection AddTrayHost(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<ITrayHost>(provider => new SystemTrayHost(
            provider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(SystemTrayHost).FullName!)));

        return services;
    }
}
