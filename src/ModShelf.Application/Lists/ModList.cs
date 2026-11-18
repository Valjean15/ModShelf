namespace ModShelf.Application.Lists;

/// <summary>
/// A list of mods and their files, as ModShelf exports it to a file and imports it back (ADR 0098): what to watch and
/// download, by the sites' own IDs, never by download links, which expire.
/// </summary>
/// <param name="Title">What the list is, such as a collection's name; null for a library's own export.</param>
/// <param name="CreatedAt">When it was made.</param>
/// <param name="Mods">Its mods, each once, in its order.</param>
public sealed record ModList(string? Title, DateTimeOffset CreatedAt, IReadOnlyList<ListedMod> Mods)
{
    /// <summary>
    /// What the list needs from outside its mods' sites, such as a collection's script extender: named, with its page,
    /// for the user to get; never downloaded. A list file has none: only a collection read from its site does.
    /// </summary>
    public IReadOnlyList<ListedElsewhere> Elsewhere { get; init; } = [];

    /// <summary>
    /// How many files the collection the list was read from names that its site no longer offers, so aren't listed.
    /// </summary>
    public int FilesUnavailable { get; init; }
}
