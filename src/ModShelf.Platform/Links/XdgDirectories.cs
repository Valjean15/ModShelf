namespace ModShelf.Platform.Links;

/// <summary>
/// Where a freedesktop.org system keeps its configuration and its applications, as the XDG Base Directory
/// specification says.
/// </summary>
/// <param name="ConfigHome">The user's configuration folder, such as <c>~/.config</c>.</param>
/// <param name="DataHome">The user's data folder, such as <c>~/.local/share</c>.</param>
/// <param name="ConfigDirs">The system's configuration folders, most important first.</param>
/// <param name="DataDirs">The system's data folders, most important first.</param>
/// <param name="Desktops">The desktop environments in use, lowercase, such as <c>gnome</c>.</param>
internal sealed record XdgDirectories(
    string ConfigHome,
    string DataHome,
    IReadOnlyList<string> ConfigDirs,
    IReadOnlyList<string> DataDirs,
    IReadOnlyList<string> Desktops)
{
    /// <summary>
    /// The folders as this user's environment says, with the specification's defaults for what it leaves out.
    /// </summary>
    public static XdgDirectories ForThisUser()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return new XdgDirectories(
            Folder("XDG_CONFIG_HOME") ?? Path.Combine(home, ".config"),
            Folder("XDG_DATA_HOME") ?? Path.Combine(home, ".local", "share"),
            Folders("XDG_CONFIG_DIRS", "/etc/xdg"),
            Folders("XDG_DATA_DIRS", "/usr/local/share", "/usr/share"),
            [.. (Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") ?? string.Empty)
                .Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(desktop => desktop.ToLowerInvariant())]);
    }

    // The specification ignores a relative path in these variables.
    private static string? Folder(string variable)
        => Environment.GetEnvironmentVariable(variable) is { } folder && Path.IsPathFullyQualified(folder) ? folder : null;

    private static string[] Folders(string variable, params string[] defaults)
    {
        string[] folders = [.. (Environment.GetEnvironmentVariable(variable) ?? string.Empty)
            .Split(':', StringSplitOptions.RemoveEmptyEntries)
            .Where(Path.IsPathFullyQualified)];
        return folders.Length > 0 ? folders : defaults;
    }
}
