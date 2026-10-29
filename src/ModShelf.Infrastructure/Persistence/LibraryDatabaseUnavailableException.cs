using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ModShelf.Infrastructure.Persistence;

/// <summary>
/// The library database can't be opened or brought up to date, or a change to it failed: it isn't a database or is
/// damaged, another program holds it, or it, its folder or the disk can't be read or written. The message says what the
/// user can do. ModShelf leaves the file as it is.
/// </summary>
public sealed class LibraryDatabaseUnavailableException : Exception
{
    public LibraryDatabaseUnavailableException()
        : base("The library database can't be opened.")
    {
    }

    public LibraryDatabaseUnavailableException(string message)
        : base(message)
    {
    }

    public LibraryDatabaseUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// What to tell the user when a change to the library database failed after it opened, such as while another program
    /// holds it past SQLite's wait, or the disk is full: the problem, and what they can do about it.
    /// </summary>
    /// <returns>The exception to report, or null when <paramref name="exception"/> isn't the database's.</returns>
    public static LibraryDatabaseUnavailableException? FromFailedChange(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        SqliteException? database = exception as SqliteException ?? (exception as DbUpdateException)?.InnerException as SqliteException;
        return database is null
            ? null
            : new LibraryDatabaseUnavailableException($"The library database couldn't be changed: {LibraryDatabaseMigrator.Problem(database)}", exception);
    }
}
