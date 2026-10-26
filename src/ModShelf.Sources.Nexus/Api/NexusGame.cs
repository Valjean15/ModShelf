namespace ModShelf.Sources.Nexus.Api;

/// <summary>
/// A game, from <c>v1/games/{game}.json</c>.
/// </summary>
/// <param name="NexusmodsUrl">The game's page, such as <c>https://www.nexusmods.com/stardewvalley</c>.</param>
internal sealed record NexusGame(string? Name, string? NexusmodsUrl);
