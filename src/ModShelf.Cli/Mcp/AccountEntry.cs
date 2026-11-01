namespace ModShelf.Cli.Mcp;

/// <summary>
/// A site's account in ModShelf: whether one is linked, and at what level, never whose or its key.
/// </summary>
/// <param name="Level">The account's level as the site names it, such as Premium; null when none is linked.</param>
/// <param name="LinkWith">How the user can link one, in the desktop app or the CLI.</param>
internal sealed record AccountEntry(
    string Source,
    string Site,
    bool Linked,
    string? Level,
    DateTimeOffset? LinkedAt,
    bool WorksWithoutAccount,
    IReadOnlyList<string> LinkWith);
