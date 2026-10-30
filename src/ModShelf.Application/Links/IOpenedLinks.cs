using ModShelf.Application.Downloads;

namespace ModShelf.Application.Links;

/// <summary>
/// Opens the links other apps hand ModShelf, such as an <c>nxm://</c> link from a website's "mod manager download"
/// button, which the system opens ModShelf with.
/// </summary>
public interface IOpenedLinks
{
    /// <summary>
    /// Downloads the file <paramref name="link"/> is to. An unfinished download of the file outside the library goes on
    /// with the link: one waiting for the user to start it on its site, paused or failed. Otherwise the file goes into
    /// the library, as <see cref="Library.ILibraryDownloads.AddAsync"/> puts it, which also goes on with an unfinished
    /// download of it there.
    /// </summary>
    /// <param name="link">An absolute link. It can carry a key, so it's never logged.</param>
    /// <exception cref="UnsupportedLinkException">
    /// No source knows the link, or its site can't describe its mods, so they can't go into the library.
    /// </exception>
    /// <exception cref="DownloadQueueBusyException">Another process runs the download queue.</exception>
    /// <exception cref="ArgumentException">
    /// The link isn't absolute, or is malformed, or is the site's but not to a mod or a file; or the site doesn't list the
    /// file among the mod's.
    /// </exception>
    /// <exception cref="IOException">The file can't go where it should in the library.</exception>
    /// <exception cref="InvalidDataException">The settings can't be read.</exception>
    /// <exception cref="Sources.Abstractions.Accounts.AuthenticationFailedException">The site needs a linked account, or refused it.</exception>
    /// <exception cref="HttpRequestException">The site couldn't be reached, or refused; with 404 when there's no such mod or file.</exception>
    Task<OpenedLink> OpenAsync(Uri link, CancellationToken cancellationToken);
}
