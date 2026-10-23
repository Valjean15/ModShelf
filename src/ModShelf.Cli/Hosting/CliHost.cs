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

namespace ModShelf.Cli.Hosting;

/// <summary>
/// The CLI's composition root: builds the generic host with the same services the desktop app will use.
/// </summary>
internal static class CliHost
{
    /// <summary>
    /// The configuration key, or <c>ModShelf__DatabasePath</c> as an environment variable, that moves the
    /// library database away from its default location.
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

        // Logs go to stderr so they never mix with a command's output; appsettings.json keeps them to warnings.
        builder.Services.Configure<ConsoleLoggerOptions>(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
        builder.Services.AddSingleton<IHostLifetime, CliHostLifetime>();

        string? configuredPath = builder.Configuration[DatabasePathKey];
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            builder.Services.AddLibraryDatabase();
        }
        else
        {
            builder.Services.AddLibraryDatabase(Path.GetFullPath(configuredPath));
        }

        builder.Services.AddPlatformServices(SecretStoreOptionsFrom(builder.Configuration));
        builder.Services.AddHttpFileTransfer();
        builder.Services.AddSourceHttpClients();
        builder.Services.AddDownloadEngine();
        builder.Services.AddAccounts();
        builder.Services.AddDirectUrlSource();
        if (runDownloads)
        {
            builder.Services.AddBackgroundDownloads();
        }

        configureServices?.Invoke(builder.Services);
        return builder.Build();
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
