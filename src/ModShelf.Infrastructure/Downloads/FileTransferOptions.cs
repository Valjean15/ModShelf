namespace ModShelf.Infrastructure.Downloads;

/// <summary>
/// Settings for <see cref="HttpFileTransfer"/> and its HTTP client.
/// </summary>
public sealed record FileTransferOptions
{
    /// <summary>
    /// How long a download may go without receiving any data before the connection counts as dropped.
    /// </summary>
    public TimeSpan ReadTimeout { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// How many times one download call resumes after a dropped or stalled connection before giving up.
    /// </summary>
    public int MaxResumeAttempts { get; init; } = 3;

    /// <summary>
    /// How many times the HTTP pipeline retries a request that failed transiently (5xx, 408, 429,
    /// connection errors). Expired links are never retried.
    /// </summary>
    public int MaxRetryAttempts { get; init; } = 3;

    /// <summary>
    /// The first retry's delay. Later retries back off exponentially, with jitter.
    /// </summary>
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// The shortest time between two progress reports.
    /// </summary>
    public TimeSpan ProgressInterval { get; init; } = TimeSpan.FromMilliseconds(100);
}
