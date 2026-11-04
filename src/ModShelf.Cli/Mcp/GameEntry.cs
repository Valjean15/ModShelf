namespace ModShelf.Cli.Mcp;

/// <summary>
/// A site's game.
/// </summary>
/// <param name="Game">Its key on the site, which <c>mod_search</c>'s <c>game</c> takes.</param>
/// <param name="Page">The game's page on the site, when it has one.</param>
internal sealed record GameEntry(string Source, string Game, string Name, string? Page);
