using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;

using ModShelf.Application;
using ModShelf.Infrastructure;
using ModShelf.Platform;
using ModShelf.Platform.Secrets;
using ModShelf.Sources.DirectUrl;
using ModShelf.Sources.Nexus;

namespace ModShelf.Cli.Hosting;

/// <summary>
/// The CLI's composition root: builds the generic host with the same services the desktop app will use.
/// </summary>
internal static class CliHost
{
    /// <summary>
    /// The configuration key, or <c>ModShelf__DatabasePath</c> as an environment variable, that moves the
    /// library database away from its default location. The settings file, <c>settings.json</c>, moves with it.
    /// </summary>
    public const string DatabasePathKey = "ModShelf:DatabasePath";

    /// <summary>
    /// The configuration key, or <c>ModShelf__SecretsFolder</c>, for the folder of secret files on Windows and in the
    /// Linux fallback.
    /// </summary>
    public const string SecretsFolderKey = "ModShelf:SecretsFolder";

    /// <summary>
    /// The configuration key, or <c>ModShelf__SecretScope</c>, that keeps these secrets apart from the user's own in the
    /// Keychain and the Secret Service.
    /// </summary>
    public const string SecretScopeKey = "ModShelf:SecretScope";

    /// <summary>
    /// The configuration key, or <c>ModShelf__Nexus__ClientId</c>, for the OAuth client ID Nexus Mods issues to ModShelf.
    /// With one, Nexus accounts can sign in through the browser. Keep it in user secrets or the environment, never in a
    /// committed file.
    /// </summary>
    public const string NexusClientIdKey = "ModShelf:Nexus:ClientId";

    /// <summary>
    /// What the CLI's log files' names start with, such as <c>cli-20261029.log</c>. They sit beside the desktop app's.
    /// </summary>
    private const string LogFilePrefix = "cli";

    /// <param name="runDownloads">
    /// Whether queued jobs download while the host runs. A command that only reads the queue leaves this off.
    /// </param>
    /// <param name="settings">Configuration that takes precedence over appsettings.json and environment variables.</param>
    /// <param name="configureServices">Runs after every registration, so it can add services or replace them.</param>
    public static IHost Build(
        bool runDownloads,
        IReadOnlyDictionary<string, string?>? settings = null,
        Action<IServiceCollection>? configureServices = null)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            // System.CommandLine owns the arguments, so they aren't read as configuration too.
            Args = [],

            // Read appsettings.json next to the executable, not from whatever directory the shell is in.
            ContentRootPath = AppContext.BaseDirectory,
        });
        if (settings is not null)
        {
            builder.Configuration.AddInMemoryCollection(settings);
        }

        // Logs go to stderr so they never mix with a command's output; appsettings.json keeps them to warnings there,
        // and lets more through to the log files.
        builder.Services.Configure<ConsoleLoggerOptions>(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
        builder.Services.AddSingleton<IHostLifetime, CliHostLifetime>();

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
        if (runDownloads)
        {
            builder.Services.AddBackgroundDownloads();
            builder.Services.AddLibraryRecording();
        }

        configureServices?.Invoke(builder.Services);
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
