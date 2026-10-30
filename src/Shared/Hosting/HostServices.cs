using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using ModShelf.Application;
using ModShelf.Application.Diagnostics;
using ModShelf.Infrastructure;
using ModShelf.Platform;
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
    /// Registers ModShelf's files, where the configuration says, and every service both hosts have.
    /// </summary>
    /// <param name="app">What the host's log files' names start with, such as <c>cli</c>.</param>
    /// <returns>The library database's folder, where the settings and the log files are too.</returns>
    public static string AddModShelf(this IServiceCollection services, IConfiguration configuration, string app)
    {
        string? databasePath = configuration[DatabasePathKey];
        string folder = services.AddLibraryFiles(string.IsNullOrWhiteSpace(databasePath) ? null : Path.GetFullPath(databasePath), app);

        services.AddPlatformServices(SecretStoreOptionsFrom(configuration));
        services.AddHttpFileTransfer();
        services.AddSourceHttpClients();
        services.AddDownloadEngine();
        services.AddAccounts();
        services.AddModLookup();
        services.AddModUpdates();
        services.AddWatchlists();
        services.AddLibrary();
        services.AddLibraryStorage();
        services.AddLibraryDownloads();
        services.AddLibraryMover();
        services.AddUpdateChecks();
        services.AddSettings();
        services.AddDiagnostics(AppInfo.Of(typeof(HostServices).Assembly));
        services.AddDirectUrlSource();
        services.AddNexusSource(NexusOptionsFrom(configuration));
        return folder;
    }

    private static NexusOptions NexusOptionsFrom(IConfiguration configuration)
    {
        string? clientId = configuration[NexusClientIdKey];
        return new NexusOptions { OAuthClientId = string.IsNullOrWhiteSpace(clientId) ? null : clientId.Trim() };
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
