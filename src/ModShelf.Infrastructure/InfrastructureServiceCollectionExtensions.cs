using System.Net.Http.Headers;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;

using ModShelf.Application.Accounts;
using ModShelf.Application.Accounts.Ports;
using ModShelf.Application.Downloads.Ports;
using ModShelf.Application.Library.Ports;
using ModShelf.Application.Settings.Ports;
using ModShelf.Infrastructure.Downloads;
using ModShelf.Infrastructure.Library;
using ModShelf.Infrastructure.Logging;
using ModShelf.Infrastructure.Persistence;
using ModShelf.Infrastructure.Persistence.Repositories;
using ModShelf.Infrastructure.Settings;
using ModShelf.Infrastructure.Sources;
using ModShelf.Sources.Abstractions.Http;

using Polly;

namespace ModShelf.Infrastructure;

/// <summary>
/// Registers Infrastructure's services. The hosts call these from their composition roots.
/// </summary>
public static class InfrastructureServiceCollectionExtensions
{
    private static readonly string Version = typeof(HttpFileTransfer).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    /// <summary>
    /// Registers <see cref="HttpFileTransfer"/> as the <see cref="IFileTransfer"/>, with an HTTP client that
    /// retries transient failures with exponential backoff. The pipeline has no overall timeout on purpose:
    /// a large download can take hours, and stalls are caught by <see cref="FileTransferOptions.ReadTimeout"/>.
    /// </summary>
    public static IServiceCollection AddHttpFileTransfer(this IServiceCollection services, FileTransferOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        FileTransferOptions settings = options ?? new FileTransferOptions();

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton(settings);
        services.AddSingleton<IFileTransfer, HttpFileTransfer>();

        services
            .AddHttpClient(
                HttpFileTransfer.HttpClientName,
                client => client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ModShelf", Version)))

            // The factory's built-in request logging writes each URL's host and path, which can carry
            // signatures. HttpFileTransfer logs what's needed (the host and the file name) itself.
            .RemoveAllLoggers()
            .AddResilienceHandler("file-transfer", pipeline =>
            {
                if (settings.MaxRetryAttempts > 0)
                {
                    // Retries 5xx, 408, 429 and connection errors, honouring Retry-After. Expired links
                    // (401, 403, 404, 410) aren't transient, so they come straight back to the scheduler.
                    pipeline.AddRetry(new HttpRetryStrategyOptions
                    {
                        MaxRetryAttempts = settings.MaxRetryAttempts,
                        Delay = settings.RetryDelay,
                    });
                }
            });

        return services;
    }

    /// <summary>
    /// Sets up the HTTP client each source asks for by <see cref="SourceHttpClients.NameFor"/>: every request gets the
    /// source's linked account through <see cref="ICredentialProvider"/>, and a request refused with a 401 is sent
    /// once more with refreshed credentials. Needs the provider, which Application's <c>AddAccounts()</c> registers.
    /// </summary>
    public static IServiceCollection AddSourceHttpClients(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHttpClient();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHttpMessageHandlerBuilderFilter, SourceHttpClientFilter>());
        return services;
    }

    /// <summary>
    /// Registers ModShelf's files: the library database at <paramref name="databasePath"/>, or by default
    /// <c>library.db</c> in a ModShelf folder under the user's local application data (<c>%LOCALAPPDATA%\ModShelf</c>
    /// on Windows, <c>~/Library/Application Support/ModShelf</c> on macOS, <c>~/.local/share/ModShelf</c> on Linux);
    /// and beside it the settings, <c>settings.json</c>, and the log files, in a <c>logs</c> folder. A database moved
    /// elsewhere, such as for tests, takes them along.
    /// </summary>
    /// <param name="databasePath">The database's absolute path, or null for its default one.</param>
    /// <param name="app">What the host's log files' names start with, such as <c>desktop</c>.</param>
    /// <returns>The folder they're all in.</returns>
    /// <exception cref="InvalidOperationException">No path is given, and the system has no local application data folder for this user.</exception>
    public static string AddLibraryFiles(this IServiceCollection services, string? databasePath, string app)
    {
        ArgumentNullException.ThrowIfNull(services);

        string database = databasePath ?? LibraryDatabaseLocation.GetDefaultPath();
        string folder = Path.GetDirectoryName(Path.GetFullPath(database))!;
        services.AddLibraryDatabase(database);
        services.AddSettingsFile(Path.Combine(folder, "settings.json"));
        services.AddLogFiles(Path.Combine(folder, "logs"), app);
        return folder;
    }

