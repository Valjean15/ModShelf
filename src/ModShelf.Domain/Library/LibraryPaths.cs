using System.Runtime.CompilerServices;

using ModShelf.Domain.Common;

namespace ModShelf.Domain.Library;

/// <summary>
/// The rule for where a library file is: a path relative to the library root, with '/' between folders, so that moving
/// the root, even to another system, moves every file with it.
/// </summary>
public static class LibraryPaths
{
    public const char Separator = '/';

    /// <summary>
    /// The absolute path of a library file on this system.
    /// </summary>
    /// <param name="root">The library root, an absolute path.</param>
    /// <param name="relativePath">The file's path in the library, as <see cref="EnsureRelative"/> takes it.</param>
    /// <exception cref="ArgumentException">The root isn't absolute, or the path isn't a library path.</exception>
    public static string Resolve(string root, string relativePath)
    {
        EnsureAbsolute(root);
        EnsureRelative(relativePath);

        return Path.Combine([root, .. relativePath.Split(Separator)]);
    }

    /// <summary>
    /// Where <paramref name="path"/> is in the library: its path relative to the root, with '/' between folders; or null
    /// when it isn't under the root. Case counts as it does for this system's paths: it doesn't on Windows and macOS.
    /// </summary>
    /// <param name="root">The library root, an absolute path.</param>
    /// <param name="path">An absolute path.</param>
    /// <exception cref="ArgumentException">The root or the path isn't absolute.</exception>
    public static string? RelativeTo(string root, string path)
    {
        EnsureAbsolute(root);
        EnsureAbsolute(path);

        string relative = Path.GetRelativePath(root, path);
        bool outside = relative == "."
            || relative == ".."
            || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || Path.IsPathRooted(relative);
        return outside ? null : relative.Replace(Path.DirectorySeparatorChar, Separator);
    }

    /// <summary>
    /// Throws unless <paramref name="path"/> is relative and stays inside the library: folder and file names separated
    /// by '/', none of them blank, "." or "..", and none holding '\' or ':', which would mean another folder or a drive
    /// on Windows.
    /// </summary>
    public static void EnsureRelative(string path, [CallerArgumentExpression(nameof(path))] string? paramName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path, paramName);
        if (path.Contains('\\', StringComparison.Ordinal) || path.Contains(':', StringComparison.Ordinal))
        {
            throw new ArgumentException($"'{path}' isn't a library path: use '/' between folders, and no drive.", paramName);
        }

        foreach (string segment in path.Split(Separator))
        {
            if (segment.Length == 0)
            {
                throw new ArgumentException($"'{path}' isn't a path inside the library.", paramName);
            }

            FileNames.EnsureSingleSegment(segment, paramName);
        }
    }

    private static void EnsureAbsolute(string path, [CallerArgumentExpression(nameof(path))] string? paramName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path, paramName);
        if (!Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException($"'{path}' isn't an absolute path.", paramName);
        }
    }
}
