namespace ModShelf.Application.Library.Ports;

/// <summary>
/// What the library needs to know about, and do to, the disk it keeps its files on. Implemented by Infrastructure.
/// Every path is absolute.
/// </summary>
public interface ILibraryStorage
{
    /// <summary>
    /// Whether a file is at <paramref name="path"/>.
    /// </summary>
    bool FileExists(string path);

    /// <summary>
    /// The size of the file at <paramref name="path"/>, or null when there's no file there.
    /// </summary>
    long? FileSize(string path);

    /// <summary>
    /// The files directly in the folder at <paramref name="path"/>, by their full paths, leaving out downloads under way:
    /// none when there's no folder there.
    /// </summary>
    /// <exception cref="IOException">The folder couldn't be read.</exception>
    /// <exception cref="UnauthorizedAccessException">This user may not list it.</exception>
    IReadOnlyList<string> FilesIn(string path);

    /// <summary>
    /// The SHA-256 of the file at <paramref name="path"/>, read through once, as 64 lowercase hexadecimal digits.
    /// </summary>
    /// <exception cref="FileNotFoundException">There's no file there.</exception>
    /// <exception cref="IOException">The file couldn't be read, such as when another program has it locked.</exception>
    /// <exception cref="UnauthorizedAccessException">This user may not read it.</exception>
    Task<string> Sha256Async(string path, CancellationToken cancellationToken);

    /// <summary>
    /// Whether two paths lead to the same folder, however differently they name it: through a link or junction,
    /// another drive letter, or in another case or Unicode form on a file system that ignores them. False when either
    /// isn't a folder.
    /// </summary>
    bool IsSameFolder(string first, string second);

    /// <summary>
    /// Checks that this system can save a download at <paramref name="path"/>, with the room the download needs while
    /// it's under way.
    /// </summary>
    /// <exception cref="PathTooLongException">The path is too long for this system. The message says what to do.</exception>
    void EnsurePathFits(string path);

    /// <summary>
    /// Moves a file, making the folders it goes into. A move to another disk copies the file, then deletes it where it was.
    /// </summary>
    /// <exception cref="IOException">
    /// The file couldn't be moved, such as when a file is at <paramref name="destination"/> already, which is never
    /// overwritten.
    /// </exception>
    /// <exception cref="UnauthorizedAccessException">This user may not move it.</exception>
    void MoveFile(string source, string destination);

    /// <summary>
    /// Deletes a file for good, or does nothing when there's none at <paramref name="path"/>.
    /// </summary>
    /// <exception cref="IOException">The file couldn't be deleted, such as when another program has it open.</exception>
    /// <exception cref="UnauthorizedAccessException">This user may not delete it.</exception>
    void DeleteFile(string path);

    /// <summary>
    /// Removes the folder at <paramref name="path"/> if it's there and holds nothing.
    /// </summary>
    /// <returns>Whether it removed it.</returns>
    bool RemoveFolderIfEmpty(string path);
}
