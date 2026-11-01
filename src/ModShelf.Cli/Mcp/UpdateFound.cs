namespace ModShelf.Cli.Mcp;

/// <summary>
/// An update of a watched mod.
/// </summary>
/// <param name="Changes">What the mod's changelog says of the update's version, when asked for; null otherwise.</param>
internal sealed record UpdateFound(
    string Source,
    string Game,
    string ModId,
    string ModName,
    LibraryUpdateEntry Update,
    IReadOnlyList<string>? Changes);
