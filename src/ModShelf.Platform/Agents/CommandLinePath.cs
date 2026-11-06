namespace ModShelf.Platform.Agents;

/// <summary>
/// Where the command line's executable is: as configured; beside the desktop app, as in an installed ModShelf; or, in a
/// build from source, in the command line's own build folder beside the desktop app's.
/// </summary>
internal static class CommandLinePath
{
    /// <summary>
    /// The executable's name on this system.
    /// </summary>
    internal static string ExecutableName => OperatingSystem.IsWindows() ? "ModShelf.Cli.exe" : "ModShelf.Cli";

    /// <param name="configured">The executable as configured; null for none.</param>
    /// <param name="appFolder">The desktop app's folder.</param>
    /// <param name="exists">Whether a file is there; <see cref="File.Exists"/> but in tests.</param>
    /// <returns>The executable, a full path; null when it's in none of those places.</returns>
    public static string? Find(string? configured, string appFolder, Func<string, bool> exists)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appFolder);
        ArgumentNullException.ThrowIfNull(exists);

        if (!string.IsNullOrWhiteSpace(configured))
        {
            string path = Path.GetFullPath(configured);
            return exists(path) ? path : null;
        }

        string beside = Path.Combine(appFolder, ExecutableName);
        if (exists(beside))
        {
            return beside;
        }

        // src/ModShelf.Desktop/bin/<configuration>/<framework>/ → src/ModShelf.Cli/bin/<configuration>/<framework>/
        string[] parts = Path.TrimEndingDirectorySeparator(Path.GetFullPath(appFolder)).Split(Path.DirectorySeparatorChar);
        int desktop = Array.LastIndexOf(parts, "ModShelf.Desktop");
        if (desktop >= 0 && desktop + 1 < parts.Length && parts[desktop + 1] == "bin")
        {
            parts[desktop] = "ModShelf.Cli";
            string sibling = Path.Combine(string.Join(Path.DirectorySeparatorChar, parts), ExecutableName);
            if (exists(sibling))
            {
                return sibling;
            }
        }

        return null;
    }
}
