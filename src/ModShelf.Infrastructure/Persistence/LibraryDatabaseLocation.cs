namespace ModShelf.Infrastructure.Persistence;

/// <summary>
/// Where the library database lives unless the host is told otherwise.
/// </summary>
public static class LibraryDatabaseLocation
{
    /// <summary>
    /// <c>library.db</c> in a ModShelf folder under the user's local application data: <c>%LOCALAPPDATA%\ModShelf</c>
    /// on Windows, <c>~/Library/Application Support/ModShelf</c> on macOS, and <c>$XDG_DATA_HOME/ModShelf</c>
    /// (usually <c>~/.local/share/ModShelf</c>) on Linux.
    /// </summary>
    /// <exception cref="InvalidOperationException">The system has no local application data folder for this user.</exception>
    public static string GetDefaultPath()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrEmpty(appData))
        {
            throw new InvalidOperationException(
                "This user has no local application data folder, so the library database needs an explicit path.");
        }

        return Path.Combine(appData, "ModShelf", "library.db");
    }
}
