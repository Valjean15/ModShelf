using ModShelf.Application.Library.Ports;
using ModShelf.Domain.Common;
using ModShelf.Infrastructure.Downloads;

namespace ModShelf.Infrastructure.Library;

/// <summary>
/// The library's files, on this system's file system.
/// </summary>
internal sealed class FileSystemLibraryStorage : ILibraryStorage
{
    /// <summary>
    /// The longest path Windows takes in most programs: 260 characters, less the one that ends it.
    /// </summary>
    /// <remarks>
    /// .NET can go past it, but Explorer, archivers and many mod managers can't, so a file beyond it couldn't be used.
    /// </remarks>
    internal const int MaxWindowsPath = 259;

    private readonly bool _isWindows;
    private readonly Func<string> _markName;

    public FileSystemLibraryStorage()
        : this(OperatingSystem.IsWindows())
    {
    }

    /// <param name="isWindows">Whether Windows' limits apply. Tests check them on any system.</param>
    /// <param name="markName">The name of the file that marks a folder while it's looked for under another name.</param>
    internal FileSystemLibraryStorage(bool isWindows, Func<string>? markName = null)
    {
        _isWindows = isWindows;
        _markName = markName ?? (() => $".modshelf-{Guid.NewGuid():N}.tmp");
    }

    public bool FileExists(string path) => File.Exists(path);

    public bool IsSameFolder(string first, string second)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(first);
        ArgumentException.ThrowIfNullOrWhiteSpace(second);

        if (!Directory.Exists(first) || !Directory.Exists(second))
        {
            return false;
        }

        // A file put in the first is in the second only when they're one folder, whatever names, links, drives or
        // file system rules lead there. It's deleted as it's closed.
        string name = _markName();
        try
        {
            using FileStream mark = new(
                Path.Combine(first, name), FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.DeleteOnClose);
            return File.Exists(Path.Combine(second, name));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A folder this user can't write in: compared by name, as this system does, with its links followed.
            return FilePaths.Same(FreeSpace.RealPath(first), FreeSpace.RealPath(second));
        }
    }

    public long? FileSize(string path)
    {
        FileInfo file = new(path);
        return file.Exists ? file.Length : null;
    }

    public void EnsurePathFits(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        // While it downloads, the file has a longer name.
        if (_isWindows && PartialFiles.PathFor(path).Length > MaxWindowsPath)
        {
            throw new PathTooLongException(
                $"{path} is too long for Windows, where a file in the library can have {MaxWindowsPath - PartialFiles.Extension.Length} "
                + "characters at most. Choose a shorter library root, or a folder pattern with fewer or shorter parts.");
        }
    }

    public void MoveFile(string source, string destination)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destination))!);
        File.Move(source, destination, overwrite: false);
    }

    public void DeleteFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        // File.Delete does nothing for a file that isn't there, but throws for a folder that isn't.
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    public bool RemoveFolderIfEmpty(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        try
        {
            if (!Directory.Exists(path) || Directory.EnumerateFileSystemEntries(path).Any())
            {
                return false;
            }

            Directory.Delete(path, recursive: false);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Something came into it, or it's in use: it stays.
            return false;
        }
    }
}
