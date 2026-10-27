using ModShelf.Application.Library.Ports;
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

    public FileSystemLibraryStorage()
        : this(OperatingSystem.IsWindows())
    {
    }

    /// <param name="isWindows">Whether Windows' limits apply. Tests check them on any system.</param>
    internal FileSystemLibraryStorage(bool isWindows) => _isWindows = isWindows;

    public bool FileExists(string path) => File.Exists(path);

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
}
