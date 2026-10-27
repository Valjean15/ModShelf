using ModShelf.Application.Downloads;
using ModShelf.Domain.Library;

namespace ModShelf.Application.Library;

/// <summary>
/// Downloads files into the library, and records them there once they're done.
/// </summary>
/// <remarks>
/// A download counts as the library's when it's saved under the library root, however it was added.
/// </remarks>
public interface ILibraryDownloads
{
    /// <summary>
    /// Queues the file a link is to, saved under the library root: in its mod's folder when the mod has files in the
    /// library, otherwise where its game's folder pattern says. A name another file has already gets the file's ID added.
    /// A link that completes a handoff is handed to the queue too.
    /// </summary>
    /// <remarks>
    /// Nothing is queued when the file is in the library and on disk already. A file already queued for the library
    /// keeps the place it was queued for.
    /// </remarks>
    /// <exception cref="UnsupportedLinkException">
    /// No source knows the link, or its site can't describe its mods, so they can't go into the library.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// The link is malformed, or is the site's but not to a mod or a file; or the site doesn't list the file among the
    /// mod's.
    /// </exception>
    /// <exception cref="IOException">
    /// The file can't go where it should: both names it could have are taken, or the path is too long for this system
    /// (a <see cref="PathTooLongException"/>).
    /// </exception>
    /// <exception cref="InvalidDataException">The settings can't be read.</exception>
    /// <exception cref="Sources.Abstractions.Accounts.AuthenticationFailedException">The site needs a linked account, or refused it.</exception>
    /// <exception cref="HttpRequestException">The site couldn't be reached, or refused; with 404 when there's no such mod or file.</exception>
    Task<LibraryDownload> AddAsync(Uri link, CancellationToken cancellationToken);

    /// <summary>
    /// Records a finished download saved under the library root as one of its mod's files. A mod new to the library is
    /// watched; one in it keeps its choice. Recording the same download again changes nothing.
    /// </summary>
    /// <returns>The file as the library has it; or null when the job isn't a finished download under the library root.</returns>
    /// <exception cref="ArgumentException">
    /// The site doesn't list the file among the mod's, or the file can't be in the library, such as one whose path holds
    /// a ':'.
    /// </exception>
    /// <exception cref="InvalidDataException">The settings can't be read.</exception>
    /// <exception cref="HttpRequestException">The mod's details were needed from its site, which couldn't be reached.</exception>
    Task<LibraryFile?> RecordAsync(DownloadJobSnapshot job, CancellationToken cancellationToken);

    /// <summary>
    /// Records the finished downloads under the library root that the library lacks and that are still on disk, such as
    /// one that finished just before the app stopped. Failures are logged, not thrown.
    /// </summary>
    Task RecordMissedAsync(CancellationToken cancellationToken);
}
