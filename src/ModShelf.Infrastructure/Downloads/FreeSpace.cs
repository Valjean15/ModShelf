using System.Security;

namespace ModShelf.Infrastructure.Downloads;

/// <summary>
/// How much room the disk that holds a folder has left.
/// </summary>
internal static class FreeSpace
{
    /// <summary>
    /// The bytes this user may still write on the volume that holds <paramref name="folder"/>; or null when the system
    /// can't tell, such as for a network share on Windows.
    /// </summary>
    public static long? Available(string folder)
    {
        try
        {
            string path = Path.GetFullPath(folder);
            DriveInfo? volume = OperatingSystem.IsWindows()
                ? new DriveInfo(Path.GetPathRoot(path)!)
                : DriveInfo.GetDrives()
                    .Where(drive => Holds(drive.RootDirectory.FullName, path))
                    .MaxBy(drive => drive.RootDirectory.FullName.Length);
            return volume is { IsReady: true } ? volume.AvailableFreeSpace : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or SecurityException)
        {
            return null;
        }
    }

    /// <summary>
    /// Whether <paramref name="path"/> is at or under the mount point <paramref name="root"/>, on Linux or macOS. The
    /// mount point that holds a path is the longest one it's under.
    /// </summary>
    internal static bool Holds(string root, string path)
    {
        StringComparison comparison = OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        string under = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
        return path.Equals(Path.TrimEndingDirectorySeparator(root), comparison) || path.StartsWith(under, comparison);
    }
}
