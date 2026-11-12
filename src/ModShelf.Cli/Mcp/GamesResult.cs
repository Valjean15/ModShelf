namespace ModShelf.Cli.Mcp;

/// <summary>
/// A page of a site's games whose names match: how many match, where the page starts, and whether there are more. With
/// <c>installed</c>, a page of the installed games instead, in <see cref="Installed"/>, <see cref="Games"/> empty.
/// </summary>
internal sealed record GamesResult(int Total, int Offset, bool HasMore, IReadOnlyList<GameEntry> Games)
{
    /// <summary>
    /// With <c>installed</c>, the stores found on this computer, such as Steam; none when none is.
    /// </summary>
    public IReadOnlyList<string>? Stores { get; init; }

    /// <summary>
    /// With <c>installed</c>, the page of installed games.
    /// </summary>
    public IReadOnlyList<InstalledGameEntry>? Installed { get; init; }

    /// <summary>
    /// With <c>installed</c>, why a site's games couldn't be read, so they match only as chosen.
    /// </summary>
    public IReadOnlyList<string>? Problems { get; init; }
}
