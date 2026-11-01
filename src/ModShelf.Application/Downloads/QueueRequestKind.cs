namespace ModShelf.Application.Downloads;

/// <summary>
/// What a <see cref="QueueRequest"/> asks of the download queue.
/// </summary>
public enum QueueRequestKind
{
    /// <summary>
    /// Downloads a link's file, as <see cref="Links.IOpenedLinks.OpenAsync"/> does: into the library, or on with an
    /// unfinished download of it outside.
    /// </summary>
    Download,

    Pause,

    Resume,

    Retry,

    /// <summary>
    /// Cancels the download and deletes the partial file it left; a finished file is never deleted.
    /// </summary>
    Cancel,
}
