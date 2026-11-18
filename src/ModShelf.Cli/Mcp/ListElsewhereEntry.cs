namespace ModShelf.Cli.Mcp;

/// <summary>
/// Something a collection needs from outside its mods' site, which ModShelf doesn't download: the user gets it.
/// </summary>
/// <param name="Name">What it is, as the collection names it.</param>
/// <param name="Page">Where it's found; null when the collection gives no link.</param>
/// <param name="Optional">Whether the collection marks it optional.</param>
internal sealed record ListElsewhereEntry(string Name, string? Page, bool Optional);
