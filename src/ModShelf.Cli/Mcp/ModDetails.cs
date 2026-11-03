namespace ModShelf.Cli.Mcp;

/// <summary>
/// A mod as its site describes it, with its files, and whether the library watches it.
/// </summary>
/// <param name="Pinned">Whether the library has the mod pinned at the version it has, so its updates aren't offered.</param>
/// <param name="Note">The user's note of the mod in the library; null for none.</param>
/// <param name="HiddenFiles">How many old, archived or unsorted files aren't listed, unless asked for.</param>
/// <param name="LibraryOnly">
/// The library's files of the mod that the site no longer lists, as a file its author pulled or hid, the newest first.
/// </param>
/// <param name="NewerInLibrary">Whether one of those is newer than every file the site lists.</param>
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
    bool Pinned,
    string? Note,
    IReadOnlyList<ModFileEntry> Files,
    int HiddenFiles,
    IReadOnlyList<LibraryFileEntry> LibraryOnly,
    bool NewerInLibrary,
    IReadOnlyList<ChangelogEntry>? Changelog);
