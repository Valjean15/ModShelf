namespace ModShelf.Cli.Mcp;

/// <summary>
/// A page of the library's mods, under its root folder.
/// </summary>
/// <param name="Total">How many mods match, on every page.</param>
/// <param name="Offset">How many of them come before this page.</param>
/// <param name="HasMore">Whether more come after it: ask again with the next offset.</param>
internal sealed record LibraryListing(string Root, int Total, int Offset, bool HasMore, IReadOnlyList<LibraryModEntry> Mods);
