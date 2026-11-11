namespace ModShelf.Sources.ModIo;

/// <summary>
/// How <c>AddModIoSource()</c> sets up the mod.io source.
/// </summary>
public sealed record ModIoOptions
{
    /// <summary>
    /// mod.io's API for what isn't one game's: the games, the account, its subscriptions.
    /// </summary>
    public static readonly Uri DefaultApiAddress = new("https://api.mod.io/v1/");

    /// <summary>
    /// Each game's own API, as mod.io asks apps to call it: <c>{0}</c> is the game's ID.
    /// </summary>
    public const string DefaultGameApiAddress = "https://g-{0}.modapi.io/v1/";

    /// <summary>
    /// The API for what isn't one game's, ending in a slash. Tests point it at recorded answers.
    /// </summary>
    public Uri ApiAddress { get; init; } = DefaultApiAddress;

    /// <summary>
    /// A game's API, ending in a slash, with <c>{0}</c> where the game's ID goes. Tests point it at recorded answers,
    /// without the <c>{0}</c>.
    /// </summary>
    public string GameApiAddress { get; init; } = DefaultGameApiAddress;

    /// <summary>
    /// How many requests the source sends mod.io a minute, at most: mod.io allows an API key 60, and a token 120.
    /// </summary>
    public int RequestsPerMinute { get; init; } = 60;
}
