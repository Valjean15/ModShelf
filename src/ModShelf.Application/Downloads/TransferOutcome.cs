namespace ModShelf.Application.Downloads;

/// <summary>
/// How a call to <see cref="IFileTransfer.DownloadAsync"/> ended.
/// </summary>
public enum TransferOutcome
{
    /// <summary>The file is at its destination and, when a hash was given, verified.</summary>
    Completed,

    /// <summary>The server rejected the ticket because it expired. Resolve a fresh one and try again.</summary>
    LinkExpired,

    /// <summary>The file's hash doesn't match the ticket's. The partial file was deleted.</summary>
    HashMismatch,

    /// <summary>The connection kept failing after the retries ran out.</summary>
    NetworkError,

    /// <summary>The file couldn't be written, for example because the disk is full.</summary>
    StorageError,
}
