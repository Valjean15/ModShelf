using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using ModShelf.Application.Accounts.Ports;
using ModShelf.Platform.Browser;
using ModShelf.Platform.OAuth;
using ModShelf.Platform.Secrets;

namespace ModShelf.Platform;

/// <summary>
/// Registers Platform's services. The hosts call these from their composition roots.
/// </summary>
public static class PlatformServiceCollectionExtensions
{
    /// <summary>
    /// Registers the secret store for this system, the OAuth loopback redirect listener and the browser launcher.
    /// </summary>
    /// <param name="secretsFolder">
    /// The folder for secret files on Windows and in the Linux fallback; by default ModShelf's folder under the
    /// user's local application data.
    /// </param>
    public static IServiceCollection AddPlatformServices(this IServiceCollection services, string? secretsFolder = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton(provider => SecretStores.ForThisSystem(
            secretsFolder ?? SecretStores.GetDefaultFolder(),
            provider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(SecretStores).FullName!)));
        services.AddSingleton<IOAuthRedirectListener, LoopbackRedirectListener>();
        services.AddSingleton<IBrowserLauncher>(_ => new SystemBrowserLauncher());

        return services;
    }
}
