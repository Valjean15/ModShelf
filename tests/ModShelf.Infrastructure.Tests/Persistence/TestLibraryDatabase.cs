using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using ModShelf.Application.Accounts.Ports;
using ModShelf.Application.Downloads.Ports;
using ModShelf.Infrastructure.Persistence;
using ModShelf.Testing;

namespace ModShelf.Infrastructure.Tests.Persistence;

/// <summary>
/// A library database in a temp folder, registered the way the hosts register it. Each instance is one
/// "run of the app": <see cref="RestartAsync"/> throws the container away and builds a new one on the same file.
/// </summary>
internal sealed class TestLibraryDatabase : IAsyncDisposable
{
    private readonly TempFolder _folder = new();
    private ServiceProvider _services;

    /// <param name="relativePath">Where the database file goes, relative to a fresh temp folder.</param>
    public TestLibraryDatabase(params string[] relativePath)
    {
        DatabasePath = Path.Combine([_folder.Path, .. relativePath.Length > 0 ? relativePath : ["library.db"]]);
        _services = BuildServices();
    }

    public string DatabasePath { get; }

    public IDownloadJobRepository Repository => _services.GetRequiredService<IDownloadJobRepository>();

    public ILinkedAccountRepository LinkedAccounts => _services.GetRequiredService<ILinkedAccountRepository>();

    public IServiceProvider Services => _services;

    /// <summary>
    /// Runs what the host runs at startup: the hosted services' <see cref="IHostedLifecycleService.StartingAsync"/>.
    /// </summary>
    public async Task StartAsync()
    {
        foreach (IHostedLifecycleService service in _services.GetServices<IHostedService>().OfType<IHostedLifecycleService>())
        {
            await service.StartingAsync(TestContext.Current.CancellationToken);
        }
    }

    /// <summary>
    /// Simulates closing and reopening the app: a new container on the same file, started again.
    /// </summary>
    public async Task RestartAsync()
    {
        await _services.DisposeAsync();
        SqliteConnection.ClearAllPools();
        _services = BuildServices();
        await StartAsync();
    }

    public async Task<ModShelfDbContext> CreateContextAsync()
        => await _services.GetRequiredService<IDbContextFactory<ModShelfDbContext>>()
            .CreateDbContextAsync(TestContext.Current.CancellationToken);

    /// <summary>
    /// Runs SQL straight against the file, outside EF Core, and returns the first column of the first row.
    /// </summary>
    public async Task<object?> QueryAsync(string sql)
    {
        await using SqliteConnection connection = new(
            new SqliteConnectionStringBuilder { DataSource = DatabasePath, Pooling = false }.ToString());
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await _services.DisposeAsync();

        // Pooled connections keep the file open, and Windows won't delete an open file.
        SqliteConnection.ClearAllPools();
        _folder.Dispose();
    }

    private ServiceProvider BuildServices()
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddLibraryDatabase(DatabasePath);
        return services.BuildServiceProvider();
    }
}
