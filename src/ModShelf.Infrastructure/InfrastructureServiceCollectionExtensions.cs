using System.Net.Http.Headers;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;

using ModShelf.Application.Accounts.Ports;
using ModShelf.Application.Downloads.Ports;
using ModShelf.Infrastructure.Downloads;
using ModShelf.Infrastructure.Persistence;
using ModShelf.Infrastructure.Persistence.Repositories;

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
    /// Registers the library database at its default location: <c>library.db</c> in a ModShelf folder under the
    /// user's local application data (<c>%LOCALAPPDATA%\ModShelf</c> on Windows,
    /// <c>~/Library/Application Support/ModShelf</c> on macOS, <c>~/.local/share/ModShelf</c> on Linux).
    /// </summary>
    /// <exception cref="InvalidOperationException">The system has no local application data folder for this user.</exception>
    public static IServiceCollection AddLibraryDatabase(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        return services.AddLibraryDatabase(LibraryDatabaseLocation.GetDefaultPath());
    }

    /// <summary>
    /// Registers the library database at <paramref name="databasePath"/>: the <see cref="ModShelfDbContext"/>
    /// factory, the repositories (<see cref="IDownloadJobRepository"/>, <see cref="ILinkedAccountRepository"/>), and
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
        services.AddDbContextFactory<ModShelfDbContext>(options => options.UseSqlite(connectionString));
        services.AddSingleton<IDownloadJobRepository, DownloadJobRepository>();
        services.AddSingleton<ILinkedAccountRepository, LinkedAccountRepository>();
        services.AddHostedService<LibraryDatabaseMigrator>();

        return services;
    }
}
