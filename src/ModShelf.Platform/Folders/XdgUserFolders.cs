using ModShelf.Application.Settings.Ports;

namespace ModShelf.Platform.Folders;

/// <summary>
/// The user's folders on Linux and other freedesktop systems, as <c>xdg-user-dirs</c> records them in
/// <c>user-dirs.dirs</c>, such as <c>XDG_DOWNLOAD_DIR="$HOME/Downloads"</c>. A folder it doesn't name is in the home
/// folder under its English name.
/// </summary>
internal sealed class XdgUserFolders : IUserFolders
{
    private readonly string _home;
    private readonly string _configHome;

    /// <param name="home">The user's home folder.</param>
    /// <param name="configHome">The XDG configuration folder, which holds <c>user-dirs.dirs</c>.</param>
    public XdgUserFolders(string home, string configHome)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(home);
        ArgumentException.ThrowIfNullOrWhiteSpace(configHome);

        _home = home;
        _configHome = configHome;
    }

    public string Downloads => Folder("XDG_DOWNLOAD_DIR") ?? Path.Combine(_home, "Downloads");

    /// <summary>
    /// The folders as this user's environment says: <c>$XDG_CONFIG_HOME</c>, or <c>~/.config</c>.
    /// </summary>
    public static XdgUserFolders ForThisUser()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string? configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        return new XdgUserFolders(home, Path.IsPathFullyQualified(configHome ?? string.Empty) ? configHome! : Path.Combine(home, ".config"));
    }

    /// <summary>
    /// The folder <paramref name="name"/> names, when the file names one that is absolute or in the home folder, as the
    /// format allows; otherwise null.
    /// </summary>
    private string? Folder(string name)
    {
        string file = Path.Combine(_configHome, "user-dirs.dirs");
        string[] lines;
        try
        {
            lines = File.ReadAllLines(file);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        foreach (string line in lines)
        {
            string[] parts = line.Trim().Split('=', 2);
            if (parts.Length != 2 || parts[0].Trim() != name)
            {
                continue;
            }

            string value = parts[1].Trim().Trim('"');
            if (value.StartsWith("$HOME/", StringComparison.Ordinal))
            {
                return Path.Combine(_home, value["$HOME/".Length..]);
            }

            return value.StartsWith('/') ? value : null;
        }

        return null;
    }
}
