namespace ModShelf.Cli.Mcp;

/// <summary>
/// Part of a list that couldn't be imported.
/// </summary>
/// <param name="Source">The mod's site.</param>
/// <param name="Game">Its game, as the site names it.</param>
/// <param name="Mod">The mod's ID on its site.</param>
/// <param name="ModName">Its name, as listed.</param>
/// <param name="File">The file's ID; null when the whole mod was left out.</param>
/// <param name="Kind">Why: noSite, siteFailed, fileGone or notDone.</param>
/// <param name="Problem">What happened, in a sentence.</param>
internal sealed record ListProblemEntry(string Source, string Game, string Mod, string ModName, string? File, string Kind, string Problem);
