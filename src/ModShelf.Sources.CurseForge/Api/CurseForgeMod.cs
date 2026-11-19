namespace ModShelf.Sources.CurseForge.Api;

/// <summary>
/// A mod on CurseForge, with what ModShelf reads of it.
/// </summary>
/// <param name="ClassId">Its class, such as Minecraft's Mods or Resource Packs.</param>
/// <param name="AllowModDistribution">
/// Whether its author lets apps download its files; when false, CurseForge gives no download link, and the files are
/// downloaded on its website.
/// </param>
internal sealed record CurseForgeMod(
    long Id,
    long GameId,
    string? Name,
    string? Slug,
    CurseForgeModLinks? Links,
    string? Summary,
    int Status,
    long DownloadCount,
    long? ClassId,
    IReadOnlyList<CurseForgeAuthor>? Authors,
    CurseForgeLogo? Logo,
    long MainFileId,
    DateTimeOffset? DateCreated,
    DateTimeOffset? DateModified,
    DateTimeOffset? DateReleased,
    bool? AllowModDistribution,
    bool IsAvailable);
