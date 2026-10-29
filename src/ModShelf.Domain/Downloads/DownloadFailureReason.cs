namespace ModShelf.Domain.Downloads;

/// <summary>
/// Why a <see cref="DownloadJob"/> failed. The UI turns each reason into a localized message;
/// technical details belong in the log.
/// </summary>
/// <remarks>The numeric values are stored, so never renumber them.</remarks>
public enum DownloadFailureReason
{
    /// <summary>The source couldn't provide a download link, for example because the file was removed.</summary>
    LinkUnavailable = 1,

    /// <summary>The user didn't start the download on the source's website in time.</summary>
    HandoffExpired = 2,

    /// <summary>The connection kept failing after the retries ran out.</summary>
    Network = 3,

    /// <summary>The file couldn't be written, for example because the disk is full.</summary>
    Storage = 4,

    /// <summary>The downloaded file doesn't match the hash the source published.</summary>
    HashMismatch = 5,

    /// <summary>Something else went wrong.</summary>
    Unexpected = 6,

    /// <summary>
    /// The source needs a linked account to download the file, or refused the one linked: the user has to link it.
    /// </summary>
    AccountNeeded = 7,

    /// <summary>
    /// The source's rate limit for the account is used up: the download can go on once it resets.
    /// </summary>
    RateLimited = 8,
}
