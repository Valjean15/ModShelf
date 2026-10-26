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
    /// <param name="targetDirectory">An absolute path. Jobs count as the same folder only when this text matches exactly.</param>
    /// <param name="fileName">The name to save the file under, or null for the name the link suggests.</param>
    /// <returns>
    /// The new job; or, when an unfinished job already downloads the same file under the same name to the
    /// same folder, that job, so that adding a link twice doesn't download it twice.
    /// </returns>
    /// <exception cref="UnsupportedLinkException">No registered source recognizes the link.</exception>
    /// <exception cref="ArgumentException">
    /// The link is malformed, or neither <paramref name="fileName"/> nor the link gives a valid file name.
    /// </exception>
    Task<LinkDownload> AddAsync(Uri link, string targetDirectory, string? fileName, CancellationToken cancellationToken);
}