    /// <summary>
    /// Keeps the settings in the JSON file at <paramref name="path"/>, an absolute path, as the
    /// <see cref="ISettingsStore"/>. The file and its folder are created when settings are first saved.
    /// </summary>
    public static IServiceCollection AddSettingsFile(this IServiceCollection services, string path)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException($"'{path}' is not an absolute path.", nameof(path));
        }

        services.AddSingleton<ISettingsStore>(new JsonSettingsStore(path));

        return services;
    }

    /// <summary>
    /// Registers the file system as the <see cref="ILibraryStorage"/>, which Application's library downloads check files
    /// and paths with.
    /// </summary>
    public static IServiceCollection AddLibraryStorage(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<ILibraryStorage>(new FileSystemLibraryStorage());

        return services;
    }

    /// <summary>
    /// Registers the library database at <paramref name="databasePath"/>: the <see cref="ModShelfDbContext"/>
    /// factory, the repositories (<see cref="IDownloadJobRepository"/>, <see cref="ILinkedAccountRepository"/>,
    /// <see cref="ILibraryRepository"/>), the <see cref="IDownloadQueueLock"/> beside the database, and
    /// a hosted service that creates or migrates the database before any other hosted service starts.
    /// </summary>
    /// <param name="databasePath">The absolute path of the database file. Its folder is created if missing.</param>
    public static IServiceCollection AddLibraryDatabase(this IServiceCollection services, string databasePath)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        if (!Path.IsPathFullyQualified(databasePath))
        {
            throw new ArgumentException($"'{databasePath}' is not an absolute path.", nameof(databasePath));
        }

        // The builder quotes the path, so spaces and semicolons in it are safe.
        string connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
        }.ToString();

        services.AddSingleton(new LibraryDatabaseOptions(databasePath));
        services.AddDbContextFactory<ModShelfDbContext>(options => options.UseSqlite(connectionString).AddInterceptors(new SqliteBusyTimeout()));
        services.AddSingleton<IDownloadJobRepository, DownloadJobRepository>();
        services.AddSingleton<ILinkedAccountRepository, LinkedAccountRepository>();
        services.AddSingleton<ILibraryRepository, LibraryRepository>();
        services.AddSingleton<IDownloadQueueLock, FileDownloadQueueLock>();
        services.AddHostedService<LibraryDatabaseMigrator>();

        return services;
    }

    /// <summary>
    /// Writes the logs to files in <paramref name="folder"/>, an absolute path, a file a day for the last week, such as
    /// <c>desktop-20261029.log</c>, and registers the <see cref="LogFolder"/>. The configuration's <c>Logging:File</c>
    /// section sets what's written, as <c>Logging:Console</c> does for the console; without it, the general rules do.
    /// </summary>
    /// <param name="app">What the files' names start with, such as <c>desktop</c>; each app keeps its own files.</param>
    public static IServiceCollection AddLogFiles(this IServiceCollection services, string folder, string app)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        ArgumentException.ThrowIfNullOrWhiteSpace(app);
        if (!Path.IsPathFullyQualified(folder))
        {
            throw new ArgumentException($"'{folder}' is not an absolute path.", nameof(folder));
        }

        services.AddSingleton(new LogFolder(folder));
        services.AddSingleton<ILoggerProvider>(provider => new FileLoggerProvider(
            folder,
            app,
            provider.GetService<TimeProvider>() ?? TimeProvider.System));
        return services;
    }
}
