namespace ModShelf.Sources.Nexus;

/// <summary>
/// How <c>AddNexusSource()</c> sets up the Nexus Mods source.
/// </summary>
public sealed record NexusOptions
{
    /// <summary>
    /// The Nexus Mods API.
    /// </summary>
    public static readonly Uri DefaultApiBaseAddress = new("https://api.nexusmods.com/");

    /// <summary>
    /// The API's address, ending in a slash. Tests point it at recorded responses.
    /// </summary>
    public Uri ApiBaseAddress { get; init; } = DefaultApiBaseAddress;

    /// <summary>
    /// How many more times a request is sent when it failed for a passing reason: a server error, a timeout or a
    /// broken connection. A refusal or the rate limit is never retried.
    /// </summary>
    public int MaxRetryAttempts { get; init; } = 3;

    /// <summary>
    /// The wait before the first retry. It doubles for each retry after that, with some randomness.
    /// </summary>
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// How long one attempt may take before it counts as failed.
    /// </summary>
    public TimeSpan AttemptTimeout { get; init; } = TimeSpan.FromSeconds(30);
}
