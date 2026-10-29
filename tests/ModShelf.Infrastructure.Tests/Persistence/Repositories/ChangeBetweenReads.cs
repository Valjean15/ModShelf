using System.Data.Common;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

using ModShelf.Infrastructure.Persistence;

namespace ModShelf.Infrastructure.Tests.Persistence.Repositories;

/// <summary>
/// Contexts on a test's database that, just before the second query of a context's read, start a change through
/// another connection, as another process would, and give it a while to finish.
/// </summary>
internal sealed class ChangeBetweenReads(string databasePath, Func<Task> change) : DbCommandInterceptor, IDbContextFactory<ModShelfDbContext>
{
    /// <summary>
    /// How long the read waits for the change: long enough for a change that can go ahead.
    /// </summary>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(1);

    private int _queries;

    /// <summary>
    /// The change, once it started.
    /// </summary>
    public Task? Change { get; private set; }

    public ModShelfDbContext CreateDbContext()
        => new(new DbContextOptionsBuilder<ModShelfDbContext>()
            .UseSqlite(new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString())
            .AddInterceptors(this)
            .Options);

    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        if (Interlocked.Increment(ref _queries) == 2)
        {
            Change = Task.Run(change, cancellationToken);
            await Task.WhenAny(Change, Task.Delay(Patience, cancellationToken)).ConfigureAwait(false);
        }

        return result;
    }
}
