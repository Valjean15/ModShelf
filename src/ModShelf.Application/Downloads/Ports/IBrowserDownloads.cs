namespace ModShelf.Application.Downloads.Ports;

/// <summary>
/// The folder the browser saves downloads to, watched for the file of a download waiting on a site's page. Implemented
/// by Platform.
/// </summary>
/// <remarks>Where browsers save files unless told otherwise is <see cref="Settings.Ports.IUserFolders.Downloads"/>.</remarks>
public interface IBrowserDownloads
{
    /// <summary>
    /// The complete files that appear in <paramref name="folder"/> from now on, each by its full path, once: one a browser
    /// writes under a name of its own and renames, once renamed; one written in place, once its size stops changing.
    /// Files in folders inside it aren't watched. Ends when <paramref name="cancellationToken"/> is cancelled.
    /// </summary>
    /// <exception cref="DirectoryNotFoundException">There's no folder at <paramref name="folder"/>.</exception>
    IAsyncEnumerable<string> WatchAsync(string folder, CancellationToken cancellationToken);
}
