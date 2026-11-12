namespace ModShelf.Cli.Mcp;

/// <summary>
/// An installed game's games on a site.
/// </summary>
/// <param name="Picked">
/// True when the user chose them in the desktop app: the one game, or none. False when its name matched: one game, or
/// several sharing the name, for the user to choose.
/// </param>
internal sealed record InstalledSiteEntry(string Source, bool Picked, IReadOnlyList<GameEntry> Games);
