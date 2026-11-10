namespace ModShelf.Sources.GameBanana;

/// <summary>
/// How <c>AddGameBananaSource()</c> sets up the GameBanana source.
/// </summary>
public sealed record GameBananaOptions
{
    /// <summary>
    /// GameBanana, whose API answers under <c>apiv11/</c>.
    /// </summary>
    public static readonly Uri DefaultBaseAddress = new("https://gamebanana.com/");

    /// <summary>
    /// GameBanana's address, ending in a slash. Tests point it at recorded answers.
    /// </summary>
    public Uri BaseAddress { get; init; } = DefaultBaseAddress;

    /// <summary>
    /// How many requests the source sends GameBanana at once, at most: GameBanana publishes no limits, so ModShelf keeps
    /// to a few.
    /// </summary>
    public int MaxRequestsAtOnce { get; init; } = 4;
}
