using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using ModShelf.Application.Accounts.Ports;
using ModShelf.Application.Agents.Ports;
using ModShelf.Application.Desktop.Ports;
using ModShelf.Application.Downloads;
using ModShelf.Application.Downloads.Ports;
using ModShelf.Application.Links.Ports;
using ModShelf.Application.Notifications.Ports;
using ModShelf.Application.Releases.Ports;
using ModShelf.Application.Settings.Ports;
using ModShelf.Platform.Agents;
using ModShelf.Platform.Browser;
using ModShelf.Platform.Folders;
using ModShelf.Platform.Instances;
using ModShelf.Platform.Links;
using ModShelf.Platform.Notifications;
using ModShelf.Platform.OAuth;
using ModShelf.Platform.Releases;
using ModShelf.Platform.Secrets;
using ModShelf.Platform.Tray;

namespace ModShelf.Platform;

/// <summary>
/// Registers Platform's services. The hosts call these from their composition roots.
/// </summary>
public static class PlatformServiceCollectionExtensions
{
    /// <summary>
    /// The app ID Windows groups ModShelf's windows and notices by. The installer's Start-menu shortcut carries it too.
    /// </summary>
    public const string WindowsAppId = ToastAppId.Default;

    /// <summary>
    /// Registers the secret store for this system, the OAuth loopback redirect listener, the browser launcher, the
    /// user's folders as this system keeps them, the way this system shows notices, its register of the apps that open
    /// link schemes, and what ModShelf registers for itself.
    /// </summary>
    /// <param name="secrets">Where the secret store keeps its secrets; by default the user's own ModShelf secrets.</param>
    /// <param name="links">
    /// What the system starts when it opens a link with ModShelf; by default the desktop app beside this one, with no
    /// arguments before the link.
    /// </param>
    public static IServiceCollection AddPlatformServices(
        this IServiceCollection services,
        SecretStoreOptions? secrets = null,
        SchemeHandlerOptions? links = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        SecretStoreOptions settings = secrets ?? new SecretStoreOptions();
        ArgumentException.ThrowIfNullOrWhiteSpace(settings.Scope, nameof(secrets));
        SchemeHandlerOptions handler = links ?? new SchemeHandlerOptions(DefaultDesktopApp, []);
        ArgumentException.ThrowIfNullOrWhiteSpace(handler.DesktopApp, nameof(links));

        services.AddSingleton(provider => SecretStores.ForThisSystem(
            settings.Folder ?? SecretStores.GetDefaultFolder(),
            settings.Scope,
            provider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(SecretStores).FullName!)));
        services.AddSingleton<IOAuthRedirectListener, LoopbackRedirectListener>();
        services.AddSingleton<IBrowserLauncher>(_ => new SystemBrowserLauncher());
        services.AddSingleton(_ => UserFolders.ForThisSystem());
        services.AddSingleton(_ => SchemeRegistrations.ForThisSystem(handler));
        services.AddSingleton(provider => NotificationServices.ForThisSystem(
            provider.GetRequiredService<ISchemeRegistration>(),
            provider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(NotificationServices).FullName!)));
        services.AddSingleton(provider => AppRegistrations.ForThisSystem(
            provider.GetRequiredService<ISchemeRegistration>(),
            provider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(NoticeClicks).FullName!)));

        return services;
    }

    /// <summary>
    /// The desktop app's executable in this app's folder, such as <c>ModShelf.Desktop.exe</c> on Windows; on Linux, the
    /// AppImage this app runs from, if so.
    /// </summary>
    public static string DefaultDesktopApp
        => DesktopAppPath.Of(AppContext.BaseDirectory, Environment.GetEnvironmentVariable("APPIMAGE"), Environment.GetEnvironmentVariable("APPDIR"));

    /// <summary>
    /// Registers <see cref="IAppReleases"/>: ModShelf's own releases, which an installed ModShelf updates itself from.
    /// </summary>
    /// <param name="feed">
    /// A folder or an http(s) address that holds Velopack's release feed, in place of ModShelf's GitHub releases, such as
    /// for testing; null for GitHub.
    /// </param>
    public static IServiceCollection AddAppReleases(this IServiceCollection services, string? feed = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IAppReleases>(_ => new VelopackAppReleases(AppReleaseSources.For(feed)));

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
    /// Registers <see cref="IQueueOwner"/>, for a process that doesn't run the download queue of the library in
    /// <paramref name="folder"/>, such as the CLI's MCP server: the desktop app that runs it, asked over a pipe, which
    /// takes a request within 1.5 seconds when it runs, and replies within 90.
    /// </summary>
    /// <param name="folder">The folder of the library database, an absolute path.</param>
    public static IServiceCollection AddQueueOwner(this IServiceCollection services, string folder)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);

        services.AddSingleton<IQueueOwner>(provider => new PipeQueueOwner(
            folder,
            TimeSpan.FromSeconds(1.5),
            TimeSpan.FromSeconds(90),
            provider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(PipeQueueOwner).FullName!)));

        return services;
    }

    /// <summary>
    /// Registers what answers <see cref="IQueueOwner"/>'s requests in the desktop app, while its host runs: the library
    /// in <paramref name="folder"/>'s queue requests, carried out by <see cref="IQueueRequests"/>.
    /// </summary>
    /// <param name="folder">The folder of the library database, an absolute path.</param>
    public static IServiceCollection AddQueueListener(this IServiceCollection services, string folder)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);

        services.AddHostedService(provider => new PipeQueueListener(
            folder,
            provider.GetRequiredService<IQueueRequests>(),
            TimeSpan.FromSeconds(5),
            provider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(PipeQueueListener).FullName!)));

        return services;
    }

    /// <summary>
    /// Registers the desktop app's <see cref="ITrayHost"/>, which says whether this system shows tray icons now.
    /// </summary>
    /// <summary>
    /// Registers the MCP server the desktop app offers agents (<see cref="IAgentServer"/>): the command line's command, and
    /// its address, which it serves while agents may use ModShelf; and Claude Desktop's configuration, to add the server
    /// to (<see cref="IClaudeDesktopConfig"/>).
    /// </summary>
    public static IServiceCollection AddAgentServer(this IServiceCollection services, AgentServerOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        services.AddSingleton<IAgentServer>(provider => new AgentServerProcess(
            CommandLinePath.Find(options.CommandLine, AppContext.BaseDirectory, File.Exists),

            // An AppImage's files are where the system mounts it while it runs, somewhere new each time.
            lasting: options.CommandLine is not null || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("APPIMAGE")),
            options.Environment,
            new ProcessAgentServerLauncher(),
            provider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(AgentServerProcess).FullName!)));
        services.AddSingleton<IClaudeDesktopConfig>(_ => new ClaudeDesktopConfigFile(ClaudeDesktopFolder.ForThisSystem()));

        return services;
    }

    public static IServiceCollection AddTrayHost(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<ITrayHost>(provider => new SystemTrayHost(
            provider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(SystemTrayHost).FullName!)));

        return services;
    }
}
