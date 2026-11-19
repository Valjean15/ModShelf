namespace ModShelf.Sources.CurseForge;

/// <summary>
/// Where the CurseForge source reaches CurseForge, and how much of a mod's files it reads (ADR 0107).
/// </summary>
public sealed record CurseForgeOptions
{
    /// <summary>
    /// CurseForge's Core API, ending in a slash.
    /// </summary>
    public Uri ApiAddress { get; init; } = new("https://api.curseforge.com/");

    /// <summary>
    /// CurseForge's website, ending in a slash: its mods' and files' pages.
    /// </summary>
    public Uri WebAddress { get; init; } = new("https://www.curseforge.com/");

    /// <summary>
    /// The most pages of a mod's files read, 50 files a page, the newest first: a mod with thousands, one for each game
    /// version and loader, isn't read whole each check.
    /// </summary>
    public int MaxFilePages { get; init; } = 10;
}
