namespace ModShelf.Application.Library.Ports;

/// <summary>
/// What the library needs to know about the disk it keeps its files on. Implemented by Infrastructure.
/// </summary>
public interface ILibraryStorage
{
    /// <summary>
    /// Whether a file is at <paramref name="path"/>, an absolute path.
    /// </summary>
    bool FileExists(string path);

    /// <summary>
    /// Checks that this system can save a download at <paramref name="path"/>, an absolute path, with the room the
    /// download needs while it's under way.
    /// </summary>
    /// <exception cref="PathTooLongException">The path is too long for this system. The message says what to do.</exception>
    void EnsurePathFits(string path);
}
