using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using ModShelf.Application;
using ModShelf.Desktop.Services;
using ModShelf.Infrastructure;
using ModShelf.Platform;
using ModShelf.Platform.Secrets;
using ModShelf.Presentation;
using ModShelf.Presentation.Services;
using ModShelf.Sources.DirectUrl;
using ModShelf.Sources.Nexus;

namespace ModShelf.Desktop.Hosting;

/// <summary>
/// The desktop app's composition root: the generic host with every service the CLI has, plus the ones that run in
/// the background while the app is open (downloads, recording them into the library, and update checks), and the
/// screens.
/// </summary>
internal static class DesktopHost
{
    /// <summary>
    /// The configuration key, or <c>ModShelf__DatabasePath</c> as an environment variable, that moves the library
    /// database away from its default location, as in the CLI. The settings file, <c>settings.json</c>, moves with it.
    /// </summary>
    public const string DatabasePathKey = "ModShelf:DatabasePath";

    /// <summary>
    /// The configuration key, or <c>ModShelf__SecretsFolder</c>, for the folder of secret files on Windows and in the
    /// Linux fallback, as in the CLI.
    /// </summary>
    public const string SecretsFolderKey = "ModShelf:SecretsFolder";

    /// <summary>
    /// The configuration key, or <c>ModShelf__SecretScope</c>, that keeps these secrets apart from the user's own in the
    /// Keychain and the Secret Service, as in the CLI.
    /// </summary>
    public const string SecretScopeKey = "ModShelf:SecretScope";

    /// <summary>
    /// The configuration key, or <c>ModShelf__Nexus__ClientId</c>, for the OAuth client ID Nexus Mods issues to
    /// ModShelf, as in the CLI. Keep it in user secrets or the environment, never in a committed file.
    /// </summary>
    public const string NexusClientIdKey = "ModShelf:Nexus:ClientId";

    /// <summary>
    /// What the desktop app's log files' names start with, such as <c>desktop-20261029.log</c>.
    /// </summary>
    private const string LogFilePrefix = "desktop";

    public static IHost Create(string[] args)
    {
        // Read configuration next to the executable: desktop shortcuts and nxm:// launches
        // start the app with an arbitrary working directory.
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = args,
            ContentRootPath = AppContext.BaseDirectory,
        });

        // A service missing a dependency fails at start, not when its screen first opens.
        builder.ConfigureContainer(new DefaultServiceProviderFactory(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true }));

        // The console and the debugger, and a log file a day in a logs folder beside the database (below). The plan's
        // tech stack named Serilog for the files; a small provider of ModShelf's own writes them without another package.
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole();
        builder.Logging.AddDebug();

        builder.Services.AddSingleton<IHostLifetime, DesktopHostLifetime>();

        // The settings sit next to the database, so a database moved elsewhere, such as for tests, takes its settings along.
        string? configuredPath = builder.Configuration[DatabasePathKey];
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            builder.Services.AddLibraryDatabase();
            builder.Services.AddSettingsFile();
            builder.Services.AddLogFiles(LogFilePrefix);
        }
        else
        {
            string databasePath = Path.GetFullPath(configuredPath);
            builder.Services.AddLibraryDatabase(databasePath);
            builder.Services.AddSettingsFile(Path.Combine(Path.GetDirectoryName(databasePath)!, "settings.json"));
            builder.Services.AddLogFiles(Path.Combine(Path.GetDirectoryName(databasePath)!, "logs"), LogFilePrefix);
        }

        builder.Services.AddPlatformServices(SecretStoreOptionsFrom(builder.Configuration));
        builder.Services.AddHttpFileTransfer();
        builder.Services.AddSourceHttpClients();
        builder.Services.AddDownloadEngine();
        builder.Services.AddAccounts();
        builder.Services.AddModLookup();
        builder.Services.AddModUpdates();
        builder.Services.AddWatchlists();
        builder.Services.AddLibrary();
        builder.Services.AddLibraryStorage();
        builder.Services.AddLibraryDownloads();
        builder.Services.AddLibraryMover();
        builder.Services.AddUpdateChecks();
        builder.Services.AddSettings();
        builder.Services.AddDirectUrlSource();
        builder.Services.AddNexusSource(NexusOptionsFrom(builder.Configuration));

        // While the app is open, queued jobs download, finished ones are recorded into the library, and the watched
        // mods are checked for updates every updates.interval.
        builder.Services.AddBackgroundDownloads();
        builder.Services.AddLibraryRecording();
        builder.Services.AddBackgroundUpdateChecks();

        builder.Services.AddPresentation();
        builder.Services.AddSingleton<IUiThread, AvaloniaUiThread>();
        builder.Services.AddSingleton<IDialogService, AvaloniaDialogService>();
        builder.Services.AddSingleton<IFolderOpener, AvaloniaFolderOpener>();
        builder.Services.AddSingleton<IThemeSwitcher, AvaloniaThemeSwitcher>();

        return builder.Build();
    }

    private static NexusOptions NexusOptionsFrom(ConfigurationManager configuration)
    {
        string? clientId = configuration[NexusClientIdKey];
        return new NexusOptions { OAuthClientId = string.IsNullOrWhiteSpace(clientId) ? null : clientId.Trim() };
    }

    private static SecretStoreOptions SecretStoreOptionsFrom(ConfigurationManager configuration)
    {
        string? folder = configuration[SecretsFolderKey];
        string? scope = configuration[SecretScopeKey];
        return new SecretStoreOptions
        {
            Folder = string.IsNullOrWhiteSpace(folder) ? null : Path.GetFullPath(folder),
            Scope = string.IsNullOrWhiteSpace(scope) ? SecretStoreOptions.DefaultScope : scope,
        };
    }
}
