namespace ModShelf.Domain.Common;

/// <summary>
/// How paths compare on the system ModShelf runs on.
/// </summary>
public static class FilePaths
{
    /// <summary>
    /// How this system compares paths: case doesn't count on Windows and macOS, and does elsewhere.
    /// </summary>
    public static StringComparison Comparison
        => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>
    /// Whether two paths name the same file or folder, as this system compares them: as full paths, without a trailing
    /// separator. Links on the disk aren't followed.
    /// </summary>
    public static bool Same(string first, string second)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(first);
        ArgumentException.ThrowIfNullOrWhiteSpace(second);

        return string.Equals(Full(first), Full(second), Comparison);
    }

    private static string Full(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
}
