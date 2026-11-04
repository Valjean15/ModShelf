namespace ModShelf.Infrastructure.Sources;

/// <summary>
/// How <c>AddSourceHttpClients()</c> retries and times the requests every source makes to its site's API.
/// </summary>
public sealed record SourceHttpOptions
{
    /// <summary>
    /// How many more times a request is sent when it failed for a passing reason: a server error, a timeout or a
    /// broken connection. A refusal or a site's rate limit is never retried.
    /// </summary>
    public int MaxRetryAttempts { get; init; } = 3;

    /// <summary>
    /// The wait before the first retry. It doubles for each retry after that, with some randomness.
    /// </summary>
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// How long one attempt may take, its answer read in full, before it counts as failed.
    /// </summary>
    public TimeSpan AttemptTimeout { get; init; } = TimeSpan.FromSeconds(30);
}
