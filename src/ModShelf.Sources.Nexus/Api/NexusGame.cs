namespace ModShelf.Sources.Nexus.Api;

/// <summary>
/// A game, from <c>v1/games/{game}.json</c>, or one of the list from <c>v1/games.json</c>.
/// </summary>
/// <param name="NexusmodsUrl">The game's page, such as <c>https://www.nexusmods.com/stardewvalley</c>.</param>
/// <param name="DomainName">The game's name in links, such as <c>stardewvalley</c>.</param>
/// <param name="Categories">The game's mod categories; the list of games gives them too, but they're read from the
/// game.</param>
/// <param name="Id">The game's number, such as 1303, which the v2 API names games by in mods' requirements.</param>
internal sealed record NexusGame(
    string? Name,
    string? NexusmodsUrl,
    string? DomainName = null,
    IReadOnlyList<NexusCategory?>? Categories = null,
    long? Id = null);
