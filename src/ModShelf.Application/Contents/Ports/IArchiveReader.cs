namespace ModShelf.Application.Contents.Ports;

/// <summary>
/// Reads archives where they are: lists what they hold, and reads the start of one file in them. Nothing is ever
/// written, and an entry's path is never made into a path on disk (ADR 0106).
/// </summary>
public interface IArchiveReader
{
    /// <summary>
    /// The kind of archive a file is by its name, such as <c>zip</c>, <c>7z</c> or <c>rar</c>; null for a file this
    /// reader doesn't read.
    /// </summary>
    string? FormatOf(string fileName);

    /// <returns>Each entry as the archive lists it, its path with '/' between folders.</returns>
    /// <exception cref="ArchiveUnreadableException">The archive needs a password, or is damaged or isn't one.</exception>
    /// <exception cref="IOException">The file couldn't be read, such as when another program holds it.</exception>
    /// <exception cref="UnauthorizedAccessException">This user may not read the file.</exception>
    Task<IReadOnlyList<ArchiveEntry>> ListAsync(string path, CancellationToken cancellationToken);

    /// <summary>
    /// Reads the first <paramref name="maxBytes"/> bytes of the file at <paramref name="entry"/>, as
    /// <see cref="ListAsync"/> gives its path.
    /// </summary>
    /// <exception cref="KeyNotFoundException">The archive has no file at that path.</exception>
    /// <exception cref="ArchiveUnreadableException">The file needs a password, or the archive is damaged.</exception>
    /// <exception cref="IOException">The archive couldn't be read.</exception>
    /// <exception cref="UnauthorizedAccessException">This user may not read the archive.</exception>
    Task<ArchiveBytes> ReadAsync(string path, string entry, int maxBytes, CancellationToken cancellationToken);
}
