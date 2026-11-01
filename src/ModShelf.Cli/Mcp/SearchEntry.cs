namespace ModShelf.Cli.Mcp;

/// <summary>
/// A mod a search found.
/// </summary>
/// <param name="Link">The mod's page, which <c>mod_show</c> takes.</param>
internal sealed record SearchEntry(
    string Source,
    string Game,
    string ModId,
    string Name,
    string? Author,
    string? Summary,
    string? Version,
    DateTimeOffset? UpdatedAt,
    long? Endorsements,
    long? Downloads,
    bool Adult,
    bool Available,
    string? Link);
