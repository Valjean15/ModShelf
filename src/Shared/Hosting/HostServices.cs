using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using ModShelf.Application;
using ModShelf.Application.Diagnostics;
using ModShelf.Infrastructure;
using ModShelf.Platform;
using ModShelf.Platform.Agents;
using ModShelf.Platform.Links;
using ModShelf.Platform.Secrets;
using ModShelf.Sources.DirectUrl;
using ModShelf.Sources.Nexus;

namespace ModShelf.Hosting;

/// <summary>
/// What the CLI and the desktop app build alike, from the same configuration: ModShelf's files, and the services of
/// the core, the platform and the mod sites. Each host adds what only it runs. Both compile this file, which lives in
/// <c>src/Shared/Hosting</c>.
/// </summary>
internal static class HostServices
{
    /// <summary>
    /// The configuration key, or <c>ModShelf__DatabasePath</c> as an environment variable, that moves the library
    /// database away from its default location. The settings file, <c>settings.json</c>, and the log files move with it.
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
    /// The configuration key, or <c>ModShelf__DesktopApp</c>, for the desktop app's executable, which the system starts
    /// to open a link with ModShelf. By default it's beside this app, as it is in an installed ModShelf; the CLI of a
    /// build from source needs it set.
    /// </summary>
    public const string DesktopAppKey = "ModShelf:DesktopApp";

    /// <summary>
    /// The configuration key, or <c>ModShelf__UpdateFeed</c>, for a folder or an http(s) address holding Velopack's
    /// release feed, which an installed ModShelf updates itself from in place of its GitHub releases, such as for testing.
    /// </summary>
    public const string UpdateFeedKey = "ModShelf:UpdateFeed";

    /// <summary>
    /// The configuration key, or <c>ModShelf__CommandLine</c>, for the command line's executable, which serves the MCP
    /// server's address and which agents' apps start. By default it's beside the desktop app, as in an installed ModShelf,
    /// or, in a build from source, in its own build folder (ADR 0080).
    /// </summary>
    public const string CommandLineKey = "ModShelf:CommandLine";

    /// <summary>
    /// Registers ModShelf's files, where the configuration says, and every service both hosts have.
    /// </summary>
    /// <param name="app">What the host's log files' names start with, such as <c>cli</c>.</param>
    /// <returns>The library database's folder, where the settings and the log files are too.</returns>
    public static string AddModShelf(this IServiceCollection services, IConfiguration configuration, string app)
    {
        string? databasePath = configuration[DatabasePathKey];
        string folder = services.AddLibraryFiles(string.IsNullOrWhiteSpace(databasePath) ? null : Path.GetFullPath(databasePath), app);

        services.AddPlatformServices(SecretStoreOptionsFrom(configuration), SchemeHandlerOptionsFrom(configuration));
        services.AddAppReleases(string.IsNullOrWhiteSpace(configuration[UpdateFeedKey]) ? null : configuration[UpdateFeedKey]);
        services.AddHttpFileTransfer();
        services.AddSourceHttpClients();
        services.AddDownloadEngine();
        services.AddAccounts();
        services.AddModLookup();
        services.AddModDetails();
        services.AddModUpdates();
        services.AddWatchlists();
        services.AddLibrary();
        services.AddLibraryStorage();
        services.AddLibraryDownloads();
        services.AddLibraryMover();
        services.AddUpdateChecks();
        services.AddSettings();
        services.AddDiagnostics(AppInfo.Of(typeof(HostServices).Assembly));
        services.AddLinks();
        services.AddQueueRequests();
        services.AddAppUninstall();
        services.AddDirectUrlSource();
        services.AddNexusSource(NexusOptionsFrom(configuration));
        return folder;
    }

    private static NexusOptions NexusOptionsFrom(IConfiguration configuration)
    {
        string? clientId = configuration[NexusClientIdKey];
        return new NexusOptions { OAuthClientId = string.IsNullOrWhiteSpace(clientId) ? null : clientId.Trim() };
    }

    /// <summary>
    /// The desktop app, started with what keeps it on this library and these secrets when they aren't in their default
    /// places, such as <c>--ModShelf:DatabasePath=…</c>: the system starts it without this app's environment.
    /// </summary>
    internal static SchemeHandlerOptions SchemeHandlerOptionsFrom(IConfiguration configuration)
    {
        string? desktopApp = configuration[DesktopAppKey];
        string[] arguments =
        [
            .. new[] { DatabasePathKey, SecretsFolderKey }
                .Where(key => !string.IsNullOrWhiteSpace(configuration[key]))
                .Select(key => $"--{key}={Path.GetFullPath(configuration[key]!)}"),
            .. string.IsNullOrWhiteSpace(configuration[SecretScopeKey]) ? [] : new[] { $"--{SecretScopeKey}={configuration[SecretScopeKey]}" },
        ];
        return new SchemeHandlerOptions(
            string.IsNullOrWhiteSpace(desktopApp) ? PlatformServiceCollectionExtensions.DefaultDesktopApp : Path.GetFullPath(desktopApp),
            arguments);
    }

    /// <summary>
    /// The command line, for agents, with the environment that keeps it on this library and these secrets when they
    /// aren't in their default places, such as <c>ModShelf__DatabasePath</c>: an agent's app starts it without this app's
    /// environment. Never a secret, such as the Nexus Mods client ID.
    /// </summary>
    internal static AgentServerOptions AgentServerOptionsFrom(IConfiguration configuration)
    {
        string? commandLine = configuration[CommandLineKey];
        Dictionary<string, string> environment = new(StringComparer.Ordinal);
        foreach (string key in new[] { DatabasePathKey, SecretsFolderKey })
        {
            if (!string.IsNullOrWhiteSpace(configuration[key]))
            {
                environment[key.Replace(":", "__", StringComparison.Ordinal)] = Path.GetFullPath(configuration[key]!);
            }
        }

        if (!string.IsNullOrWhiteSpace(configuration[SecretScopeKey]))
        {
            environment[SecretScopeKey.Replace(":", "__", StringComparison.Ordinal)] = configuration[SecretScopeKey]!;
        }

        return new AgentServerOptions(string.IsNullOrWhiteSpace(commandLine) ? null : Path.GetFullPath(commandLine), environment);
    }

    private static SecretStoreOptions SecretStoreOptionsFrom(IConfiguration configuration)
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
