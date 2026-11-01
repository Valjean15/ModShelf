namespace ModShelf.Cli.Mcp;

/// <summary>
/// A mod as its site describes it, with its files, and whether the library watches it.
/// </summary>
/// <param name="HiddenFiles">How many old, archived or unsorted files aren't listed, unless asked for.</param>
/// <param name="Changelog">The mod's changelog, when asked for; null otherwise.</param>
internal sealed record ModDetails(
    string Source,
    string Game,
    string GameName,
    string ModId,
    string Name,
    string? Author,
    string? Version,
    string? Summary,
    string? Page,
    DateTimeOffset? UpdatedAt,
    bool Adult,
    bool Available,
    long? Downloads,
    long? Endorsements,
    bool Watched,
    IReadOnlyList<ModFileEntry> Files,
    int HiddenFiles,
    IReadOnlyList<ChangelogEntry>? Changelog);
