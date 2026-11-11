namespace ModShelf.Application.Downloads;

/// <summary>
/// What came of handing a download waiting on a site's page the file the browser saved, with
/// <see cref="IDownloadService.TakeFileAsync"/>.
/// </summary>
public enum TakeFileOutcome
{
    /// <summary>The file is in the download's place, and the download completed.</summary>
    Taken,

    /// <summary>The file isn't the one: its hash isn't the site's. It's left where it was, and the download waits.</summary>
    HashMismatch,

    /// <summary>There's no file at the path. The download waits.</summary>
    NotFound,

    /// <summary>
    /// The file couldn't be read or put in place, such as with the disk full or the file still open. It's left where it
    /// was, and the download waits.
    /// </summary>
    StorageError,
}
