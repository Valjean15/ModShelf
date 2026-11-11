namespace ModShelf.Sources.DegMods.Catalog;

/// <summary>
/// A mod, as its newest event says it: one of kind 31142, or an older listing of kind 30402.
/// </summary>
internal sealed record DegMod
{
    /// <summary>
    /// The kind of the event it was read from.
    /// </summary>
    public required int Kind { get; init; }

    /// <summary>
    /// Its author's public key, in lowercase hexadecimal.
    /// </summary>
    public required string Author { get; init; }

    /// <summary>
    /// Its <c>d</c> tag, which names it among its author's mods.
    /// </summary>
    public required string D { get; init; }

    /// <summary>
    /// Its ID in ModShelf, <c>&lt;author&gt;:&lt;d&gt;</c>: the same whichever kind of event it's read from, as DEG Mods
    /// tells its mods apart.
    /// </summary>
    public string Key => ModEvents.Key(Author, D);

    public required string Title { get; init; }

    public string? Summary { get; init; }

    /// <summary>
    /// The game's name as the author picked it from DEG Mods' list, such as "Fallout: New Vegas".
    /// </summary>
    public required string Game { get; init; }

    public Uri? Picture { get; init; }

    /// <summary>
    /// Its tags, lowercase, each once.
    /// </summary>
    public required IReadOnlySet<string> Tags { get; init; }

    /// <summary>
    /// Whether its author marked it as content for adults, or DEG Mods' admin did.
    /// </summary>
    public bool IsAdult { get; init; }

    public required DateTimeOffset PublishedAt { get; init; }

    /// <summary>
    /// When its event was made: when it was last changed.
    /// </summary>
    public required DateTimeOffset UpdatedAt { get; init; }

    /// <summary>
    /// Its files, in the order the author listed them; the first is its main one.
    /// </summary>
    public required IReadOnlyList<DegDownload> Downloads { get; init; }
}
