namespace ModShelf.Infrastructure.Persistence;

/// <summary>
/// The library database can't be opened or brought up to date: it isn't a database or is damaged, another program
/// holds it, or it, its folder or the disk can't be read or written. The message names the file and says what the
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
}
