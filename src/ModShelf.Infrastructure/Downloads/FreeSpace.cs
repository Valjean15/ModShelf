using System.Security;

using ModShelf.Domain.Common;

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
            // A folder behind a link, such as a Downloads folder linked to another disk, is on the link's target's disk.
            string path = RealPath(folder);
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
    /// <paramref name="path"/> as a full path with every link in it followed, symbolic links and junctions alike, as far
    /// as its folders exist; the rest, still to be made, as it is.
    /// </summary>
    internal static string RealPath(string path)
    {
        string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        string root = Path.GetPathRoot(full)!;
        string[] parts = full[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        string current = root;
        for (int index = 0; index < parts.Length; index++)
        {
            string next = Path.Combine(current, parts[index]);
            if (!Directory.Exists(next))
            {
                return Path.Combine([current, .. parts[index..]]);
            }

            current = new DirectoryInfo(next).ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? next;
        }

        return current;
    }

    /// <summary>
    /// Whether <paramref name="path"/> is at or under the mount point <paramref name="root"/>, on Linux or macOS. The
    /// mount point that holds a path is the longest one it's under.
    /// </summary>
    internal static bool Holds(string root, string path)
    {
        string under = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
        return path.Equals(Path.TrimEndingDirectorySeparator(root), FilePaths.Comparison) || path.StartsWith(under, FilePaths.Comparison);
    }
}
