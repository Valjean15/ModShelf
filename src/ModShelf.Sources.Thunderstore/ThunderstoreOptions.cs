namespace ModShelf.Sources.Thunderstore;

/// <summary>
/// How <c>AddThunderstoreSource()</c> sets up the Thunderstore source.
/// </summary>
public sealed record ThunderstoreOptions
{
    /// <summary>
    /// Thunderstore, whose API answers under <c>api/</c> and each community's under <c>c/{community}/api/</c>.
    /// </summary>
    public static readonly Uri DefaultBaseAddress = new("https://thunderstore.io/");

    /// <summary>
    /// Thunderstore's address, ending in a slash. Tests point it at recorded answers. The listings' blobs are read from
    /// its host and the hosts under it, such as its CDN, <c>ccdn.thunderstore.io</c>, and only with its scheme.
    /// </summary>
    public Uri BaseAddress { get; init; } = DefaultBaseAddress;

    /// <summary>
    /// How many requests the source sends Thunderstore at once, at most: Thunderstore publishes no limits, so ModShelf
    /// keeps to a few.
    /// </summary>
    public int MaxRequestsAtOnce { get; init; } = 4;

    /// <summary>
    /// How long a community's listing is used before it's asked for again: Thunderstore rebuilds it once an hour.
    /// </summary>
    public TimeSpan ListingLifetime { get; init; } = TimeSpan.FromHours(1);
}
