namespace ModShelf.Platform.Agents;

/// <summary>
/// The folder of Claude Desktop's configuration: on Windows, <c>%APPDATA%\Claude</c>, or, for the app from the Microsoft
/// Store or another MSIX installer, the copy of it the system keeps in the app's package,
/// <c>%LOCALAPPDATA%\Packages\Claude_…\LocalCache\Roaming\Claude</c>, which is the one that app reads; on macOS,
/// <c>~/Library/Application Support/Claude</c>; on Linux, <c>~/.config/Claude</c>.
/// </summary>
internal static class ClaudeDesktopFolder
{
    /// <summary>
    /// The folder on this system.
    /// </summary>
    public static string? ForThisSystem()
    {
        if (OperatingSystem.IsWindows())
        {
            return OnWindows(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                Directory.Exists);
        }

        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(home))
        {
            return null;
        }

        if (OperatingSystem.IsMacOS())
        {
            return Path.Combine(home, "Library", "Application Support", "Claude");
        }

        string? config = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        return Path.Combine(string.IsNullOrWhiteSpace(config) || !Path.IsPathFullyQualified(config) ? Path.Combine(home, ".config") : config, "Claude");
    }

    /// <param name="roaming">The user's roaming application data, <c>%APPDATA%</c>.</param>
    /// <param name="local">The user's local application data, <c>%LOCALAPPDATA%</c>.</param>
    /// <param name="exists">Whether a folder is there; <see cref="Directory.Exists"/> but in tests.</param>
    internal static string? OnWindows(string roaming, string local, Func<string, bool> exists)
    {
        ArgumentNullException.ThrowIfNull(exists);

        string packages = Path.Combine(local, "Packages");
        if (!string.IsNullOrEmpty(local) && exists(packages))
        {
            string? package = Packages(packages).FirstOrDefault(folder => Path.GetFileName(folder).StartsWith("Claude_", StringComparison.OrdinalIgnoreCase));
            if (package is not null)
            {
                return Path.Combine(package, "LocalCache", "Roaming", "Claude");
            }
        }

        return string.IsNullOrEmpty(roaming) ? null : Path.Combine(roaming, "Claude");
    }

    private static List<string> Packages(string packages)
    {
        try
        {
            return [.. Directory.EnumerateDirectories(packages, "Claude_*").Order(StringComparer.OrdinalIgnoreCase)];
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }
}
