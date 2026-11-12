using ModShelf.Platform.Links;

namespace ModShelf.Platform.Installed;

/// <summary>
/// Where the Steam client may be on this system, in the order to look: on Windows, where the registry says it is, the
/// user's own then the computer's, then its default folder; on macOS, <c>~/Library/Application Support/Steam</c>; on
/// Linux, Flatpak's copy, then <c>~/.local/share/Steam</c> (or under <c>XDG_DATA_HOME</c>) and the <c>~/.steam</c>
/// links, then Snap's copy.
/// </summary>
internal static class SteamFolders
{
    private const string UserKey = @"Software\Valve\Steam";
    private const string ComputerKey = @"SOFTWARE\WOW6432Node\Valve\Steam";
    private const string ComputerKey64 = @"SOFTWARE\Valve\Steam";
    private const string Flatpak = "com.valvesoftware.Steam";

    /// <summary>
    /// The folders to look in on this system.
    /// </summary>
    public static IReadOnlyList<string> ForThisSystem()
    {
        if (OperatingSystem.IsWindows())
        {
            return OnWindows(
                new WindowsRegistryTree(Microsoft.Win32.Registry.CurrentUser),
                new WindowsRegistryTree(Microsoft.Win32.Registry.LocalMachine),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
        }

        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(home))
        {
            return [];
        }

        return OperatingSystem.IsMacOS()
            ? [Path.Combine(home, "Library", "Application Support", "Steam")]
            : OnLinux(home, Environment.GetEnvironmentVariable("XDG_DATA_HOME"));
    }

    /// <param name="user">The user's hive, <c>HKEY_CURRENT_USER</c>.</param>
    /// <param name="computer">The computer's, <c>HKEY_LOCAL_MACHINE</c>.</param>
    /// <param name="programFiles">The 32-bit programs' folder, where Steam installs by default; empty for none.</param>
    internal static IReadOnlyList<string> OnWindows(IRegistryTree user, IRegistryTree computer, string programFiles)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(computer);

        List<string> folders = [];
        Add(folders, () => user.GetString(UserKey, "SteamPath"));
        Add(folders, () => computer.GetString(ComputerKey, "InstallPath"));
        Add(folders, () => computer.GetString(ComputerKey64, "InstallPath"));
        if (!string.IsNullOrEmpty(programFiles))
        {
            Add(folders, () => Path.Combine(programFiles, "Steam"));
        }

        return folders;
    }

    /// <param name="home">The user's home folder.</param>
    /// <param name="dataHome"><c>XDG_DATA_HOME</c>; null or not a full path for <c>~/.local/share</c>.</param>
    internal static IReadOnlyList<string> OnLinux(string home, string? dataHome)
    {
        ArgumentException.ThrowIfNullOrEmpty(home);

        string data = string.IsNullOrWhiteSpace(dataHome) || !Path.IsPathFullyQualified(dataHome) ? Path.Combine(home, ".local", "share") : dataHome;
        string flatpak = Path.Combine(home, ".var", "app", Flatpak);
        string snap = Path.Combine(home, "snap", "steam", "common");
        return
        [
            Path.Combine(flatpak, ".local", "share", "Steam"),
            Path.Combine(flatpak, ".steam", "steam"),
            Path.Combine(data, "Steam"),
            Path.Combine(home, ".steam", "steam"),
            Path.Combine(home, ".steam", "root"),
            Path.Combine(snap, ".local", "share", "Steam"),
            Path.Combine(snap, ".steam", "steam"),
        ];
    }

    /// <summary>
    /// Adds a folder the registry names, written with either slash, when it's a full path not listed yet; a key that
    /// can't be read is passed over.
    /// </summary>
    private static void Add(List<string> folders, Func<string?> read)
    {
        string? folder;
        try
        {
            folder = read();
        }
        catch (UnauthorizedAccessException)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(folder) || !Path.IsPathFullyQualified(folder))
        {
            return;
        }

        string full = Path.GetFullPath(folder);
        if (!folders.Contains(full, StringComparer.OrdinalIgnoreCase))
        {
            folders.Add(full);
        }
    }
}
