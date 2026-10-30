using System.Data.Common;
using System.Globalization;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ModShelf.Infrastructure.Persistence;

/// <summary>
/// Has SQLite wait for another connection's lock on the library database, for as long as a command may run, rather than
/// fail at once with "database is locked".
/// </summary>
/// <remarks>
/// Microsoft.Data.Sqlite waits by running a statement again while SQLite says the database is busy, but only while it
/// steps the statement. A change that returns values, as EF Core's inserts do, is committed when the statement ends,
/// which it doesn't run again; so a job added while another connection read the database failed. SQLite's own busy
/// timeout covers that wait too.
/// </remarks>
internal sealed class SqliteBusyTimeout : DbConnectionInterceptor
{
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        if (connection is SqliteConnection sqlite)
        {
            using SqliteCommand command = Command(sqlite);
            command.ExecuteNonQuery();
        }
    }

    public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        if (connection is SqliteConnection sqlite)
        {
            SqliteCommand command = Command(sqlite);
            await using (command.ConfigureAwait(false))
            {
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static SqliteCommand Command(SqliteConnection connection)
    {
        SqliteCommand command = connection.CreateCommand();
        command.CommandText = string.Create(CultureInfo.InvariantCulture, $"PRAGMA busy_timeout = {connection.DefaultTimeout * 1000}");
        return command;
    }
}
