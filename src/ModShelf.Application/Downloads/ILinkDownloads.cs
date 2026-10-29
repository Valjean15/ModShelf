namespace ModShelf.Application.Downloads;

/// <summary>
/// Adds downloads from web links, such as a link the user pastes, by finding the source that recognizes it.
/// </summary>
public interface ILinkDownloads
{
    /// <summary>
    /// Queues the file <paramref name="link"/> points to, saved in <paramref name="targetDirectory"/>. When the
    /// link completes a handoff, such as an <c>nxm://</c> link, it's also handed to
    /// <see cref="IDownloadService.HandOffAsync"/>, which wakes a job for the file that is waiting for it.
    /// </summary>
    /// <param name="link">An absolute link.</param>
    /// <param name="targetDirectory">
    /// An absolute path. Jobs count as the same folder however it's written: with a trailing separator, or in another case
    /// where the system ignores case.
    /// </param>
    /// <param name="fileName">
    /// The name to save the file under, or null for the name the link suggests; either is made to work on every system, as
    /// the library's names are (see <see cref="Domain.Library.LibraryNames.File"/>).
    /// </param>
    /// <returns>
    /// The new job; or, when an unfinished job already downloads the same file under the same name to the
    /// same folder, that job, so that adding a link twice doesn't download it twice; or, when a finished job saved it
    /// there already and the file is still there, that job (<see cref="LinkDownload.AlreadySaved"/>).
    /// </returns>
    /// <exception cref="UnsupportedLinkException">No registered source recognizes the link.</exception>
    /// <exception cref="DownloadQueueBusyException">Another process runs the download queue.</exception>
    /// <exception cref="ArgumentException">
    /// The link is malformed, neither <paramref name="fileName"/> nor the link gives a file name, or another file is being
    /// downloaded to the same place.
    /// </exception>
    Task<LinkDownload> AddAsync(Uri link, string targetDirectory, string? fileName, CancellationToken cancellationToken);
}
