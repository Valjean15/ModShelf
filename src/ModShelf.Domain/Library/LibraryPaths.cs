using System.Runtime.CompilerServices;

using ModShelf.Domain.Common;

namespace ModShelf.Domain.Library;

/// <summary>
/// The rule for where a library file is: a path relative to the library root, with '/' between folders, so that moving
/// the root, even to another system, moves every file with it.
/// </summary>
internal static class LibraryPaths
{
    public const char Separator = '/';

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
}
