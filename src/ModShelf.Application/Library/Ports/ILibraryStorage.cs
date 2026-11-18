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
    /// The SHA-256 and the MD5 of the file at <paramref name="path"/>, read through once for both, each as lowercase
    /// hexadecimal digits: the library keeps the first, and a site may find the file by the second (ADR 0101).
    /// </summary>
    /// <inheritdoc cref="Sha256Async" path="/exception"/>
    Task<FileHashes> HashesAsync(string path, CancellationToken cancellationToken);

    /// <summary>
    /// The bytes of a small file, read whole, such as the note a mod manager keeps beside an archive (ADR 0101).
    /// </summary>
    /// <exception cref="FileNotFoundException">There's no file there.</exception>
    /// <exception cref="IOException">
    /// The file couldn't be read, or is larger than <paramref name="maxBytes"/>, so it isn't such a file.
    /// </exception>
    /// <exception cref="UnauthorizedAccessException">This user may not read it.</exception>
    Task<byte[]> ReadSmallFileAsync(string path, int maxBytes, CancellationToken cancellationToken);

    /// <summary>
    /// Copies a file to <paramref name="destination"/>, making the folders it goes into, through a partial file beside
    /// it that a rescan doesn't list, which becomes the file only once it's whole and its SHA-256 is
    /// <paramref name="sha256"/>. The file at <paramref name="source"/> is only read.
    /// </summary>
    /// <remarks>Nothing is left at <paramref name="destination"/> when the copy fails or is cancelled.</remarks>
    /// <exception cref="InvalidDataException">What was copied has another SHA-256: the file changed since it was read.</exception>
    /// <exception cref="IOException">
    /// The file couldn't be copied, such as when a file is at <paramref name="destination"/> already, which is never
    /// overwritten, or the drive is full.
    /// </exception>
    /// <exception cref="UnauthorizedAccessException">This user may not read the file, or write there.</exception>
    Task CopyFileAsync(string source, string destination, string sha256, CancellationToken cancellationToken);

    /// <summary>
    /// Whether two paths lead to the same folder, however differently they name it: through a link or junction,
    /// another drive letter, or in another case or Unicode form on a file system that ignores them. False when either
    /// isn't a folder.
    /// </summary>
    bool IsSameFolder(string first, string second);

    /// <summary>
    /// The bytes this user may still write on the drive that holds <paramref name="folder"/>, which needn't exist yet;
    /// null when the system can't tell, such as for a network share on Windows.
    /// </summary>
    long? FreeSpace(string folder);

    /// <summary>
    /// Whether two folders, which needn't exist yet, are on the same drive, so a file moves from one to the other
    /// without being copied. False when the system can't tell.
    /// </summary>
    bool SameDrive(string first, string second);

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
