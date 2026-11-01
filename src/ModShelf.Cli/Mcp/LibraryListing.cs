namespace ModShelf.Cli.Mcp;

/// <summary>
/// The library, under its root folder.
/// </summary>
internal sealed record LibraryListing(string Root, IReadOnlyList<LibraryModEntry> Mods);
