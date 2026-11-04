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
    /// Where Nexus Mods signs users in: its OAuth authorization server.
    /// </summary>
    public static readonly Uri DefaultUsersBaseAddress = new("https://users.nexusmods.com/");

    /// <summary>
    /// The API's address, ending in a slash. Tests point it at recorded responses.
    /// </summary>
    public Uri ApiBaseAddress { get; init; } = DefaultApiBaseAddress;

    /// <summary>
    /// The OAuth authorization server's address, ending in a slash. Tests point it elsewhere.
    /// </summary>
    public Uri UsersBaseAddress { get; init; } = DefaultUsersBaseAddress;

    /// <summary>
    /// The client ID Nexus Mods issues for ModShelf once the app is registered. Signing in through the browser is
    /// offered only when it's set; until then, accounts link with a personal API key. Never commit one.
    /// </summary>
    public string? OAuthClientId { get; init; }
}
